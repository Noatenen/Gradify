using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  MilestoneTemplatesController — /api/milestone-templates
//
//  Admin management of MilestoneTemplates (the master list).
//  Applicability is stored as ProjectTypeId on MilestoneTemplates:
//    NULL  → shared (both Technological and Methodological)
//    1     → Technological only
//    2     → Methodological only
//
//  This controller does NOT manage AcademicYearMilestones (schedule per cycle)
//  or ProjectMilestones (per-project status) — those are separate concerns.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/milestone-templates")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public class MilestoneTemplatesController : ControllerBase
{
    private readonly DbRepository _db;

    public MilestoneTemplatesController(DbRepository db) => _db = db;

    // ── GET /api/milestone-templates ────────────────────────────────────────
    // Returns all templates with resolved applicability label.
    // Optional query param ?projectTypeId=1|2 to filter by applicability
    // (returns matching type AND shared/null templates).
    [HttpGet]
    public async Task<IActionResult> GetTemplates(int authUserId, [FromQuery] int? projectTypeId = null)
    {
        const string sql = @"
            SELECT  mt.Id,
                    mt.Title,
                    mt.Description,
                    mt.OrderIndex,
                    mt.IsRequired,
                    mt.IsActive,
                    mt.ProjectTypeId,
                    mt.OpenDate,
                    mt.DueDate,
                    mt.CloseDate,
                    CASE mt.ProjectTypeId
                        WHEN 1 THEN 'טכנולוגי'
                        WHEN 2 THEN 'מתודולוגי'
                        ELSE        'שניהם'
                    END AS Applicability
            FROM    MilestoneTemplates mt
            ORDER   BY mt.OrderIndex, mt.Id";

        var rows = await _db.GetRecordsAsync<MilestoneTemplateDto>(sql);
        if (rows is null) return Ok(Enumerable.Empty<MilestoneTemplateDto>());

        // Optional server-side filter: return only templates relevant to a given project type.
        // "Relevant" = the template is shared (NULL) OR matches the requested type.
        if (projectTypeId.HasValue)
        {
            rows = rows.Where(t =>
                t.ProjectTypeId is null || t.ProjectTypeId == projectTypeId.Value);
        }

        return Ok(rows);
    }

    // ── GET /api/milestone-templates/{id} ───────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetTemplate(int id, int authUserId)
    {
        const string sql = @"
            SELECT  mt.Id,
                    mt.Title,
                    mt.Description,
                    mt.OrderIndex,
                    mt.IsRequired,
                    mt.IsActive,
                    mt.ProjectTypeId,
                    mt.OpenDate,
                    mt.DueDate,
                    mt.CloseDate,
                    CASE mt.ProjectTypeId
                        WHEN 1 THEN 'טכנולוגי'
                        WHEN 2 THEN 'מתודולוגי'
                        ELSE        'שניהם'
                    END AS Applicability
            FROM    MilestoneTemplates mt
            WHERE   mt.Id = @Id";

        var rows = await _db.GetRecordsAsync<MilestoneTemplateDto>(sql, new { Id = id });
        var template = rows?.FirstOrDefault();
        if (template is null) return NotFound("אבן הדרך לא נמצאה");
        return Ok(template);
    }

    // ── POST /api/milestone-templates ───────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> CreateTemplate(
        [FromBody] SaveMilestoneTemplateRequest req, int authUserId)
    {
        var err = Validate(req);
        if (err != null) return BadRequest(err);

        if (req.ProjectTypeId.HasValue && await ProjectTypeExistsAsync(req.ProjectTypeId.Value) == false)
            return BadRequest("סוג הפרויקט לא נמצא");

        const string sql = @"
            INSERT INTO MilestoneTemplates
                (Title, Description, OrderIndex, IsRequired, IsActive, ProjectTypeId,
                 OpenDate, DueDate, CloseDate)
            VALUES
                (@Title, @Description, @OrderIndex, @IsRequired, @IsActive, @ProjectTypeId,
                 @OpenDate, @DueDate, @CloseDate)";

        int newId = await _db.InsertReturnIdAsync(sql, new
        {
            Title        = req.Title.Trim(),
            Description  = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            req.OrderIndex,
            IsRequired   = req.IsRequired ? 1 : 0,
            IsActive     = req.IsActive   ? 1 : 0,
            req.ProjectTypeId,
            OpenDate     = req.OpenDate?.ToString("yyyy-MM-dd"),
            DueDate      = req.DueDate?.ToString("yyyy-MM-dd"),
            CloseDate    = req.CloseDate?.ToString("yyyy-MM-dd"),
        });

        if (newId == 0) return StatusCode(500, "שגיאה ביצירת אבן הדרך");
        return Ok(new { id = newId });
    }

    // ── PUT /api/milestone-templates/{id} ────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateTemplate(
        int id, [FromBody] SaveMilestoneTemplateRequest req, int authUserId)
    {
        var err = Validate(req);
        if (err != null) return BadRequest(err);

        if (req.ProjectTypeId.HasValue && await ProjectTypeExistsAsync(req.ProjectTypeId.Value) == false)
            return BadRequest("סוג הפרויקט לא נמצא");

        const string sql = @"
            UPDATE MilestoneTemplates
            SET    Title         = @Title,
                   Description   = @Description,
                   OrderIndex    = @OrderIndex,
                   IsRequired    = @IsRequired,
                   IsActive      = @IsActive,
                   ProjectTypeId = @ProjectTypeId,
                   OpenDate      = @OpenDate,
                   DueDate       = @DueDate,
                   CloseDate     = @CloseDate
            WHERE  Id = @Id";

        int affected = await _db.SaveDataAsync(sql, new
        {
            Title        = req.Title.Trim(),
            Description  = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            req.OrderIndex,
            IsRequired   = req.IsRequired ? 1 : 0,
            IsActive     = req.IsActive   ? 1 : 0,
            req.ProjectTypeId,
            OpenDate     = req.OpenDate?.ToString("yyyy-MM-dd"),
            DueDate      = req.DueDate?.ToString("yyyy-MM-dd"),
            CloseDate    = req.CloseDate?.ToString("yyyy-MM-dd"),
            Id           = id,
        });

        if (affected == 0) return NotFound("אבן הדרך לא נמצאה");
        return Ok();
    }

    // ── PATCH /api/milestone-templates/{id}/toggle-active ───────────────────
    [HttpPatch("{id:int}/toggle-active")]
    public async Task<IActionResult> ToggleActive(int id, int authUserId)
    {
        const string sql = @"
            UPDATE MilestoneTemplates
            SET    IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END
            WHERE  Id = @Id";

        int affected = await _db.SaveDataAsync(sql, new { Id = id });
        if (affected == 0) return NotFound("אבן הדרך לא נמצאה");
        return Ok();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string? Validate(SaveMilestoneTemplateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return "שם אבן הדרך הוא שדה חובה";
        if (req.OrderIndex < 0)                   return "מספר סדר חייב להיות אפס או יותר";

        // Date ordering — only enforced when both endpoints are set.
        if (req.OpenDate is not null && req.DueDate is not null
            && req.DueDate.Value.Date < req.OpenDate.Value.Date)
            return "תאריך היעד חייב להיות אחרי תאריך ההתחלה";

        if (req.OpenDate is not null && req.CloseDate is not null
            && req.CloseDate.Value.Date < req.OpenDate.Value.Date)
            return "תאריך הסיום חייב להיות אחרי תאריך ההתחלה";

        return null;
    }

    private async Task<bool> ProjectTypeExistsAsync(int id) =>
        (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM ProjectTypes WHERE Id = @Id", new { Id = id }))
        .FirstOrDefault() > 0;
}
