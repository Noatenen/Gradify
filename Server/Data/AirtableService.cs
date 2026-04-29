using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Data;

/// <summary>
/// Fetches project records from Airtable and upserts them into the local DB.
///
/// Configuration is supplied per-call via <see cref="AirtableOptions"/>:
///   · The new admin UI calls <see cref="SyncProjectsAsync(AirtableOptions)"/>
///     with the saved row from AirtableIntegrationSettings.
///   · The legacy <see cref="SyncProjectsAsync()"/> entry point — still wired
///     to /api/airtable/sync-projects — first looks up the active DB
///     configuration for the current academic year, then falls back to the
///     "Airtable" section in appsettings.json so existing installations keep
///     working without immediate admin action.
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

    /// <summary>Resolves the DB-backed active configuration (or appsettings fallback) and runs the sync.</summary>
    public async Task<AirtableSyncResultDto> SyncProjectsAsync()
    {
        var options = await ResolveActiveOptionsAsync();
        if (options is null)
        {
            return new AirtableSyncResultDto
            {
                SyncError = "Airtable אינו מוגדר. הגדירו אינטגרציה פעילה דרך ניהול > אינטגרציות."
            };
        }
        return await SyncProjectsAsync(options);
    }

    /// <summary>Runs the sync with an explicit configuration (used by the admin UI per-row).</summary>
    public async Task<AirtableSyncResultDto> SyncProjectsAsync(AirtableOptions options)
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

    /// <summary>Picks the active Airtable config for the current academic year, or null/appsettings-fallback.</summary>
    public async Task<AirtableOptions?> ResolveActiveOptionsAsync()
    {
        var rows = await _db.GetRecordsAsync<int>(
            "SELECT Id FROM AirtableIntegrationSettings WHERE IsActive = 1 " +
            "AND AcademicYearId IN (SELECT Id FROM AcademicYears WHERE IsCurrent = 1) LIMIT 1");
        int settingsId = rows?.FirstOrDefault() ?? 0;

        if (settingsId > 0)
            return await LoadOptionsAsync(settingsId);

        // Legacy fallback — appsettings.json "Airtable" section.
        var legacy = _config.GetSection(AirtableOptions.SectionName).Get<AirtableOptions>();
        return legacy is { IsConfigured: true } ? legacy : null;
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

        if (existingId > 0)
        {
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
        else
        {
            int dupCount = (await _db.GetRecordsAsync<int>(
                "SELECT COUNT(1) FROM Projects WHERE ProjectNumber = @Num",
                new { Num = projectNumber })).FirstOrDefault();

            if (dupCount > 0)
                projectNumber = ++counter.Value;

            int teamId = await _db.InsertReturnIdAsync(
                "INSERT INTO Teams (AcademicYearId) VALUES (@AcademicYearId)",
                new { AcademicYearId = academicYearId });

            if (teamId == 0)
                throw new InvalidOperationException("Failed to create team for Airtable project.");

            await _db.InsertReturnIdAsync(@"
                INSERT INTO Projects
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

            result.Inserted++;
        }
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
