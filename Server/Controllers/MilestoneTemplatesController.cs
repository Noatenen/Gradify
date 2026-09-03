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
                    END AS Applicability,
                    (SELECT COUNT(1) FROM TaskTemplates tt
                     WHERE  tt.MilestoneTemplateId = mt.Id)              AS TaskTemplateCount,
                    (SELECT COUNT(1) FROM AcademicYearMilestones aym
                     WHERE  aym.MilestoneTemplateId = mt.Id)             AS CycleUsageCount,
                    (SELECT COUNT(1) FROM ProjectMilestones pm
                     JOIN   AcademicYearMilestones aym2 ON aym2.Id = pm.AcademicYearMilestoneId
                     WHERE  aym2.MilestoneTemplateId = mt.Id)            AS ProjectMilestoneCount
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
                    END AS Applicability,
                    (SELECT COUNT(1) FROM TaskTemplates tt
                     WHERE  tt.MilestoneTemplateId = mt.Id)              AS TaskTemplateCount,
                    (SELECT COUNT(1) FROM AcademicYearMilestones aym
                     WHERE  aym.MilestoneTemplateId = mt.Id)             AS CycleUsageCount,
                    (SELECT COUNT(1) FROM ProjectMilestones pm
                     JOIN   AcademicYearMilestones aym2 ON aym2.Id = pm.AcademicYearMilestoneId
                     WHERE  aym2.MilestoneTemplateId = mt.Id)            AS ProjectMilestoneCount
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

    // ── POST /api/milestone-templates/{id}/duplicate ────────────────────────
    //
    // Copies a milestone template, and the task templates attached to it, as a
    // NEW DRAFT.
    //
    // The copy is created INACTIVE on purpose. An active template is offered to
    // every cycle's "apply templates" picker, so a duplicate that arrived active
    // would immediately present itself as real curriculum before anyone had
    // renamed or edited it.
    //
    // Nothing existing is touched: no AcademicYearMilestones row, no
    // ProjectMilestone and no project Task is read or written here. The
    // duplicate starts life referenced by nobody.
    [HttpPost("{id:int}/duplicate")]
    public async Task<IActionResult> DuplicateTemplate(int id, int authUserId)
    {
        var source = (await _db.GetRecordsAsync<TemplateRow>(@"
            SELECT Id, Title, Description, OrderIndex, IsRequired, ProjectTypeId,
                   OpenDate, DueDate, CloseDate
            FROM   MilestoneTemplates
            WHERE  Id = @Id",
            new { Id = id }))?.FirstOrDefault();

        if (source is null) return NotFound("אבן הדרך לא נמצאה");

        // Appended to the library rather than squeezed in beside the original —
        // OrderIndex is the library's own ordering and cycles resolve their own
        // position from AcademicYearMilestones.DisplayOrder anyway.
        int nextOrder = (await _db.GetRecordsAsync<int>(
            "SELECT COALESCE(MAX(OrderIndex), 0) + 1 FROM MilestoneTemplates")).FirstOrDefault();

        string copyTitle = await UniqueCopyTitleAsync(source.Title);

        int newId = await _db.InsertReturnIdAsync(@"
            INSERT INTO MilestoneTemplates
                (Title, Description, OrderIndex, IsRequired, IsActive, ProjectTypeId,
                 OpenDate, DueDate, CloseDate)
            VALUES
                (@Title, @Description, @OrderIndex, @IsRequired, 0, @ProjectTypeId,
                 @OpenDate, @DueDate, @CloseDate)",
            new
            {
                Title       = copyTitle,
                source.Description,
                OrderIndex  = nextOrder,
                IsRequired  = source.IsRequired ? 1 : 0,
                source.ProjectTypeId,
                OpenDate    = D(source.OpenDate),
                DueDate     = D(source.DueDate),
                CloseDate   = D(source.CloseDate),
            });

        if (newId == 0) return StatusCode(500, "שגיאה בשכפול אבן הדרך");

        // The task templates come with it — a milestone without its tasks is not
        // a copy of that milestone. They are copies too, not re-pointed
        // originals: the source keeps everything it had.
        int copiedTasks = await _db.SaveDataAsync(@"
            INSERT INTO TaskTemplates
                (Title, Description, MilestoneTemplateId, StartDate, DueDate, IsActive,
                 IsSubmission, SubmissionInstructions, MaxFilesCount, MaxFileSizeMb,
                 AllowedFileTypes)
            SELECT
                 Title, Description, @NewId, StartDate, DueDate, 0,
                 IsSubmission, SubmissionInstructions, MaxFilesCount, MaxFileSizeMb,
                 AllowedFileTypes
            FROM TaskTemplates
            WHERE MilestoneTemplateId = @SourceId",
            new { NewId = newId, SourceId = id });

        return Ok(new { id = newId, title = copyTitle, taskTemplatesCopied = copiedTasks });
    }

    // ── DELETE /api/milestone-templates/{id} ────────────────────────────────
    //
    // Physical delete, allowed ONLY for library content nothing depends on.
    //
    // Refused (409) the moment any cycle includes the template or any project
    // has a milestone instantiated from it. That is not a cosmetic guard: an
    // AcademicYearMilestones row stores no title or type of its own and reads
    // both through this FK, so deleting the template underneath a live cycle
    // would strip the name off every student's roadmap. Deactivation is the
    // retirement path for anything in use.
    //
    // Task templates attached to it are DETACHED, not deleted. They are library
    // content in their own right; they land in the unassigned pool and stay
    // editable and re-attachable. (The FK is ON DELETE SET NULL after the
    // nullable migration, so this is belt-and-braces — but it makes the
    // behaviour explicit at the call site rather than implicit in the schema.)
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id, int authUserId)
    {
        bool exists = (await _db.GetRecordsAsync<int>(
            "SELECT 1 FROM MilestoneTemplates WHERE Id = @Id LIMIT 1",
            new { Id = id }))?.Any() ?? false;

        if (!exists) return NotFound("אבן הדרך לא נמצאה");

        int cycleCount = (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM AcademicYearMilestones WHERE MilestoneTemplateId = @Id",
            new { Id = id })).FirstOrDefault();

        int projectCount = (await _db.GetRecordsAsync<int>(@"
            SELECT COUNT(1)
            FROM   ProjectMilestones pm
            JOIN   AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            WHERE  aym.MilestoneTemplateId = @Id",
            new { Id = id })).FirstOrDefault();

        if (cycleCount > 0 || projectCount > 0)
        {
            var parts = new List<string>();
            if (cycleCount > 0)   parts.Add($"{cycleCount} מחזורים");
            if (projectCount > 0) parts.Add($"{projectCount} אבני דרך בפרויקטים");

            return Conflict(
                $"לא ניתן למחוק את התבנית — היא בשימוש ב־{string.Join(" ו־", parts)}. " +
                "ניתן להשבית אותה במקום זאת.");
        }

        await _db.SaveDataAsync(
            "UPDATE TaskTemplates SET MilestoneTemplateId = NULL WHERE MilestoneTemplateId = @Id",
            new { Id = id });

        await _db.SaveDataAsync("DELETE FROM MilestoneTemplates WHERE Id = @Id", new { Id = id });

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

    /// <summary>
    /// "X — עותק", then "X — עותק 2", 3, … MilestoneTemplates has no UNIQUE on
    /// Title, so this is for legibility rather than integrity: three duplicates
    /// of one template should be tellable apart in the list without opening them.
    /// </summary>
    private async Task<string> UniqueCopyTitleAsync(string sourceTitle)
    {
        string baseTitle = $"{sourceTitle} — עותק";

        var taken = (await _db.GetRecordsAsync<string>(
            "SELECT Title FROM MilestoneTemplates WHERE Title = @T OR Title LIKE @P",
            new { T = baseTitle, P = baseTitle + " %" }))?.ToHashSet() ?? new HashSet<string>();

        if (!taken.Contains(baseTitle)) return baseTitle;

        for (int n = 2; n < 1000; n++)
        {
            string candidate = $"{baseTitle} {n}";
            if (!taken.Contains(candidate)) return candidate;
        }

        return $"{baseTitle} {Guid.NewGuid():N}"[..80];
    }

    /// <summary>Dates are stored as yyyy-MM-dd text, as everywhere else here.</summary>
    private static string? D(DateTime? d) => d?.ToString("yyyy-MM-dd");

    private sealed class TemplateRow
    {
        public int       Id            { get; set; }
        public string    Title         { get; set; } = "";
        public string?   Description   { get; set; }
        public int       OrderIndex    { get; set; }
        public bool      IsRequired    { get; set; }
        public int?      ProjectTypeId { get; set; }
        public DateTime? OpenDate      { get; set; }
        public DateTime? DueDate       { get; set; }
        public DateTime? CloseDate     { get; set; }
    }

    private async Task<bool> ProjectTypeExistsAsync(int id) =>
        (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM ProjectTypes WHERE Id = @Id", new { Id = id }))
        .FirstOrDefault() > 0;
}
