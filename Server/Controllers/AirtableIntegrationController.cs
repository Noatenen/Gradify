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

    // ── POST /api/integrations/airtable/{id}/import ─────────────────────────
    [HttpPost("{id:int}/import")]
    public async Task<IActionResult> Import(int id, int authUserId)
    {
        var options = await _airtable.LoadOptionsAsync(id);
        if (options is null) return NotFound("הגדרת אינטגרציה לא נמצאה");

        // Must have all required mappings populated.
        var mappings = (await _db.GetRecordsAsync<RequiredMappingRow>(@"
            SELECT  LocalFieldName, AirtableFieldName, IsRequired
            FROM    AirtableFieldMappings
            WHERE   IntegrationSettingsId = @Id AND EntityType = 'Project'",
            new { Id = id }))?.ToList() ?? new();

        var missingRequired = mappings
            .Where(m => m.IsRequired && string.IsNullOrWhiteSpace(m.AirtableFieldName))
            .Select(m => m.LocalFieldName)
            .ToList();

        if (missingRequired.Count > 0)
        {
            return BadRequest(
                $"חסרות שיוכי שדות חובה: {string.Join(", ", missingRequired)}");
        }

        var result = await _airtable.SyncProjectsAsync(options);

        string summary = result.SyncError ??
            $"נטענו {result.TotalFetched}, נוספו {result.Inserted}, עודכנו {result.Updated}, נכשלו {result.Failed}";

        await _db.SaveDataAsync(@"
            UPDATE AirtableIntegrationSettings
            SET    LastImportAt      = datetime('now'),
                   LastImportSummary = @Summary,
                   UpdatedAt         = datetime('now')
            WHERE  Id = @Id",
            new { Id = id, Summary = summary });

        return Ok(result);
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
