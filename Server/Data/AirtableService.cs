using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Data;

/// <summary>
/// Fetches project records from Airtable and upserts them into the local DB.
/// All configuration (token, base ID, table name, view, field map) is read from
/// AirtableOptions so no values are hardcoded — swap appsettings.json each year.
/// </summary>
public class AirtableService
{
    private const string AirtableApiBase = "https://api.airtable.com/v0";

    private readonly IHttpClientFactory         _httpFactory;
    private readonly DbRepository               _db;
    private readonly AirtableOptions            _options;
    private readonly ILogger<AirtableService>   _log;

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
        _log         = log;
        _options     = config.GetSection(AirtableOptions.SectionName)
                             .Get<AirtableOptions>()
                       ?? new AirtableOptions();
    }

    // ── Public entry point ───────────────────────────────────────────────────

    public async Task<AirtableSyncResultDto> SyncProjectsAsync()
    {
        if (!_options.IsConfigured)
        {
            _log.LogWarning("Airtable sync skipped: configuration incomplete (Token/BaseId/TableName missing).");
            return new AirtableSyncResultDto
            {
                SyncError = "Airtable אינו מוגדר. יש למלא Token, BaseId ו-TableName ב-appsettings."
            };
        }

        _log.LogInformation(
            "Starting Airtable sync — base: {BaseId}, table: {Table}, view: {View}",
            _options.BaseId, _options.TableName,
            string.IsNullOrWhiteSpace(_options.ViewName) ? "(all records)" : _options.ViewName);

        List<AirtableRecord> records;
        try
        {
            records = await FetchAllRecordsAsync();
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

        // ── Secondary pool filter ────────────────────────────────────────────
        // The Airtable view is the primary filter. When StudentVisibleOnly is true
        // and IncludeInPool is configured, we apply it as a defensive second pass.
        var fm = _options.FieldMap;
        if (_options.StudentVisibleOnly && !string.IsNullOrWhiteSpace(fm.IncludeInPool))
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

        // Pre-load project types: name (lower) → id
        var typeRows = await _db.GetRecordsAsync<ProjectTypeRow>(
            "SELECT Id, Name FROM ProjectTypes");
        var typesByName = typeRows?
            .ToDictionary(t => t.Name.ToLowerInvariant(), t => t.Id)
            ?? new Dictionary<string, int>();

        // Resolve current academic year for new inserts
        int currentYearId = (await _db.GetRecordsAsync<int>(
            "SELECT COALESCE(Id, 0) FROM AcademicYears WHERE IsCurrent = 1 LIMIT 1"))
            .FirstOrDefault();

        if (currentYearId == 0)
            _log.LogWarning("No current AcademicYear found (IsCurrent = 1). New Airtable projects will have AcademicYearId = 0.");

        // Track max project number so auto-assigned numbers don't collide.
        var counter = new Counter
        {
            Value = (await _db.GetRecordsAsync<int>(
                "SELECT COALESCE(MAX(ProjectNumber), 0) FROM Projects")).FirstOrDefault()
        };

        foreach (var record in records)
        {
            try
            {
                await UpsertRecordAsync(record, typesByName, counter, currentYearId, result);
            }
            catch (Exception ex)
            {
                result.Failed++;

                // Surface the root cause — include inner exception when present
                string rootMsg = ex.InnerException?.Message ?? ex.Message;
                string detail  = $"[{ex.GetType().Name}] {ex.Message}" +
                                 (ex.InnerException is not null
                                     ? $" → {ex.InnerException.Message}"
                                     : "");

                // Include mapped title/number so failures are easy to identify in logs
                string title  = GetString(record.Fields, _options.FieldMap.Title);
                int    num    = GetInt   (record.Fields, _options.FieldMap.ProjectNumber);

                result.Errors.Add(
                    $"Record {record.Id} (#{num} \"{title}\"): {rootMsg}");

                _log.LogError(ex,
                    "Upsert failed — record: {RecordId}, projectNumber: {Num}, title: \"{Title}\". {Detail}",
                    record.Id, num, title, detail);
            }
        }

        _log.LogInformation(
            "Airtable sync complete — fetched: {Fetched}, inserted: {Inserted}, updated: {Updated}, failed: {Failed}.",
            result.TotalFetched, result.Inserted, result.Updated, result.Failed);

        // If every record failed, promote the first error to SyncError so the UI
        // shows a meaningful message rather than just "0 inserted, 0 updated".
        if (result.Failed > 0 && result.Inserted == 0 && result.Updated == 0
            && result.Errors.Count > 0)
        {
            result.SyncError =
                $"כל {result.Failed} הרשומות נכשלו. דוגמה לשגיאה ראשונה: {result.Errors[0]}";
        }

        return result;
    }

    // ── Paginated fetch from Airtable REST API ───────────────────────────────

    private async Task<List<AirtableRecord>> FetchAllRecordsAsync()
    {
        var all    = new List<AirtableRecord>();
        string? offset = null;
        var client = _httpFactory.CreateClient("Airtable");

        do
        {
            // URL: /v0/{baseId}/{tableName}[?view=...&offset=...]
            var url = $"{AirtableApiBase}/{Uri.EscapeDataString(_options.BaseId)}" +
                      $"/{Uri.EscapeDataString(_options.TableName)}";

            var queryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_options.ViewName))
                queryParts.Add($"view={Uri.EscapeDataString(_options.ViewName)}");
            if (offset is not null)
                queryParts.Add($"offset={Uri.EscapeDataString(offset)}");
            if (queryParts.Count > 0)
                url += "?" + string.Join("&", queryParts);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.Token);

            _log.LogDebug("GET {Url}", url);
            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _log.LogError(
                    "Airtable API returned {Status}: {Body}",
                    (int)response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode(); // throws with status code in message
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
        AirtableRecord          record,
        Dictionary<string, int> typesByName,
        Counter                 counter,
        int                     academicYearId,
        AirtableSyncResultDto   result)
    {
        var fm = _options.FieldMap;
        var f  = record.Fields;

        // ── Map fields via FieldMap (no Hebrew is hardcoded here) ────────────

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

        // Log missing critical fields at debug level
        if (string.IsNullOrWhiteSpace(orgName))
            _log.LogDebug("Record {Id}: OrganizationName field (\"{Field}\") is empty.", record.Id, fm.OrganizationName);

        // ── Check for existing record by Airtable record ID ──────────────────
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
            // Ensure project number uniqueness
            int dupCount = (await _db.GetRecordsAsync<int>(
                "SELECT COUNT(1) FROM Projects WHERE ProjectNumber = @Num",
                new { Num = projectNumber })).FirstOrDefault();

            if (dupCount > 0)
                projectNumber = ++counter.Value;

            // Every project requires a team row.
            // Teams.AcademicYearId is NOT NULL with no default, so we must supply it.
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

    /// <summary>
    /// Returns the string value of a field. Returns "" when the field is absent or
    /// of a type that cannot be converted to text. Never throws.
    /// </summary>
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

    /// <summary>Returns trimmed non-empty string or null.</summary>
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

    /// <summary>Mutable counter used in async loops instead of a ref parameter.</summary>
    private sealed class Counter
    {
        public int Value { get; set; }
    }
}
