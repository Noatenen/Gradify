using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Data;

/// <summary>
/// Fetches project records from Airtable and upserts them into the local DB.
///
/// Configuration is supplied per-call via <see cref="AirtableOptions"/>:
/// the controller resolves the active <see cref="AirtableIntegrationSettings"/>
/// row, builds an <see cref="AirtableOptions"/>, and invokes the per-row
/// overload. The legacy "no-args, look up active" overload was removed
/// on 2026-06-04 — every Airtable import now has to flow through the
/// audited controller path so AirtableImportRuns receives a row.
/// </summary>
public class AirtableService
{
    private const string AirtableApiBase = "https://api.airtable.com/v0";

    private readonly IHttpClientFactory       _httpFactory;
    private readonly DbRepository             _db;
    private readonly IConfiguration           _config;
    private readonly ILogger<AirtableService> _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AirtableService(
        IHttpClientFactory       httpFactory,
        DbRepository             db,
        IConfiguration           config,
        ILogger<AirtableService> log)
    {
        _httpFactory = httpFactory;
        _db          = db;
        _config      = config;
        _log         = log;
    }

    // ── Public entry points ──────────────────────────────────────────────────
    //
    // The no-args SyncProjectsAsync() overload that used to live here was
    // removed: it bypassed the audited controller (no AirtableImportRuns
    // row written) and was reachable from the now-410'd
    // /api/airtable/sync-projects endpoint. Callers must invoke the
    // explicit-options overload below via AirtableIntegrationController.Import,
    // which writes the audit row + per-integration LastImportAt/Summary.

    /// <summary>Runs the sync with an explicit configuration.
    /// <paramref name="skipRecordIds"/> is the optional opt-out list — Airtable
    /// records whose id is in this set are counted as Skipped and never
    /// touched on the local side. Used by the preview→confirm workflow so
    /// the admin can deselect suspected duplicates before committing.</summary>
    public async Task<AirtableSyncResultDto> SyncProjectsAsync(
        AirtableOptions options,
        HashSet<string>? skipRecordIds = null)
    {
        if (!options.IsConfigured)
        {
            return new AirtableSyncResultDto
            {
                SyncError = "תצורת Airtable אינה מלאה (Token / BaseId / ProjectsTable חסרים)."
            };
        }

        // Logs use BaseId/Table only — never the token.
        _log.LogInformation(
            "Starting Airtable sync — base: {BaseId}, table: {Table}, view: {View}",
            options.BaseId, options.TableName,
            string.IsNullOrWhiteSpace(options.ViewName) ? "(all records)" : options.ViewName);

        List<AirtableRecord> records;
        try
        {
            records = await FetchAllRecordsAsync(options);
            _log.LogInformation("Fetched {Count} raw records from Airtable.", records.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to fetch records from Airtable.");
            return new AirtableSyncResultDto
            {
                SyncError = $"שגיאה בגישה ל-Airtable: {ex.Message}"
            };
        }

        var fm = options.FieldMap;
        if (options.StudentVisibleOnly && !string.IsNullOrWhiteSpace(fm.IncludeInPool))
        {
            int before = records.Count;
            records = records
                .Where(r => string.Equals(
                    GetString(r.Fields, fm.IncludeInPool), "true",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            int skipped = before - records.Count;
            if (skipped > 0)
                _log.LogInformation(
                    "{Skipped} records excluded by IncludeInPool filter (field: \"{Field}\").",
                    skipped, fm.IncludeInPool);
        }

        var result = new AirtableSyncResultDto { TotalFetched = records.Count };

        var typeRows = await _db.GetRecordsAsync<ProjectTypeRow>(
            "SELECT Id, Name FROM ProjectTypes");
        var typesByName = typeRows?
            .ToDictionary(t => t.Name.ToLowerInvariant(), t => t.Id)
            ?? new Dictionary<string, int>();

        int currentYearId = (await _db.GetRecordsAsync<int>(
            "SELECT COALESCE(Id, 0) FROM AcademicYears WHERE IsCurrent = 1 LIMIT 1"))
            .FirstOrDefault();

        if (currentYearId == 0)
            _log.LogWarning("No current AcademicYear found (IsCurrent = 1). New Airtable projects will have AcademicYearId = 0.");

        var counter = new Counter
        {
            Value = (await _db.GetRecordsAsync<int>(
                "SELECT COALESCE(MAX(ProjectNumber), 0) FROM Projects")).FirstOrDefault()
        };

        foreach (var record in records)
        {
            // Admin-chosen skip list (per-row opt-out from the preview UI).
            // Counted as Skipped, never processed. Distinct from Failed so
            // the audit row makes the admin's intent explicit.
            if (skipRecordIds is not null
                && !string.IsNullOrEmpty(record.Id)
                && skipRecordIds.Contains(record.Id))
            {
                result.Skipped++;
                continue;
            }

            try
            {
                await UpsertRecordAsync(options, record, typesByName, counter, currentYearId, result);
            }
            catch (Exception ex)
            {
                result.Failed++;

                string rootMsg = ex.InnerException?.Message ?? ex.Message;
                string detail  = $"[{ex.GetType().Name}] {ex.Message}" +
                                 (ex.InnerException is not null
                                     ? $" → {ex.InnerException.Message}"
                                     : "");

                string title  = GetString(record.Fields, options.FieldMap.Title);
                int    num    = GetInt   (record.Fields, options.FieldMap.ProjectNumber);

                result.Errors.Add($"Record {record.Id} (#{num} \"{title}\"): {rootMsg}");

                _log.LogError(ex,
                    "Upsert failed — record: {RecordId}, projectNumber: {Num}, title: \"{Title}\". {Detail}",
                    record.Id, num, title, detail);
            }
        }

        _log.LogInformation(
            "Airtable sync complete — fetched: {Fetched}, inserted: {Inserted}, updated: {Updated}, failed: {Failed}.",
            result.TotalFetched, result.Inserted, result.Updated, result.Failed);

        if (result.Failed > 0 && result.Inserted == 0 && result.Updated == 0
            && result.Errors.Count > 0)
        {
            result.SyncError =
                $"כל {result.Failed} הרשומות נכשלו. דוגמה לשגיאה ראשונה: {result.Errors[0]}";
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PreviewProjectsAsync — read-only dry-run for the admin UI
    //
    //  Reuses the same Airtable fetch + StudentVisibleOnly filter that the
    //  real sync uses, then categorises each record into one of four buckets
    //  without any DB writes:
    //
    //    New     — no existing Projects row has this AirtableRecordId
    //    Update  — row exists; at least one mapped field differs
    //    Warning — row will be created/updated, but data is suspect
    //              (missing title, unmapped project type, no name, etc.)
    //    Error   — record cannot be imported at all (no Airtable id, etc.)
    //
    //  The Update bucket also lists the Hebrew field labels that would
    //  change, so the admin can confirm a no-op import doesn't actually
    //  bump a field they didn't expect.
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<AirtablePreviewResultDto> PreviewProjectsAsync(AirtableOptions options)
    {
        if (!options.IsConfigured)
        {
            return new AirtablePreviewResultDto
            {
                PreviewError = "תצורת Airtable אינה מלאה (Token / BaseId / ProjectsTable חסרים)."
            };
        }

        List<AirtableRecord> records;
        try
        {
            records = await FetchAllRecordsAsync(options);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Preview: failed to fetch records from Airtable.");
            return new AirtablePreviewResultDto
            {
                PreviewError = $"שגיאה בגישה ל-Airtable: {ex.Message}"
            };
        }

        var fm = options.FieldMap;
        if (options.StudentVisibleOnly && !string.IsNullOrWhiteSpace(fm.IncludeInPool))
        {
            records = records
                .Where(r => string.Equals(
                    GetString(r.Fields, fm.IncludeInPool), "true",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var result = new AirtablePreviewResultDto { TotalFetched = records.Count };

        // Pull every (AirtableRecordId → existing-row fields) we might compare
        // against in one round-trip, keyed by record id. Avoids N+1 lookups.
        var existingByRecordId = new Dictionary<string, ExistingProjectRow>(StringComparer.Ordinal);
        if (records.Count > 0)
        {
            var rows = await _db.GetRecordsAsync<ExistingProjectRow>(@"
                SELECT  Id, AirtableRecordId,
                        COALESCE(Title,'')            AS Title,
                        COALESCE(Description,'')      AS Description,
                        ProjectTypeId,
                        COALESCE(Status,'')           AS Status,
                        COALESCE(OrganizationName,'') AS OrganizationName,
                        COALESCE(OrganizationType,'') AS OrganizationType,
                        COALESCE(ProjectTopic,'')     AS ProjectTopic,
                        COALESCE(Contents,'')         AS Contents,
                        COALESCE(ContactPerson,'')    AS ContactPerson,
                        COALESCE(ContactRole,'')      AS ContactRole,
                        COALESCE(ContactEmail,'')     AS ContactEmail,
                        COALESCE(ContactPhone,'')     AS ContactPhone,
                        COALESCE(Goals,'')            AS Goals,
                        COALESCE(TargetAudience,'')   AS TargetAudience,
                        COALESCE(Priority,'')         AS Priority
                FROM    Projects
                WHERE   AirtableRecordId IS NOT NULL AND AirtableRecordId <> ''");
            if (rows is not null)
                foreach (var r in rows)
                    if (!string.IsNullOrEmpty(r.AirtableRecordId))
                        existingByRecordId[r.AirtableRecordId] = r;
        }

        // Suspect-duplicate index: look up local rows by normalised title and
        // by ProjectNumber so the analyser can flag a "New" record that looks
        // suspiciously like an existing project. The AirtableRecordId match
        // is the authoritative dedupe key — this is a warning layer on top.
        // Pulls ALL projects (not just Airtable-sourced) so manual catalog
        // entries also get the collision check.
        var existingByTitle  = new Dictionary<string, ExistingByTitleRow>(StringComparer.Ordinal);
        var existingByNumber = new Dictionary<int, ExistingByTitleRow>();
        {
            var rows = await _db.GetRecordsAsync<ExistingByTitleRow>(@"
                SELECT  Id, ProjectNumber, COALESCE(Title,'') AS Title,
                        COALESCE(AirtableRecordId,'') AS AirtableRecordId
                FROM    Projects");
            if (rows is not null)
            {
                foreach (var r in rows)
                {
                    string key = NormaliseTitle(r.Title);
                    if (key.Length > 0 && !existingByTitle.ContainsKey(key))
                        existingByTitle[key] = r;
                    if (r.ProjectNumber > 0 && !existingByNumber.ContainsKey(r.ProjectNumber))
                        existingByNumber[r.ProjectNumber] = r;
                }
            }
        }

        // Cache project-type lookup so warnings on unmapped types are cheap.
        var typeRows = await _db.GetRecordsAsync<ProjectTypeRow>(
            "SELECT Id, Name FROM ProjectTypes");
        var typesByName = typeRows?
            .ToDictionary(t => t.Name.ToLowerInvariant(), t => t.Id)
            ?? new Dictionary<string, int>();

        foreach (var record in records)
        {
            var row = AnalyzeRecord(
                record, options, typesByName,
                existingByRecordId, existingByTitle, existingByNumber);
            result.Rows.Add(row);
            switch (row.Kind)
            {
                case AirtablePreviewKinds.New:     result.NewCount++;     break;
                case AirtablePreviewKinds.Update:  result.UpdateCount++;  break;
                case AirtablePreviewKinds.Warning: result.WarningCount++; break;
                case AirtablePreviewKinds.Error:   result.ErrorCount++;   break;
            }
        }

        return result;
    }

    /// <summary>Case-insensitive, whitespace-collapsed title key. Used for
    /// the "looks like an existing project" warning — not for dedupe (the
    /// authoritative key is AirtableRecordId).</summary>
    private static string NormaliseTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var s = title.Trim().ToLowerInvariant();
        // Collapse runs of whitespace to single spaces so " A  B " == "a b".
        var sb = new System.Text.StringBuilder(s.Length);
        bool prevSpace = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(ch);
                prevSpace = false;
            }
        }
        return sb.ToString();
    }

    private sealed class ExistingByTitleRow
    {
        public int    Id               { get; set; }
        public int    ProjectNumber    { get; set; }
        public string Title            { get; set; } = "";
        public string AirtableRecordId { get; set; } = "";
    }

    /// <summary>Pure function — read fields from one Airtable record, compare
    /// against the local DB state, and classify into one of four buckets.
    /// No I/O.</summary>
    private static AirtablePreviewRowDto AnalyzeRecord(
        AirtableRecord record,
        AirtableOptions options,
        Dictionary<string, int> typesByName,
        Dictionary<string, ExistingProjectRow> existingByRecordId,
        Dictionary<string, ExistingByTitleRow> existingByTitle,
        Dictionary<int, ExistingByTitleRow> existingByNumber)
    {
        var fm = options.FieldMap;
        var f  = record.Fields;

        string title    = GetString(f, fm.Title);
        string orgName  = GetString(f, fm.OrganizationName);
        int    number   = GetInt(f, fm.ProjectNumber);

        var row = new AirtablePreviewRowDto
        {
            RecordId         = record.Id,
            Title            = string.IsNullOrWhiteSpace(title) ? "(ללא כותרת)" : title,
            OrganizationName = orgName,
            ProjectNumber    = number > 0 ? number : null,
        };

        // ── Error: record itself can't be imported at all ────────────────
        if (string.IsNullOrWhiteSpace(record.Id))
        {
            row.Kind   = AirtablePreviewKinds.Error;
            row.Detail = "חסר מזהה רשומה ב-Airtable";
            return row;
        }

        // ── Existing? ────────────────────────────────────────────────────
        existingByRecordId.TryGetValue(record.Id, out var existing);

        // Collect soft warnings — these don't block, but the admin should see.
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(title))
            warnings.Add("חסרה כותרת");
        if (string.IsNullOrWhiteSpace(orgName))
            warnings.Add("חסר שם ארגון");

        string orgType = GetString(f, fm.OrganizationType);
        if (!string.IsNullOrWhiteSpace(orgType) && !typesByName.ContainsKey(orgType.ToLowerInvariant()))
            warnings.Add($"סוג ארגון לא ידוע: \"{orgType}\"");

        // ── New rows ─────────────────────────────────────────────────────
        if (existing is null)
        {
            // Suspected-duplicate check: AirtableRecordId didn't match an
            // existing row, but the local DB has a project with the same
            // ProjectNumber OR a near-identical title. That's almost
            // certainly a manually-created twin of an Airtable record —
            // import would create a second project with the same name.
            string normalisedTitle = NormaliseTitle(title);
            if (number > 0 && existingByNumber.TryGetValue(number, out var twinByNum)
                && twinByNum.AirtableRecordId != record.Id)
            {
                warnings.Add(
                    $"כפילות חשודה — מספר פרויקט #{number} כבר קיים (\"{Truncate(twinByNum.Title, 50)}\"). " +
                    "הייבוא ייצור פרויקט חדש עם מספר אחר.");
            }
            else if (normalisedTitle.Length > 0
                     && existingByTitle.TryGetValue(normalisedTitle, out var twinByTitle)
                     && twinByTitle.AirtableRecordId != record.Id)
            {
                string twinTag = twinByTitle.ProjectNumber > 0
                    ? $"#{twinByTitle.ProjectNumber}"
                    : $"id {twinByTitle.Id}";
                warnings.Add(
                    $"כפילות חשודה — כותרת זהה לפרויקט קיים {twinTag}. " +
                    "אם זו אותה הצעה, יש לשייך AirtableRecordId לפרויקט הקיים לפני הייבוא.");
            }

            row.Kind   = warnings.Count > 0 ? AirtablePreviewKinds.Warning : AirtablePreviewKinds.New;
            row.Detail = warnings.Count > 0 ? string.Join(" · ", warnings) : "";
            return row;
        }

        // ── Update rows: diff each mapped field ──────────────────────────
        row.ExistingProjectId = existing.Id;

        var changes = new List<string>();
        if (!StrEq(existing.Title,            title))                                    changes.Add("כותרת");
        if (!StrEq(existing.Description,      GetString(f, fm.Description)))             changes.Add("תיאור");
        if (!StrEq(existing.Status,           GetString(f, fm.Status)))                  changes.Add("סטטוס");
        if (!StrEq(existing.OrganizationName, orgName))                                  changes.Add("שם ארגון");
        if (!StrEq(existing.OrganizationType, orgType))                                  changes.Add("סוג ארגון");
        if (!StrEq(existing.ProjectTopic,     GetString(f, fm.ProjectTopic)))            changes.Add("נושא");
        if (!StrEq(existing.Contents,         GetString(f, fm.Contents)))                changes.Add("תכנים");
        if (!StrEq(existing.ContactPerson,    GetString(f, fm.ContactPerson)))           changes.Add("איש קשר");
        if (!StrEq(existing.ContactRole,      GetString(f, fm.ContactRole)))             changes.Add("תפקיד איש קשר");
        if (!StrEq(existing.ContactEmail,     GetString(f, fm.ContactEmail)))            changes.Add("דוא״ל");
        if (!StrEq(existing.ContactPhone,     GetString(f, fm.ContactPhone)))            changes.Add("טלפון");
        if (!StrEq(existing.Goals,            GetString(f, fm.Goals)))                   changes.Add("מטרות");
        if (!StrEq(existing.TargetAudience,   GetString(f, fm.TargetAudience)))          changes.Add("קהל יעד");
        if (!StrEq(existing.Priority,         GetString(f, fm.Priority)))                changes.Add("עדיפות");

        if (warnings.Count > 0)
        {
            // Soft warning trumps a clean Update so the admin sees the issue.
            row.Kind   = AirtablePreviewKinds.Warning;
            row.Detail = string.Join(" · ", warnings) +
                         (changes.Count > 0 ? "  ·  שדות שישתנו: " + string.Join(", ", changes) : "");
        }
        else if (changes.Count > 0)
        {
            row.Kind   = AirtablePreviewKinds.Update;
            row.Detail = "שדות שישתנו: " + string.Join(", ", changes);
        }
        else
        {
            // No-op update — row exists, nothing differs. Still surface as
            // "Update" with zero fields so the count math is honest, but
            // mark the detail so the admin sees it.
            row.Kind   = AirtablePreviewKinds.Update;
            row.Detail = "אין שינוי בשדות (יעודכן LastSyncedAt בלבד)";
        }

        return row;
    }

    private static bool StrEq(string? a, string? b)
        => string.Equals(a ?? "", b ?? "", StringComparison.Ordinal);

    private sealed class ExistingProjectRow
    {
        public int    Id               { get; set; }
        public string AirtableRecordId { get; set; } = "";
        public string Title            { get; set; } = "";
        public string Description      { get; set; } = "";
        public int    ProjectTypeId    { get; set; }
        public string Status           { get; set; } = "";
        public string OrganizationName { get; set; } = "";
        public string OrganizationType { get; set; } = "";
        public string ProjectTopic     { get; set; } = "";
        public string Contents         { get; set; } = "";
        public string ContactPerson    { get; set; } = "";
        public string ContactRole      { get; set; } = "";
        public string ContactEmail     { get; set; } = "";
        public string ContactPhone     { get; set; } = "";
        public string Goals            { get; set; } = "";
        public string TargetAudience   { get; set; } = "";
        public string Priority         { get; set; } = "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PreviewFixtureAsync — dev/QA only
    //
    //  Runs the same AnalyzeRecord pipeline against a posted set of mock
    //  Airtable records, without making any Airtable HTTP call and without
    //  writing to the DB. Lets QA verify each of the four preview buckets
    //  ("New", "Update", "Warning", "Error") fires correctly without
    //  needing access to the live Airtable base.
    //
    //  Reuses the same field-by-field diff logic as PreviewProjectsAsync,
    //  so a "Title-changed" signal in this fixture produces identical
    //  Detail text to a real Airtable record whose title changed.
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<AirtablePreviewResultDto> PreviewFixtureAsync(
        AirtableOptions options,
        List<AirtableFixtureRecordDto> fixtureRecords)
    {
        var result = new AirtablePreviewResultDto
        {
            TotalFetched = fixtureRecords?.Count ?? 0,
        };
        if (fixtureRecords is null || fixtureRecords.Count == 0) return result;

        // Build the same three indexes the real preview uses.
        var existingByRecordId = new Dictionary<string, ExistingProjectRow>(StringComparer.Ordinal);
        var existingByTitle    = new Dictionary<string, ExistingByTitleRow>(StringComparer.Ordinal);
        var existingByNumber   = new Dictionary<int, ExistingByTitleRow>();

        var rows = await _db.GetRecordsAsync<ExistingProjectRow>(@"
            SELECT  Id, AirtableRecordId,
                    COALESCE(Title,'')            AS Title,
                    COALESCE(Description,'')      AS Description,
                    ProjectTypeId,
                    COALESCE(Status,'')           AS Status,
                    COALESCE(OrganizationName,'') AS OrganizationName,
                    COALESCE(OrganizationType,'') AS OrganizationType,
                    COALESCE(ProjectTopic,'')     AS ProjectTopic,
                    COALESCE(Contents,'')         AS Contents,
                    COALESCE(ContactPerson,'')    AS ContactPerson,
                    COALESCE(ContactRole,'')      AS ContactRole,
                    COALESCE(ContactEmail,'')     AS ContactEmail,
                    COALESCE(ContactPhone,'')     AS ContactPhone,
                    COALESCE(Goals,'')            AS Goals,
                    COALESCE(TargetAudience,'')   AS TargetAudience,
                    COALESCE(Priority,'')         AS Priority
            FROM    Projects
            WHERE   AirtableRecordId IS NOT NULL AND AirtableRecordId <> ''");
        if (rows is not null)
            foreach (var r in rows)
                if (!string.IsNullOrEmpty(r.AirtableRecordId))
                    existingByRecordId[r.AirtableRecordId] = r;

        var titleRows = await _db.GetRecordsAsync<ExistingByTitleRow>(@"
            SELECT  Id, ProjectNumber, COALESCE(Title,'') AS Title,
                    COALESCE(AirtableRecordId,'') AS AirtableRecordId
            FROM    Projects");
        if (titleRows is not null)
        {
            foreach (var r in titleRows)
            {
                string key = NormaliseTitle(r.Title);
                if (key.Length > 0 && !existingByTitle.ContainsKey(key))
                    existingByTitle[key] = r;
                if (r.ProjectNumber > 0 && !existingByNumber.ContainsKey(r.ProjectNumber))
                    existingByNumber[r.ProjectNumber] = r;
            }
        }

        var typeRows = await _db.GetRecordsAsync<ProjectTypeRow>(
            "SELECT Id, Name FROM ProjectTypes");
        var typesByName = typeRows?
            .ToDictionary(t => t.Name.ToLowerInvariant(), t => t.Id)
            ?? new Dictionary<string, int>();

        // Translate each fixture row into the shape AnalyzeRecord expects
        // (an AirtableRecord with a Fields dictionary keyed by the
        // integration's FieldMap names) so the SAME analyser fires. Field
        // values must be JsonElement to match the production-record shape.
        var fm = options.FieldMap;
        static void SetStr(Dictionary<string, JsonElement> d, string key, string val)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            d[key] = JsonSerializer.SerializeToElement(val ?? "");
        }
        static void SetInt(Dictionary<string, JsonElement> d, string key, int val)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            d[key] = JsonSerializer.SerializeToElement(val);
        }

        foreach (var fx in fixtureRecords)
        {
            var fields = new Dictionary<string, JsonElement>();
            SetStr(fields, fm.Title,            fx.Title);
            if (fx.ProjectNumber.HasValue) SetInt(fields, fm.ProjectNumber, fx.ProjectNumber.Value);
            SetStr(fields, fm.OrganizationName, fx.OrganizationName);
            SetStr(fields, fm.OrganizationType, fx.OrganizationType);
            SetStr(fields, fm.Description,      fx.Description);
            SetStr(fields, fm.Status,           fx.Status);
            SetStr(fields, fm.ProjectTopic,     fx.ProjectTopic);
            SetStr(fields, fm.Contents,         fx.Contents);
            SetStr(fields, fm.ContactPerson,    fx.ContactPerson);
            SetStr(fields, fm.ContactRole,      fx.ContactRole);
            SetStr(fields, fm.ContactEmail,     fx.ContactEmail);
            SetStr(fields, fm.ContactPhone,     fx.ContactPhone);
            SetStr(fields, fm.Goals,            fx.Goals);
            SetStr(fields, fm.TargetAudience,   fx.TargetAudience);
            SetStr(fields, fm.Priority,         fx.Priority);

            var record = new AirtableRecord { Id = fx.RecordId ?? "", Fields = fields };

            var row = AnalyzeRecord(
                record, options, typesByName,
                existingByRecordId, existingByTitle, existingByNumber);
            result.Rows.Add(row);
            switch (row.Kind)
            {
                case AirtablePreviewKinds.New:     result.NewCount++;     break;
                case AirtablePreviewKinds.Update:  result.UpdateCount++;  break;
                case AirtablePreviewKinds.Warning: result.WarningCount++; break;
                case AirtablePreviewKinds.Error:   result.ErrorCount++;   break;
            }
        }

        return result;
    }

    /// <summary>Read-only connection check — performs a minimal request and returns sample count.</summary>
    public async Task<AirtableTestResultDto> TestConnectionAsync(AirtableOptions options)
    {
        if (!options.IsConfigured)
        {
            return new AirtableTestResultDto
            {
                Success = false,
                Message = "תצורת Airtable אינה מלאה (Token / BaseId / ProjectsTable חסרים)."
            };
        }

        try
        {
            var url = $"{AirtableApiBase}/{Uri.EscapeDataString(options.BaseId)}" +
                      $"/{Uri.EscapeDataString(options.TableName)}?maxRecords=1";

            if (!string.IsNullOrWhiteSpace(options.ViewName))
                url += $"&view={Uri.EscapeDataString(options.ViewName)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);

            var client = _httpFactory.CreateClient("Airtable");
            var resp   = await client.SendAsync(request);

            if (!resp.IsSuccessStatusCode)
            {
                string body = await resp.Content.ReadAsStringAsync();
                _log.LogWarning("Airtable test connection returned {Status}: {Body}",
                    (int)resp.StatusCode, Truncate(body, 400));
                return new AirtableTestResultDto
                {
                    Success    = false,
                    Message    = $"Airtable החזיר סטטוס {(int)resp.StatusCode} — {ExplainStatus(resp.StatusCode)}",
                    Diagnostic = Truncate(body, 400)
                };
            }

            var json = await resp.Content.ReadAsStringAsync();
            var page = JsonSerializer.Deserialize<AirtableListResponse>(json, JsonOpts);
            int count = page?.Records?.Count ?? 0;

            return new AirtableTestResultDto
            {
                Success     = true,
                Message     = "החיבור לאיירטייבל הצליח",
                SampleCount = count
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Airtable test connection threw.");
            return new AirtableTestResultDto
            {
                Success = false,
                Message = $"שגיאה בחיבור: {ex.Message}"
            };
        }
    }

    // ── Configuration loading ────────────────────────────────────────────────

    /// <summary>
    /// Picks the active Airtable config for the current academic year.
    /// Returns null when no current cycle exists OR no integration row is
    /// marked IsActive=1 for that cycle. The DB is the SOLE source — the
    /// legacy appsettings.json "Airtable" section is no longer consulted
    /// (it is bootstrap-migrated into the DB by
    /// DatabaseMigrator.EnsureAirtableSeedFromAppsettingsAsync on first
    /// boot). Configuring a new academic year now means: create a cycle,
    /// mark it current, then add an AirtableIntegrationSettings row from
    /// /management/integrations/airtable — no code or appsettings edits.
    /// </summary>
    public async Task<AirtableOptions?> ResolveActiveOptionsAsync()
    {
        var rows = await _db.GetRecordsAsync<int>(
            "SELECT Id FROM AirtableIntegrationSettings WHERE IsActive = 1 " +
            "AND AcademicYearId IN (SELECT Id FROM AcademicYears WHERE IsCurrent = 1) LIMIT 1");
        int settingsId = rows?.FirstOrDefault() ?? 0;

        return settingsId > 0 ? await LoadOptionsAsync(settingsId) : null;
    }

    /// <summary>Builds an <see cref="AirtableOptions"/> from the saved DB rows for a given integration id.</summary>
    public async Task<AirtableOptions?> LoadOptionsAsync(int settingsId)
    {
        var settings = (await _db.GetRecordsAsync<AirtableSettingsRow>(@"
            SELECT  Id, ApiToken, BaseId, ProjectsTable, ProjectsView, StudentVisibleOnly
            FROM    AirtableIntegrationSettings
            WHERE   Id = @Id LIMIT 1",
            new { Id = settingsId }))?.FirstOrDefault();

        if (settings is null) return null;

        var mappingRows = await _db.GetRecordsAsync<MappingRow>(@"
            SELECT  LocalFieldName, AirtableFieldName
            FROM    AirtableFieldMappings
            WHERE   IntegrationSettingsId = @Id AND EntityType = 'Project'",
            new { Id = settingsId });

        var fm = new AirtableFieldMap();
        if (mappingRows is not null)
        {
            foreach (var m in mappingRows)
            {
                if (string.IsNullOrWhiteSpace(m.AirtableFieldName)) continue;
                ApplyMapping(fm, m.LocalFieldName, m.AirtableFieldName);
            }
        }

        return new AirtableOptions
        {
            Token              = settings.ApiToken,
            BaseId             = settings.BaseId,
            TableName          = settings.ProjectsTable,
            ViewName           = settings.ProjectsView,
            StudentVisibleOnly = settings.StudentVisibleOnly,
            FieldMap           = fm
        };
    }

    private static void ApplyMapping(AirtableFieldMap fm, string localField, string airtableField)
    {
        switch (localField)
        {
            case AirtableProjectFields.ProjectNumber:    fm.ProjectNumber    = airtableField; break;
            case AirtableProjectFields.Title:            fm.Title            = airtableField; break;
            case AirtableProjectFields.OrganizationName: fm.OrganizationName = airtableField; break;
            case AirtableProjectFields.OrganizationType: fm.OrganizationType = airtableField; break;
            case AirtableProjectFields.ProjectTopic:     fm.ProjectTopic     = airtableField; break;
            case AirtableProjectFields.Description:      fm.Description      = airtableField; break;
            case AirtableProjectFields.TargetAudience:   fm.TargetAudience   = airtableField; break;
            case AirtableProjectFields.Goals:            fm.Goals            = airtableField; break;
            case AirtableProjectFields.Contents:         fm.Contents         = airtableField; break;
            case AirtableProjectFields.ContactPerson:    fm.ContactPerson    = airtableField; break;
            case AirtableProjectFields.ContactRole:      fm.ContactRole      = airtableField; break;
            case AirtableProjectFields.ContactEmail:     fm.ContactEmail     = airtableField; break;
            case AirtableProjectFields.ContactPhone:     fm.ContactPhone     = airtableField; break;
            case AirtableProjectFields.IncludeInPool:    fm.IncludeInPool    = airtableField; break;
            case AirtableProjectFields.SubmittedAt:      fm.SubmittedAt      = airtableField; break;
            case AirtableProjectFields.ProjectType:      fm.ProjectType      = airtableField; break;
            case AirtableProjectFields.Status:           fm.Status           = airtableField; break;
            case AirtableProjectFields.Priority:         fm.Priority         = airtableField; break;
        }
    }

    // ── Paginated fetch from Airtable REST API ───────────────────────────────

    private async Task<List<AirtableRecord>> FetchAllRecordsAsync(AirtableOptions options)
    {
        var all    = new List<AirtableRecord>();
        string? offset = null;
        var client = _httpFactory.CreateClient("Airtable");

        do
        {
            var url = $"{AirtableApiBase}/{Uri.EscapeDataString(options.BaseId)}" +
                      $"/{Uri.EscapeDataString(options.TableName)}";

            var queryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(options.ViewName))
                queryParts.Add($"view={Uri.EscapeDataString(options.ViewName)}");
            if (offset is not null)
                queryParts.Add($"offset={Uri.EscapeDataString(offset)}");
            if (queryParts.Count > 0)
                url += "?" + string.Join("&", queryParts);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", options.Token);

            _log.LogDebug("GET {Url}", url);
            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _log.LogError(
                    "Airtable API returned {Status}: {Body}",
                    (int)response.StatusCode, Truncate(errorBody, 500));
                response.EnsureSuccessStatusCode();
            }

            var body = await response.Content.ReadAsStringAsync();
            var page = JsonSerializer.Deserialize<AirtableListResponse>(body, JsonOpts);

            if (page?.Records is not null)
                all.AddRange(page.Records);

            offset = page?.Offset;
        }
        while (offset is not null);

        return all;
    }

    // ── Upsert one Airtable record ────────────────────────────────────────────

    private async Task UpsertRecordAsync(
        AirtableOptions         options,
        AirtableRecord          record,
        Dictionary<string, int> typesByName,
        Counter                 counter,
        int                     academicYearId,
        AirtableSyncResultDto   result)
    {
        var fm = options.FieldMap;
        var f  = record.Fields;

        string title = GetString(f, fm.Title);
        if (string.IsNullOrWhiteSpace(title))
        {
            _log.LogWarning("Record {Id}: Title field (\"{Field}\") is empty — using record ID as fallback.",
                record.Id, fm.Title);
            title = $"Airtable — {record.Id}";
        }

        int projectNumber = GetInt(f, fm.ProjectNumber);
        if (projectNumber <= 0)
            projectNumber = ++counter.Value;

        int    typeId   = ResolveType(NzGet(f, fm.ProjectType), typesByName);
        string status   = NormalizeStatus(NzGet(f, fm.Status)) ?? "Available";
        string syncedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        string? description      = NzGet(f, fm.Description);
        string? goals            = NzGet(f, fm.Goals);
        string? orgName          = NzGet(f, fm.OrganizationName);
        string? orgType          = NzGet(f, fm.OrganizationType);
        string? projectTopic     = NzGet(f, fm.ProjectTopic);
        string? contents         = NzGet(f, fm.Contents);
        string? contact          = NzGet(f, fm.ContactPerson);
        string? contactRole      = NzGet(f, fm.ContactRole);
        string? contactEmail     = NzGet(f, fm.ContactEmail);
        string? contactPhone     = NzGet(f, fm.ContactPhone);
        string? audience         = NzGet(f, fm.TargetAudience);
        string? priority         = NzGet(f, fm.Priority);

        if (string.IsNullOrWhiteSpace(orgName))
            _log.LogDebug("Record {Id}: OrganizationName field (\"{Field}\") is empty.", record.Id, fm.OrganizationName);

        int existingId = (await _db.GetRecordsAsync<int>(
            "SELECT Id FROM Projects WHERE AirtableRecordId = @RecordId",
            new { RecordId = record.Id })).FirstOrDefault();

        if (existingId == 0)
        {
            // ── Insert path with race-safe partial UNIQUE ─────────────────
            // DatabaseMigrator.EnsureImportIntegrityGuardsAsync creates a
            // partial UNIQUE on Projects.AirtableRecordId (when non-null /
            // non-empty). Two concurrent imports of the same record will
            // both see existingId=0 here; the second one's INSERT OR IGNORE
            // returns 0 rows affected, at which point we re-query the row
            // the other importer just wrote and fall through to UPDATE.
            int dupCount = (await _db.GetRecordsAsync<int>(
                "SELECT COUNT(1) FROM Projects WHERE ProjectNumber = @Num",
                new { Num = projectNumber })).FirstOrDefault();
            if (dupCount > 0) projectNumber = ++counter.Value;

            int teamId = await _db.InsertReturnIdAsync(
                "INSERT INTO Teams (AcademicYearId) VALUES (@AcademicYearId)",
                new { AcademicYearId = academicYearId });
            if (teamId == 0)
                throw new InvalidOperationException("Failed to create team for Airtable project.");

            int insertedRows = await _db.SaveDataAsync(@"
                INSERT OR IGNORE INTO Projects
                    (ProjectNumber, Title, Description, Status, TeamId, AcademicYearId, ProjectTypeId,
                     SourceType, AirtableRecordId,
                     OrganizationName, OrganizationType, ProjectTopic, Contents,
                     ContactPerson, ContactRole, ContactEmail, ContactPhone,
                     Goals, TargetAudience, Priority, LastSyncedAt)
                VALUES
                    (@ProjectNumber, @Title, @Description, @Status, @TeamId, @AcademicYearId, @ProjectTypeId,
                     'Airtable', @AirtableRecordId,
                     @OrganizationName, @OrganizationType, @ProjectTopic, @Contents,
                     @ContactPerson, @ContactRole, @ContactEmail, @ContactPhone,
                     @Goals, @TargetAudience, @Priority, @LastSyncedAt)",
                new
                {
                    ProjectNumber    = projectNumber,
                    Title            = title,
                    Description      = description,
                    Status           = status,
                    TeamId           = teamId,
                    AcademicYearId   = academicYearId,
                    ProjectTypeId    = typeId,
                    AirtableRecordId = record.Id,
                    OrganizationName = orgName,
                    OrganizationType = orgType,
                    ProjectTopic     = projectTopic,
                    Contents         = contents,
                    ContactPerson    = contact,
                    ContactRole      = contactRole,
                    ContactEmail     = contactEmail,
                    ContactPhone     = contactPhone,
                    Goals            = goals,
                    TargetAudience   = audience,
                    Priority         = priority,
                    LastSyncedAt     = syncedAt,
                });

            if (insertedRows > 0)
            {
                result.Inserted++;
                return;
            }

            // ── Race fallback ────────────────────────────────────────────
            // The partial UNIQUE on AirtableRecordId blocked the insert
            // because another concurrent import already created the row.
            // Clean up the orphan Teams row we just inserted (no Project
            // points at it), then re-resolve existingId and continue into
            // the UPDATE branch below so this importer's data still wins
            // for the fields that changed since the other insert.
            await _db.SaveDataAsync(@"
                DELETE FROM Teams
                WHERE  Id = @TeamId
                  AND  NOT EXISTS (SELECT 1 FROM Projects WHERE TeamId = @TeamId)",
                new { TeamId = teamId });

            existingId = (await _db.GetRecordsAsync<int>(
                "SELECT Id FROM Projects WHERE AirtableRecordId = @RecordId",
                new { RecordId = record.Id })).FirstOrDefault();

            if (existingId == 0)
                throw new InvalidOperationException(
                    $"Race fallback could not resolve existing project for record {record.Id}.");
        }

        // ── Update path (used for pre-existing rows AND race fallback) ──
        await _db.SaveDataAsync(@"
            UPDATE Projects
            SET    Title            = @Title,
                   Description      = @Description,
                   ProjectTypeId    = @ProjectTypeId,
                   Status           = @Status,
                   OrganizationName = @OrganizationName,
                   OrganizationType = @OrganizationType,
                   ProjectTopic     = @ProjectTopic,
                   Contents         = @Contents,
                   ContactPerson    = @ContactPerson,
                   ContactRole      = @ContactRole,
                   ContactEmail     = @ContactEmail,
                   ContactPhone     = @ContactPhone,
                   Goals            = @Goals,
                   TargetAudience   = @TargetAudience,
                   Priority         = @Priority,
                   SourceType       = 'Airtable',
                   LastSyncedAt     = @LastSyncedAt
            WHERE  Id = @Id",
            new
            {
                Title            = title,
                Description      = description,
                ProjectTypeId    = typeId,
                Status           = status,
                OrganizationName = orgName,
                OrganizationType = orgType,
                ProjectTopic     = projectTopic,
                Contents         = contents,
                ContactPerson    = contact,
                ContactRole      = contactRole,
                ContactEmail     = contactEmail,
                ContactPhone     = contactPhone,
                Goals            = goals,
                TargetAudience   = audience,
                Priority         = priority,
                LastSyncedAt     = syncedAt,
                Id               = existingId,
            });

        result.Updated++;
    }

    // ── Field extraction helpers ──────────────────────────────────────────────

    private static string GetString(Dictionary<string, JsonElement> fields, string key)
    {
        if (string.IsNullOrWhiteSpace(key))        return "";
        if (!fields.TryGetValue(key, out var el))  return "";
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? "",
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            JsonValueKind.Array  => string.Join(", ",
                el.EnumerateArray()
                  .Where(e => e.ValueKind == JsonValueKind.String)
                  .Select(e => e.GetString() ?? "")),
            _                    => "",
        };
    }

    private static string? NzGet(Dictionary<string, JsonElement> fields, string key)
    {
        var v = GetString(fields, key);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    private static int GetInt(Dictionary<string, JsonElement> fields, string key)
    {
        if (string.IsNullOrWhiteSpace(key))       return 0;
        if (!fields.TryGetValue(key, out var el)) return 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
        if (el.ValueKind == JsonValueKind.String &&
            int.TryParse(el.GetString(), out var si)) return si;
        return 0;
    }

    private static int ResolveType(string? name, Dictionary<string, int> typesByName)
    {
        if (string.IsNullOrWhiteSpace(name)) return 1;
        return typesByName.TryGetValue(name.Trim().ToLowerInvariant(), out var id) ? id : 1;
    }

    private static string? NormalizeStatus(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "active"      => "Active",
        "inactive"    => "Inactive",
        "archived"    => "Archived",
        "available"   => "Available",
        "unavailable" => "Unavailable",
        "פעיל"        => "Active",
        "לא פעיל"     => "Inactive",
        "זמין"        => "Available",
        "לא זמין"     => "Unavailable",
        "בארכיון"     => "Archived",
        _             => null,
    };

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";

    private static string ExplainStatus(System.Net.HttpStatusCode code) => code switch
    {
        System.Net.HttpStatusCode.Unauthorized => "טוקן לא תקין או פג תוקף",
        System.Net.HttpStatusCode.Forbidden    => "לטוקן אין הרשאות לבסיס הנבחר",
        System.Net.HttpStatusCode.NotFound     => "Base ID או שם הטבלה לא קיימים",
        System.Net.HttpStatusCode.UnprocessableEntity => "פרמטרי הבקשה אינם תקינים",
        _ => "ראו פירוט נוסף בלוג"
    };

    // ── Internal Airtable response shapes ────────────────────────────────────

    private sealed class AirtableListResponse
    {
        [JsonPropertyName("records")]
        public List<AirtableRecord>? Records { get; set; }

        [JsonPropertyName("offset")]
        public string? Offset { get; set; }
    }

    private sealed class AirtableRecord
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("fields")]
        public Dictionary<string, JsonElement> Fields { get; set; } = new();
    }

    private sealed class ProjectTypeRow
    {
        public int    Id   { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class Counter
    {
        public int Value { get; set; }
    }

    private sealed class AirtableSettingsRow
    {
        public int    Id                 { get; set; }
        public string ApiToken           { get; set; } = "";
        public string BaseId             { get; set; } = "";
        public string ProjectsTable      { get; set; } = "";
        public string ProjectsView       { get; set; } = "";
        public bool   StudentVisibleOnly { get; set; }
    }

    private sealed class MappingRow
    {
        public string LocalFieldName    { get; set; } = "";
        public string AirtableFieldName { get; set; } = "";
    }
}
