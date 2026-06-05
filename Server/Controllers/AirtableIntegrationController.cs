using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  AirtableIntegrationController — /api/integrations/airtable
//
//  Per-academic-year Airtable configuration. The ApiToken column is never
//  returned; responses include only HasToken + a masked summary. PUT with an
//  empty ApiToken preserves the existing one (same convention as the Slack
//  ClientSecret in IntegrationSettingsController).
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/integrations/airtable")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin)]
public class AirtableIntegrationController : ControllerBase
{
    private readonly DbRepository    _db;
    private readonly AirtableService _airtable;

    public AirtableIntegrationController(DbRepository db, AirtableService airtable)
    {
        _db       = db;
        _airtable = airtable;
    }

    // Default Project mappings — local field name → Airtable column header default.
    // Used when a new integration is created.
    private static readonly (string Local, string Default, bool Required)[] DefaultProjectMappings = new[]
    {
        (AirtableProjectFields.ProjectNumber,    "ProjectNumber",    false),
        (AirtableProjectFields.Title,            "Title",            true ),
        (AirtableProjectFields.OrganizationName, "OrganizationName", false),
        (AirtableProjectFields.OrganizationType, "OrganizationType", false),
        (AirtableProjectFields.ProjectTopic,     "ProjectTopic",     false),
        (AirtableProjectFields.Description,      "Description",      false),
        (AirtableProjectFields.TargetAudience,   "TargetAudience",   false),
        (AirtableProjectFields.Goals,            "Goals",            false),
        (AirtableProjectFields.Contents,         "Contents",         false),
        (AirtableProjectFields.ContactPerson,    "ContactPerson",    false),
        (AirtableProjectFields.ContactRole,      "ContactRole",      false),
        (AirtableProjectFields.ContactEmail,     "ContactEmail",     false),
        (AirtableProjectFields.ContactPhone,     "ContactPhone",     false),
        (AirtableProjectFields.IncludeInPool,    "IncludeInPool",    false),
        (AirtableProjectFields.SubmittedAt,      "SubmittedAt",      false),
        (AirtableProjectFields.ProjectType,      "ProjectType",      false),
        (AirtableProjectFields.Status,           "Status",           false),
        (AirtableProjectFields.Priority,         "Priority",         false),
    };

    // ── GET /api/integrations/airtable ──────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll(int authUserId)
    {
        const string sql = @"
            SELECT  s.Id,
                    s.AcademicYearId,
                    COALESCE(ay.Name, '') AS AcademicYear,
                    s.Name,
                    s.BaseId,
                    s.ProjectsTable,
                    s.IsActive,
                    s.ApiToken,
                    s.LastTestedAt,
                    s.LastTestStatus,
                    s.LastImportAt,
                    s.LastImportSummary,
                    s.UpdatedAt
            FROM    AirtableIntegrationSettings s
            LEFT JOIN AcademicYears ay ON ay.Id = s.AcademicYearId
            ORDER   BY ay.IsCurrent DESC, s.UpdatedAt DESC";

        var rows = (await _db.GetRecordsAsync<ListRow>(sql))?.ToList() ?? new();

        var dtos = rows.Select(r => new AirtableIntegrationListItemDto
        {
            Id                = r.Id,
            AcademicYearId    = r.AcademicYearId,
            AcademicYear      = r.AcademicYear,
            Name              = r.Name,
            BaseId            = r.BaseId,
            ProjectsTable     = r.ProjectsTable,
            IsActive          = r.IsActive,
            HasToken          = !string.IsNullOrEmpty(r.ApiToken),
            TokenMasked       = MaskToken(r.ApiToken),
            LastTestedAt      = r.LastTestedAt,
            LastTestStatus    = r.LastTestStatus,
            LastImportAt      = r.LastImportAt,
            LastImportSummary = r.LastImportSummary,
            UpdatedAt         = r.UpdatedAt
        }).ToList();

        return Ok(dtos);
    }

    // ── GET /api/integrations/airtable/{id} ─────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne(int id, int authUserId)
    {
        var detail = await LoadDetailAsync(id);
        if (detail is null) return NotFound("הגדרת אינטגרציה לא נמצאה");
        return Ok(detail);
    }

    // ── POST /api/integrations/airtable ─────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create(int authUserId, [FromBody] SaveAirtableIntegrationRequest req)
    {
        var err = ValidateForCreate(req);
        if (err is not null) return BadRequest(err);

        if (!await ExistsAsync("SELECT 1 FROM AcademicYears WHERE Id = @Id", new { Id = req.AcademicYearId }))
            return BadRequest("המחזור האקדמי לא נמצא");

        if (req.IsActive)
            await DeactivateOtherForYearAsync(req.AcademicYearId, excludeId: 0);

        int newId = await _db.InsertReturnIdAsync(@"
            INSERT INTO AirtableIntegrationSettings
                (AcademicYearId, Name, ApiToken, BaseId,
                 ProjectsTable, ProjectsView,
                 MentorsTable, MentorsView,
                 StudentsTable, StudentsView,
                 TeamsTable, TeamsView,
                 StudentVisibleOnly, IsActive)
            VALUES
                (@YearId, @Name, @Token, @BaseId,
                 @PT, @PV,
                 @MT, @MV,
                 @ST, @SV,
                 @TT, @TV,
                 @VisOnly, @Active)",
            new
            {
                YearId   = req.AcademicYearId,
                Name     = string.IsNullOrWhiteSpace(req.Name) ? "Airtable" : req.Name.Trim(),
                Token    = req.ApiToken ?? "",
                req.BaseId,
                PT       = req.ProjectsTable ?? "",
                PV       = req.ProjectsView  ?? "",
                MT       = req.MentorsTable  ?? "",
                MV       = req.MentorsView   ?? "",
                ST       = req.StudentsTable ?? "",
                SV       = req.StudentsView  ?? "",
                TT       = req.TeamsTable    ?? "",
                TV       = req.TeamsView     ?? "",
                VisOnly  = req.StudentVisibleOnly ? 1 : 0,
                Active   = req.IsActive ? 1 : 0
            });

        if (newId == 0) return StatusCode(500, "שגיאה ביצירת ההגדרה");

        await SeedDefaultMappingsAsync(newId);
        return Ok(new { id = newId });
    }

    // ── PUT /api/integrations/airtable/{id} ─────────────────────────────────
    // Empty ApiToken keeps the existing token. IsActive=true deactivates other
    // configurations for the same academic year.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, int authUserId, [FromBody] SaveAirtableIntegrationRequest req)
    {
        if (req is null) return BadRequest("נתונים חסרים");
        if (string.IsNullOrWhiteSpace(req.BaseId))        return BadRequest("Base ID חובה");
        if (string.IsNullOrWhiteSpace(req.ProjectsTable)) return BadRequest("שם טבלת הפרויקטים חובה");
        if (req.AcademicYearId <= 0)                      return BadRequest("מחזור אקדמי חובה");

        if (!await ExistsAsync("SELECT 1 FROM AirtableIntegrationSettings WHERE Id = @Id", new { Id = id }))
            return NotFound("הגדרת אינטגרציה לא נמצאה");

        if (req.IsActive)
            await DeactivateOtherForYearAsync(req.AcademicYearId, excludeId: id);

        // Token is preserved when the incoming value is empty.
        await _db.SaveDataAsync(@"
            UPDATE AirtableIntegrationSettings
            SET    AcademicYearId     = @YearId,
                   Name               = @Name,
                   ApiToken           = CASE WHEN @Token = '' THEN ApiToken ELSE @Token END,
                   BaseId             = @BaseId,
                   ProjectsTable      = @PT,
                   ProjectsView       = @PV,
                   MentorsTable       = @MT,
                   MentorsView        = @MV,
                   StudentsTable      = @ST,
                   StudentsView       = @SV,
                   TeamsTable         = @TT,
                   TeamsView          = @TV,
                   StudentVisibleOnly = @VisOnly,
                   IsActive           = @Active,
                   UpdatedAt          = datetime('now')
            WHERE  Id = @Id",
            new
            {
                Id       = id,
                YearId   = req.AcademicYearId,
                Name     = string.IsNullOrWhiteSpace(req.Name) ? "Airtable" : req.Name.Trim(),
                Token    = req.ApiToken ?? "",
                req.BaseId,
                PT       = req.ProjectsTable ?? "",
                PV       = req.ProjectsView  ?? "",
                MT       = req.MentorsTable  ?? "",
                MV       = req.MentorsView   ?? "",
                ST       = req.StudentsTable ?? "",
                SV       = req.StudentsView  ?? "",
                TT       = req.TeamsTable    ?? "",
                TV       = req.TeamsView     ?? "",
                VisOnly  = req.StudentVisibleOnly ? 1 : 0,
                Active   = req.IsActive ? 1 : 0
            });

        return Ok();
    }

    // ── POST /api/integrations/airtable/{id}/test ───────────────────────────
    [HttpPost("{id:int}/test")]
    public async Task<IActionResult> Test(int id, int authUserId)
    {
        var options = await _airtable.LoadOptionsAsync(id);
        if (options is null) return NotFound("הגדרת אינטגרציה לא נמצאה");

        var result = await _airtable.TestConnectionAsync(options);

        await _db.SaveDataAsync(@"
            UPDATE AirtableIntegrationSettings
            SET    LastTestedAt   = datetime('now'),
                   LastTestStatus = @Status,
                   UpdatedAt      = datetime('now')
            WHERE  Id = @Id",
            new { Id = id, Status = result.Success ? "Success" : "Failed" });

        return Ok(result);
    }

    // ── POST /api/integrations/airtable/{id}/preview ────────────────────────
    //
    // Dry-run for the admin's "confirm import" workflow. Fetches Airtable
    // records and returns a per-record bucket label (New/Update/Warning/
    // Error) WITHOUT writing anything to the DB. The follow-up endpoint
    // is POST /import, which the UI calls only after the admin reviews
    // the preview and clicks "אשר ייבוא".
    [HttpPost("{id:int}/preview")]
    public async Task<IActionResult> Preview(int id, int authUserId)
    {
        var options = await _airtable.LoadOptionsAsync(id);
        if (options is null) return NotFound("הגדרת אינטגרציה לא נמצאה");

        // Same required-mapping gate as Import — surface the misconfig
        // before the admin sees an empty preview.
        var requiredErr = await CheckRequiredMappingsAsync(id);
        if (requiredErr is not null) return BadRequest(requiredErr);

        var result = await _airtable.PreviewProjectsAsync(options);
        return Ok(result);
    }

    // ── POST /api/integrations/airtable/{id}/preview-fixture ────────────────
    //
    // Dev/QA-only: runs the same preview pipeline against an in-request
    // set of mock Airtable records. No Airtable HTTP call, no DB writes.
    // Lets a tester force the three preview buckets ("new", "update with
    // diff", "suspected duplicate") deterministically without needing
    // access to the live Airtable base.
    //
    // The integration id is still required so the analyser uses the
    // configured FieldMap — that way fixture-bucket output mirrors what
    // production would produce for the same payload.
    [HttpPost("{id:int}/preview-fixture")]
    public async Task<IActionResult> PreviewFixture(
        int id, int authUserId,
        [FromBody] AirtableFixturePreviewRequest req)
    {
        var options = await _airtable.LoadOptionsAsync(id);
        if (options is null) return NotFound("הגדרת אינטגרציה לא נמצאה");

        var requiredErr = await CheckRequiredMappingsAsync(id);
        if (requiredErr is not null) return BadRequest(requiredErr);

        var result = await _airtable.PreviewFixtureAsync(options, req?.Records ?? new());
        return Ok(result);
    }

    // ── POST /api/integrations/airtable/{id}/import ─────────────────────────
    //
    // The actual import. Optional request body may carry a SkipRecordIds
    // list — Airtable records the admin opted-out of in the preview UI
    // (typically suspected duplicates). Those records are counted as
    // Skipped instead of upserted.
    //
    // Persists an AirtableImportRuns audit row so each run is investigable
    // later — counts, who triggered it, and (on failure) a short error
    // summary. The in-memory result is still returned to the UI.
    [HttpPost("{id:int}/import")]
    public async Task<IActionResult> Import(
        int id, int authUserId,
        [FromBody] AirtableImportRequest? req = null)
    {
        var options = await _airtable.LoadOptionsAsync(id);
        if (options is null) return NotFound("הגדרת אינטגרציה לא נמצאה");

        var requiredErr = await CheckRequiredMappingsAsync(id);
        if (requiredErr is not null) return BadRequest(requiredErr);

        // Build the skip set up front so the audit row + service share one
        // source of truth. Tolerant of null / empty body — backwards-compat
        // with any caller that POSTs without a body.
        var skipSet = req?.SkipRecordIds is { Count: > 0 }
            ? new HashSet<string>(req.SkipRecordIds.Where(s => !string.IsNullOrEmpty(s)))
            : new HashSet<string>();

        // Pre-insert an audit row so a crash mid-import still leaves a trail.
        int runId = await _db.InsertReturnIdAsync(@"
            INSERT INTO AirtableImportRuns
                (IntegrationSettingsId, TriggeredByUserId, StartedAt, Status)
            VALUES
                (@Id, @UserId, datetime('now'), 'InProgress')",
            new { Id = id, UserId = authUserId });

        var result = await _airtable.SyncProjectsAsync(options, skipSet);

        // Top-level status: Success | PartialFailure | Failure. PartialFailure
        // means some rows succeeded and at least one failed; Failure means
        // the whole sync errored out (SyncError set) OR nothing succeeded.
        string status =
            result.SyncError is not null                                ? "Failure" :
            result.Failed > 0 && (result.Inserted + result.Updated) > 0 ? "PartialFailure" :
            result.Failed > 0                                           ? "Failure" :
                                                                          "Success";

        string summary = result.SyncError ??
            $"נטענו {result.TotalFetched}, נוספו {result.Inserted}, עודכנו {result.Updated}, " +
            $"דולגו {result.Skipped}, נכשלו {result.Failed}";

        // Cap details at 8 KB so a runaway error list can't balloon the row.
        string details = result.Errors.Count == 0
            ? ""
            : string.Join("\n", result.Errors.Take(200));
        if (details.Length > 8 * 1024) details = details[..(8 * 1024)];

        await _db.SaveDataAsync(@"
            UPDATE AirtableImportRuns
            SET    FinishedAt   = datetime('now'),
                   TotalFetched = @T,
                   Inserted     = @I,
                   Updated      = @U,
                   Skipped      = @S,
                   Failed       = @F,
                   Status       = @Status,
                   ErrorSummary = @Summary,
                   ErrorDetails = @Details
            WHERE  Id = @Id",
            new
            {
                Id      = runId,
                T       = result.TotalFetched,
                I       = result.Inserted,
                U       = result.Updated,
                S       = result.Skipped,
                F       = result.Failed,
                Status  = status,
                Summary = summary,
                Details = details,
            });

        // Mirror the per-integration "last import" pointers for backwards
        // compatibility with the existing list-page badge.
        await _db.SaveDataAsync(@"
            UPDATE AirtableIntegrationSettings
            SET    LastImportAt      = datetime('now'),
                   LastImportSummary = @Summary,
                   UpdatedAt         = datetime('now')
            WHERE  Id = @Id",
            new { Id = id, Summary = summary });

        return Ok(result);
    }

    // ── GET /api/integrations/airtable/{id}/import-runs ─────────────────────
    //
    // Lightweight audit log surfaced under each integration. Returns most
    // recent first, capped at 25 — enough for ops triage without paging.
    [HttpGet("{id:int}/import-runs")]
    public async Task<IActionResult> GetImportRuns(int id, int authUserId)
    {
        if (!await ExistsAsync(
            "SELECT 1 FROM AirtableIntegrationSettings WHERE Id = @Id", new { Id = id }))
            return NotFound("הגדרת אינטגרציה לא נמצאה");

        var rows = (await _db.GetRecordsAsync<AirtableImportRunDto>(@"
            SELECT  r.Id, r.IntegrationSettingsId,
                    r.TriggeredByUserId,
                    COALESCE(TRIM(COALESCE(u.FirstName,'') || ' ' || COALESCE(u.LastName,'')), '')
                                                            AS TriggeredByName,
                    r.StartedAt, r.FinishedAt,
                    r.TotalFetched, r.Inserted, r.Updated, r.Failed, r.Skipped,
                    r.Status, r.ErrorSummary
            FROM    AirtableImportRuns r
            LEFT JOIN users u ON u.Id = r.TriggeredByUserId
            WHERE   r.IntegrationSettingsId = @Id
            ORDER   BY r.StartedAt DESC, r.Id DESC
            LIMIT   25",
            new { Id = id }))?.ToList() ?? new List<AirtableImportRunDto>();

        return Ok(rows);
    }

    // Shared with /preview + /import — surfaces a 400 with the missing
    // mapping names when an admin clicks before completing setup.
    private async Task<string?> CheckRequiredMappingsAsync(int integrationId)
    {
        var mappings = (await _db.GetRecordsAsync<RequiredMappingRow>(@"
            SELECT  LocalFieldName, AirtableFieldName, IsRequired
            FROM    AirtableFieldMappings
            WHERE   IntegrationSettingsId = @Id AND EntityType = 'Project'",
            new { Id = integrationId }))?.ToList() ?? new();

        var missingRequired = mappings
            .Where(m => m.IsRequired && string.IsNullOrWhiteSpace(m.AirtableFieldName))
            .Select(m => m.LocalFieldName)
            .ToList();

        return missingRequired.Count == 0
            ? null
            : $"חסרות שיוכי שדות חובה: {string.Join(", ", missingRequired)}";
    }

    // ── GET /api/integrations/airtable/{id}/mappings ────────────────────────
    [HttpGet("{id:int}/mappings")]
    public async Task<IActionResult> GetMappings(int id, int authUserId)
    {
        if (!await ExistsAsync("SELECT 1 FROM AirtableIntegrationSettings WHERE Id = @Id", new { Id = id }))
            return NotFound("הגדרת אינטגרציה לא נמצאה");

        var rows = await _db.GetRecordsAsync<AirtableFieldMappingDto>(@"
            SELECT  Id, EntityType, LocalFieldName, AirtableFieldName, IsRequired
            FROM    AirtableFieldMappings
            WHERE   IntegrationSettingsId = @Id
            ORDER   BY EntityType, LocalFieldName",
            new { Id = id });

        return Ok(rows ?? Enumerable.Empty<AirtableFieldMappingDto>());
    }

    // ── PUT /api/integrations/airtable/{id}/mappings ────────────────────────
    [HttpPut("{id:int}/mappings")]
    public async Task<IActionResult> SaveMappings(int id, int authUserId, [FromBody] SaveAirtableMappingsRequest req)
    {
        if (req is null) return BadRequest("נתונים חסרים");
        if (!await ExistsAsync("SELECT 1 FROM AirtableIntegrationSettings WHERE Id = @Id", new { Id = id }))
            return NotFound("הגדרת אינטגרציה לא נמצאה");

        foreach (var m in req.Mappings)
        {
            if (string.IsNullOrWhiteSpace(m.EntityType) || string.IsNullOrWhiteSpace(m.LocalFieldName))
                continue;

            await _db.SaveDataAsync(@"
                INSERT INTO AirtableFieldMappings
                    (IntegrationSettingsId, EntityType, LocalFieldName, AirtableFieldName, IsRequired, UpdatedAt)
                VALUES
                    (@Id, @Entity, @Local, @Air, @Req, datetime('now'))
                ON CONFLICT(IntegrationSettingsId, EntityType, LocalFieldName) DO UPDATE SET
                    AirtableFieldName = excluded.AirtableFieldName,
                    IsRequired        = excluded.IsRequired,
                    UpdatedAt         = datetime('now')",
                new
                {
                    Id     = id,
                    Entity = m.EntityType,
                    Local  = m.LocalFieldName,
                    Air    = m.AirtableFieldName ?? "",
                    Req    = m.IsRequired ? 1 : 0
                });
        }

        await _db.SaveDataAsync(
            "UPDATE AirtableIntegrationSettings SET UpdatedAt = datetime('now') WHERE Id = @Id",
            new { Id = id });

        return Ok();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Internal helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<AirtableIntegrationDetailDto?> LoadDetailAsync(int id)
    {
        var row = (await _db.GetRecordsAsync<DetailRow>(@"
            SELECT  s.Id,
                    s.AcademicYearId,
                    COALESCE(ay.Name, '') AS AcademicYear,
                    s.Name,
                    s.ApiToken,
                    s.BaseId,
                    s.ProjectsTable, s.ProjectsView,
                    s.MentorsTable,  s.MentorsView,
                    s.StudentsTable, s.StudentsView,
                    s.TeamsTable,    s.TeamsView,
                    s.StudentVisibleOnly,
                    s.IsActive,
                    s.LastTestedAt, s.LastTestStatus,
                    s.LastImportAt, s.LastImportSummary,
                    s.UpdatedAt
            FROM    AirtableIntegrationSettings s
            LEFT JOIN AcademicYears ay ON ay.Id = s.AcademicYearId
            WHERE   s.Id = @Id LIMIT 1",
            new { Id = id }))?.FirstOrDefault();

        if (row is null) return null;

        var mappings = (await _db.GetRecordsAsync<AirtableFieldMappingDto>(@"
            SELECT  Id, EntityType, LocalFieldName, AirtableFieldName, IsRequired
            FROM    AirtableFieldMappings
            WHERE   IntegrationSettingsId = @Id
            ORDER   BY EntityType, LocalFieldName",
            new { Id = id }))?.ToList() ?? new();

        return new AirtableIntegrationDetailDto
        {
            Id                 = row.Id,
            AcademicYearId     = row.AcademicYearId,
            AcademicYear       = row.AcademicYear,
            Name               = row.Name,
            BaseId             = row.BaseId,
            ProjectsTable      = row.ProjectsTable,
            ProjectsView       = row.ProjectsView,
            MentorsTable       = row.MentorsTable,
            MentorsView        = row.MentorsView,
            StudentsTable      = row.StudentsTable,
            StudentsView       = row.StudentsView,
            TeamsTable         = row.TeamsTable,
            TeamsView          = row.TeamsView,
            StudentVisibleOnly = row.StudentVisibleOnly,
            IsActive           = row.IsActive,
            HasToken           = !string.IsNullOrEmpty(row.ApiToken),
            TokenMasked        = MaskToken(row.ApiToken),
            LastTestedAt       = row.LastTestedAt,
            LastTestStatus     = row.LastTestStatus,
            LastImportAt       = row.LastImportAt,
            LastImportSummary  = row.LastImportSummary,
            UpdatedAt          = row.UpdatedAt,
            Mappings           = mappings
        };
    }

    private async Task SeedDefaultMappingsAsync(int integrationId)
    {
        foreach (var (local, def, required) in DefaultProjectMappings)
        {
            await _db.SaveDataAsync(@"
                INSERT INTO AirtableFieldMappings
                    (IntegrationSettingsId, EntityType, LocalFieldName, AirtableFieldName, IsRequired)
                VALUES (@Id, 'Project', @Local, @Air, @Req)
                ON CONFLICT(IntegrationSettingsId, EntityType, LocalFieldName) DO NOTHING",
                new { Id = integrationId, Local = local, Air = def, Req = required ? 1 : 0 });
        }
    }

    private async Task DeactivateOtherForYearAsync(int academicYearId, int excludeId)
    {
        await _db.SaveDataAsync(@"
            UPDATE AirtableIntegrationSettings
            SET    IsActive  = 0,
                   UpdatedAt = datetime('now')
            WHERE  AcademicYearId = @YearId AND Id != @ExcludeId AND IsActive = 1",
            new { YearId = academicYearId, ExcludeId = excludeId });
    }

    private async Task<bool> ExistsAsync(string sql, object parameters)
    {
        var rows = await _db.GetRecordsAsync<int>(sql, parameters);
        return rows is not null && rows.Any();
    }

    private static string? ValidateForCreate(SaveAirtableIntegrationRequest req)
    {
        if (req is null) return "נתונים חסרים";
        if (req.AcademicYearId <= 0)                      return "מחזור אקדמי חובה";
        if (string.IsNullOrWhiteSpace(req.ApiToken))      return "Personal Access Token חובה ביצירת אינטגרציה";
        if (string.IsNullOrWhiteSpace(req.BaseId))        return "Base ID חובה";
        if (string.IsNullOrWhiteSpace(req.ProjectsTable)) return "שם טבלת הפרויקטים חובה";
        return null;
    }

    private static string MaskToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return "";
        // Reveal first 3 + last 2 — typical PATs start with "pat".
        if (token.Length <= 6) return new string('•', token.Length);
        return token[..3] + new string('•', Math.Min(token.Length - 5, 8)) + token[^2..];
    }

    // ── DB row types ─────────────────────────────────────────────────────────

    private sealed class ListRow
    {
        public int     Id                { get; set; }
        public int     AcademicYearId    { get; set; }
        public string  AcademicYear      { get; set; } = "";
        public string  Name              { get; set; } = "";
        public string  BaseId            { get; set; } = "";
        public string  ProjectsTable     { get; set; } = "";
        public bool    IsActive          { get; set; }
        public string  ApiToken          { get; set; } = "";
        public string? LastTestedAt      { get; set; }
        public string? LastTestStatus    { get; set; }
        public string? LastImportAt      { get; set; }
        public string? LastImportSummary { get; set; }
        public string  UpdatedAt         { get; set; } = "";
    }

    private sealed class DetailRow
    {
        public int     Id                 { get; set; }
        public int     AcademicYearId     { get; set; }
        public string  AcademicYear       { get; set; } = "";
        public string  Name               { get; set; } = "";
        public string  ApiToken           { get; set; } = "";
        public string  BaseId             { get; set; } = "";
        public string  ProjectsTable      { get; set; } = "";
        public string  ProjectsView       { get; set; } = "";
        public string  MentorsTable       { get; set; } = "";
        public string  MentorsView        { get; set; } = "";
        public string  StudentsTable      { get; set; } = "";
        public string  StudentsView       { get; set; } = "";
        public string  TeamsTable         { get; set; } = "";
        public string  TeamsView          { get; set; } = "";
        public bool    StudentVisibleOnly { get; set; }
        public bool    IsActive           { get; set; }
        public string? LastTestedAt       { get; set; }
        public string? LastTestStatus     { get; set; }
        public string? LastImportAt       { get; set; }
        public string? LastImportSummary  { get; set; }
        public string  UpdatedAt          { get; set; } = "";
    }

    private sealed class RequiredMappingRow
    {
        public string LocalFieldName    { get; set; } = "";
        public string AirtableFieldName { get; set; } = "";
        public bool   IsRequired        { get; set; }
    }
}
