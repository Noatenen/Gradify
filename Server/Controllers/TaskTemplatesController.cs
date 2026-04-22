using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  TaskTemplatesController — /api/task-templates
//
//  Admin management of TaskTemplates (the global task master list).
//  Each template is linked to a MilestoneTemplate.
//  Date-based status is calculated client-side; server stores raw dates.
//
//  This controller does NOT manage per-project operational Tasks.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/task-templates")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public class TaskTemplatesController : ControllerBase
{
    private readonly DbRepository _db;

    public TaskTemplatesController(DbRepository db) => _db = db;

    // ── GET /api/task-templates ─────────────────────────────────────────────
    // Returns all task templates including submission policy fields and linked resource files.
    // Optional query param ?milestoneTemplateId=N to filter by milestone.
    [HttpGet]
    public async Task<IActionResult> GetTemplates(
        int authUserId,
        [FromQuery] int? milestoneTemplateId = null)
    {
        const string sql = @"
            SELECT  tt.Id,
                    tt.Title,
                    tt.Description,
                    tt.MilestoneTemplateId,
                    mt.Title AS MilestoneTitle,
                    tt.StartDate,
                    tt.DueDate,
                    tt.IsActive,
                    tt.CreatedAt,
                    tt.IsSubmission,
                    tt.SubmissionInstructions,
                    tt.MaxFilesCount,
                    tt.MaxFileSizeMb,
                    tt.AllowedFileTypes
            FROM    TaskTemplates tt
            JOIN    MilestoneTemplates mt ON mt.Id = tt.MilestoneTemplateId
            ORDER   BY tt.StartDate, tt.Id";

        var rows = await _db.GetRecordsAsync<TaskTemplateDto>(sql);
        if (rows is null) return Ok(Enumerable.Empty<TaskTemplateDto>());

        var templates = rows.ToList();

        if (milestoneTemplateId.HasValue)
            templates = templates.Where(t => t.MilestoneTemplateId == milestoneTemplateId.Value).ToList();

        await AttachLinkedFilesAsync(templates);
        return Ok(templates);
    }

    // ── GET /api/task-templates/{id} ────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetTemplate(int id, int authUserId)
    {
        const string sql = @"
            SELECT  tt.Id,
                    tt.Title,
                    tt.Description,
                    tt.MilestoneTemplateId,
                    mt.Title AS MilestoneTitle,
                    tt.StartDate,
                    tt.DueDate,
                    tt.IsActive,
                    tt.CreatedAt,
                    tt.IsSubmission,
                    tt.SubmissionInstructions,
                    tt.MaxFilesCount,
                    tt.MaxFileSizeMb,
                    tt.AllowedFileTypes
            FROM    TaskTemplates tt
            JOIN    MilestoneTemplates mt ON mt.Id = tt.MilestoneTemplateId
            WHERE   tt.Id = @Id";

        var rows = await _db.GetRecordsAsync<TaskTemplateDto>(sql, new { Id = id });
        var template = rows?.FirstOrDefault();
        if (template is null) return NotFound("המשימה לא נמצאה");

        await AttachLinkedFilesAsync(new List<TaskTemplateDto> { template });
        return Ok(template);
    }

    // ── POST /api/task-templates ────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> CreateTemplate(
        [FromBody] SaveTaskTemplateRequest req, int authUserId)
    {
        var err = Validate(req);
        if (err != null) return BadRequest(err);

        if (!await MilestoneTemplateExistsAsync(req.MilestoneTemplateId))
            return BadRequest("אבן הדרך לא נמצאה");

        const string sql = @"
            INSERT INTO TaskTemplates
                (Title, Description, MilestoneTemplateId, StartDate, DueDate, IsActive,
                 IsSubmission, SubmissionInstructions, MaxFilesCount, MaxFileSizeMb, AllowedFileTypes)
            VALUES
                (@Title, @Description, @MilestoneTemplateId, @StartDate, @DueDate, @IsActive,
                 @IsSubmission, @SubmissionInstructions, @MaxFilesCount, @MaxFileSizeMb, @AllowedFileTypes)";

        int newId = await _db.InsertReturnIdAsync(sql, new
        {
            Title                  = req.Title.Trim(),
            Description            = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            req.MilestoneTemplateId,
            StartDate              = req.StartDate.ToString("yyyy-MM-dd"),
            DueDate                = req.DueDate.ToString("yyyy-MM-dd"),
            IsActive               = req.IsActive ? 1 : 0,
            IsSubmission           = req.IsSubmission ? 1 : 0,
            SubmissionInstructions = req.IsSubmission ? req.SubmissionInstructions?.Trim() : null,
            MaxFilesCount          = req.IsSubmission ? req.MaxFilesCount : null,
            MaxFileSizeMb          = req.IsSubmission ? req.MaxFileSizeMb : null,
            AllowedFileTypes       = req.IsSubmission ? req.AllowedFileTypes : null,
        });

        if (newId == 0) return StatusCode(500, "שגיאה ביצירת המשימה");

        await SaveResourceFileLinksAsync(newId, req.LinkedResourceFileIds);
        return Ok(new { id = newId });
    }

    // ── PUT /api/task-templates/{id} ────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateTemplate(
        int id, [FromBody] SaveTaskTemplateRequest req, int authUserId)
    {
        var err = Validate(req);
        if (err != null) return BadRequest(err);

        if (!await MilestoneTemplateExistsAsync(req.MilestoneTemplateId))
            return BadRequest("אבן הדרך לא נמצאה");

        const string sql = @"
            UPDATE TaskTemplates
            SET    Title                  = @Title,
                   Description            = @Description,
                   MilestoneTemplateId    = @MilestoneTemplateId,
                   StartDate              = @StartDate,
                   DueDate               = @DueDate,
                   IsActive              = @IsActive,
                   IsSubmission          = @IsSubmission,
                   SubmissionInstructions = @SubmissionInstructions,
                   MaxFilesCount         = @MaxFilesCount,
                   MaxFileSizeMb         = @MaxFileSizeMb,
                   AllowedFileTypes      = @AllowedFileTypes
            WHERE  Id = @Id";

        int affected = await _db.SaveDataAsync(sql, new
        {
            Title                  = req.Title.Trim(),
            Description            = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            req.MilestoneTemplateId,
            StartDate              = req.StartDate.ToString("yyyy-MM-dd"),
            DueDate                = req.DueDate.ToString("yyyy-MM-dd"),
            IsActive               = req.IsActive ? 1 : 0,
            IsSubmission           = req.IsSubmission ? 1 : 0,
            SubmissionInstructions = req.IsSubmission ? req.SubmissionInstructions?.Trim() : null,
            MaxFilesCount          = req.IsSubmission ? req.MaxFilesCount : null,
            MaxFileSizeMb          = req.IsSubmission ? req.MaxFileSizeMb : null,
            AllowedFileTypes       = req.IsSubmission ? req.AllowedFileTypes : null,
            Id                     = id,
        });

        if (affected == 0) return NotFound("המשימה לא נמצאה");

        // Replace resource file links (delete-and-reinsert pattern)
        await _db.SaveDataAsync(
            "DELETE FROM TaskTemplateResourceFiles WHERE TaskTemplateId = @Id", new { Id = id });
        await SaveResourceFileLinksAsync(id, req.LinkedResourceFileIds);

        return Ok();
    }

    // ── PATCH /api/task-templates/{id}/toggle-active ────────────────────────
    [HttpPatch("{id:int}/toggle-active")]
    public async Task<IActionResult> ToggleActive(int id, int authUserId)
    {
        const string sql = @"
            UPDATE TaskTemplates
            SET    IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END
            WHERE  Id = @Id";

        int affected = await _db.SaveDataAsync(sql, new { Id = id });
        if (affected == 0) return NotFound("המשימה לא נמצאה");
        return Ok();
    }

    // ── DELETE /api/task-templates/{id} ─────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id, int authUserId)
    {
        const string sql = "DELETE FROM TaskTemplates WHERE Id = @Id";
        int affected = await _db.SaveDataAsync(sql, new { Id = id });
        if (affected == 0) return NotFound("המשימה לא נמצאה");
        return Ok();
    }

    // ── GET /api/task-templates/operational-tasks ───────────────────────────
    // Admin read-only view of ALL operational tasks across all projects.
    // Reserved for future operational-tasks screens; not used by the
    // global task-templates management page.
    // Tasks with no ProjectMilestoneId are included via LEFT JOINs.
    [HttpGet("operational-tasks")]
    public async Task<IActionResult> GetOperationalTasks(int authUserId)
    {
        const string sql = @"
            SELECT  t.Id,
                    t.Title,
                    t.Description,
                    t.TaskType,
                    t.Status,
                    t.DueDate,
                    t.CreatedAt,
                    t.ClosedAt,
                    creator.FirstName  || ' ' || creator.LastName                      AS CreatorName,
                    COALESCE(assigned.FirstName || ' ' || assigned.LastName, '')        AS AssignedToName,
                    p.ProjectNumber,
                    p.Title                                                             AS ProjectTitle,
                    COALESCE(mt.Title, '')                                              AS MilestoneTitle
            FROM    Tasks t
            JOIN    Users    creator  ON creator.Id  = t.CreatedByUserId
            JOIN    Projects p        ON p.Id         = t.ProjectId
            LEFT JOIN Users  assigned ON assigned.Id  = t.AssignedToUserId
            LEFT JOIN ProjectMilestones      pm  ON pm.Id  = t.ProjectMilestoneId
            LEFT JOIN AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            LEFT JOIN MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            ORDER   BY t.TaskType, t.CreatedAt DESC";

        var rows = await _db.GetRecordsAsync<OperationalTaskAdminDto>(sql);
        return Ok(rows ?? Enumerable.Empty<OperationalTaskAdminDto>());
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string? Validate(SaveTaskTemplateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))  return "שם המשימה הוא שדה חובה";
        if (req.MilestoneTemplateId <= 0)           return "יש לבחור אבן דרך";
        if (req.DueDate <= req.StartDate)           return "תאריך היעד חייב להיות אחרי תאריך ההתחלה";

        if (req.IsSubmission)
        {
            if (req.MaxFilesCount is null or <= 0) return "יש להגדיר מספר קבצים מקסימלי גדול מ-0";
            if (req.MaxFileSizeMb is null or <= 0) return "יש להגדיר גודל קובץ מקסימלי גדול מ-0";
        }

        return null;
    }

    private async Task<bool> MilestoneTemplateExistsAsync(int id) =>
        (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM MilestoneTemplates WHERE Id = @Id", new { Id = id }))
        .FirstOrDefault() > 0;

    // Loads linked resource files for a list of templates and attaches them in-place.
    private async Task AttachLinkedFilesAsync(List<TaskTemplateDto> templates)
    {
        if (templates.Count == 0) return;

        const string sql = @"
            SELECT  ttrf.TaskTemplateId,
                    rf.Id,
                    rf.FileName,
                    rf.ContentType
            FROM    TaskTemplateResourceFiles ttrf
            JOIN    ResourceFiles rf ON rf.Id = ttrf.ResourceFileId
            ORDER   BY ttrf.TaskTemplateId, rf.Id";

        var rows = await _db.GetRecordsAsync<LinkedFileRow>(sql);
        if (rows is null) return;

        var byTemplate = rows
            .GroupBy(r => r.TaskTemplateId)
            .ToDictionary(g => g.Key, g => g.Select(r => new TaskTemplateResourceFileDto
            {
                Id          = r.Id,
                FileName    = r.FileName,
                ContentType = r.ContentType,
            }).ToList());

        foreach (var t in templates)
            t.LinkedResourceFiles = byTemplate.GetValueOrDefault(t.Id) ?? new();
    }

    // Inserts resource file links for a template. Silently skips unknown file IDs.
    private async Task SaveResourceFileLinksAsync(int templateId, List<int> fileIds)
    {
        foreach (var fileId in fileIds.Distinct())
        {
            await _db.SaveDataAsync(
                "INSERT OR IGNORE INTO TaskTemplateResourceFiles (TaskTemplateId, ResourceFileId) VALUES (@TemplateId, @FileId)",
                new { TemplateId = templateId, FileId = fileId });
        }
    }

    private sealed class LinkedFileRow
    {
        public int    TaskTemplateId { get; set; }
        public int    Id             { get; set; }
        public string FileName       { get; set; } = "";
        public string ContentType    { get; set; } = "";
    }
}
