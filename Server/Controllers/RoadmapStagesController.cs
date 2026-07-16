using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Server.Services;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  RoadmapStagesController — /api/roadmap-stages
//
//  Two surfaces share this controller:
//
//   1. Admin / Lecturer configuration (CRUD + link milestone-templates):
//        GET    /api/roadmap-stages/cycle/{academicYearId}                 → stages with linked milestones
//        GET    /api/roadmap-stages/cycle/{academicYearId}/available-milestones
//                                                                          → all AYMs in the cycle for the linker
//        POST   /api/roadmap-stages/cycle/{academicYearId}                 → create a stage
//        PUT    /api/roadmap-stages/{id}                                   → edit
//        PATCH  /api/roadmap-stages/{id}/active                            → toggle
//        PUT    /api/roadmap-stages/{id}/milestones                        → replace linked milestone set
//        DELETE /api/roadmap-stages/{id}                                   → delete (cascades RoadmapStageId=NULL)
//
//   2. Mentor / Admin / Staff per-project progress:
//        GET    /api/roadmap-stages/projects/{projectId}/progress          → roadmap progress for one project
//
//  Authorization:
//    - All CRUD endpoints require Admin or Staff (Lecturer ⇒ Admin invariant
//      already in place; Staff has the same access).
//    - /progress is open to Admin, Staff, and Mentor. Mentor calls are
//      scoped: a mentor can only read progress for a project they mentor
//      (verified via ProjectMentors).
//
//  No new tables for the link layer — milestone binding lives on the
//  existing AcademicYearMilestones.RoadmapStageId column.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/roadmap-stages")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize]
public class RoadmapStagesController : ControllerBase
{
    private readonly DbRepository _db;
    private readonly ProjectRoadmapService _roadmapService;

    public RoadmapStagesController(DbRepository db, ProjectRoadmapService roadmapService)
    {
        _db = db;
        _roadmapService = roadmapService;
    }

    // ───── Admin / Lecturer CRUD ────────────────────────────────────────────

    // GET /api/roadmap-stages/cycle/{academicYearId}
    [HttpGet("cycle/{academicYearId:int}")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> GetForCycle(int academicYearId)
    {
        const string sql = @"
            SELECT  Id, AcademicYearId, Code, Name, Description,
                    SuggestedStartDate, SuggestedEndDate,
                    DisplayOrder, IsActive, CreatedAt, UpdatedAt
            FROM    RoadmapStages
            WHERE   AcademicYearId = @YearId
            ORDER   BY DisplayOrder, Id";

        var stages = (await _db.GetRecordsAsync<RoadmapStageDto>(
            sql, new { YearId = academicYearId }))?.ToList()
            ?? new List<RoadmapStageDto>();

        if (stages.Count > 0)
            await AttachLinkedMilestonesAsync(stages, academicYearId);

        return Ok(stages);
    }

    // GET /api/roadmap-stages/cycle/{academicYearId}/available-milestones
    // Lists every AcademicYearMilestone in the cycle (active or not) along
    // with the template title, sorted by template order. The linker UI shows
    // this list and toggles checkboxes against the current stage assignments.
    [HttpGet("cycle/{academicYearId:int}/available-milestones")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> GetAvailableMilestones(int academicYearId)
    {
        const string sql = @"
            SELECT  aym.Id                  AS AcademicYearMilestoneId,
                    aym.MilestoneTemplateId,
                    mt.Title                AS Title,
                    mt.OrderIndex           AS OrderIndex,
                    aym.RoadmapStageId      AS CurrentStageId
            FROM    AcademicYearMilestones aym
            JOIN    MilestoneTemplates     mt ON mt.Id = aym.MilestoneTemplateId
            WHERE   aym.AcademicYearId = @YearId
            ORDER   BY mt.OrderIndex, aym.Id";

        var rows = (await _db.GetRecordsAsync<AvailableMilestoneRow>(
            sql, new { YearId = academicYearId }))?.ToList()
            ?? new List<AvailableMilestoneRow>();

        return Ok(rows);
    }

    // POST /api/roadmap-stages/cycle/{academicYearId}
    [HttpPost("cycle/{academicYearId:int}")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Create(int academicYearId, [FromBody] SaveRoadmapStageRequest req)
    {
        var err = ValidateRequest(req, isCreate: true);
        if (err is not null) return BadRequest(err);

        // Cycle existence check — surfaces a clearer error than a FK violation.
        bool cycleExists = (await _db.GetRecordsAsync<int>(
            "SELECT 1 FROM AcademicYears WHERE Id = @Id LIMIT 1",
            new { Id = academicYearId }))?.Any() ?? false;
        if (!cycleExists) return NotFound("המחזור לא נמצא");

        string code = req.Code.Trim();
        bool codeExists = (await _db.GetRecordsAsync<int>(@"
            SELECT 1 FROM RoadmapStages
            WHERE  AcademicYearId = @YearId AND Code = @Code COLLATE NOCASE LIMIT 1",
            new { YearId = academicYearId, Code = code }))?.Any() ?? false;
        if (codeExists)
            return Conflict("קוד שלב כבר קיים במחזור זה — יש לבחור קוד אחר");

        int newId = await _db.InsertReturnIdAsync(@"
            INSERT INTO RoadmapStages
                (AcademicYearId, Code, Name, Description,
                 SuggestedStartDate, SuggestedEndDate,
                 DisplayOrder, IsActive, CreatedAt, UpdatedAt)
            VALUES
                (@YearId, @Code, @Name, @Description,
                 @SuggestedStartDate, @SuggestedEndDate,
                 @DisplayOrder, @IsActive, datetime('now'), datetime('now'))",
            new
            {
                YearId             = academicYearId,
                Code               = code,
                Name               = req.Name.Trim(),
                Description        = (req.Description ?? "").Trim(),
                SuggestedStartDate = req.SuggestedStartDate?.ToString("yyyy-MM-dd"),
                SuggestedEndDate   = req.SuggestedEndDate?.ToString("yyyy-MM-dd"),
                DisplayOrder       = req.DisplayOrder,
                IsActive           = req.IsActive ? 1 : 0,
            });

        if (newId == 0) return StatusCode(500, "שגיאה ביצירת השלב");
        return Ok(await LoadOneAsync(newId));
    }

    // PUT /api/roadmap-stages/{id}
    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Update(int id, [FromBody] SaveRoadmapStageRequest req)
    {
        var err = ValidateRequest(req, isCreate: false);
        if (err is not null) return BadRequest(err);

        int affected = await _db.SaveDataAsync(@"
            UPDATE RoadmapStages
            SET    Name               = @Name,
                   Description        = @Description,
                   SuggestedStartDate = @SuggestedStartDate,
                   SuggestedEndDate   = @SuggestedEndDate,
                   DisplayOrder       = @DisplayOrder,
                   IsActive           = @IsActive,
                   UpdatedAt          = datetime('now')
            WHERE  Id = @Id",
            new
            {
                Id                 = id,
                Name               = req.Name.Trim(),
                Description        = (req.Description ?? "").Trim(),
                SuggestedStartDate = req.SuggestedStartDate?.ToString("yyyy-MM-dd"),
                SuggestedEndDate   = req.SuggestedEndDate?.ToString("yyyy-MM-dd"),
                DisplayOrder       = req.DisplayOrder,
                IsActive           = req.IsActive ? 1 : 0,
            });

        if (affected == 0) return NotFound("השלב לא נמצא");
        return Ok(await LoadOneAsync(id));
    }

    // PATCH /api/roadmap-stages/{id}/active
    [HttpPatch("{id:int}/active")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> ToggleActive(int id, [FromBody] ToggleRoadmapStageActiveRequest req)
    {
        int affected = await _db.SaveDataAsync(@"
            UPDATE RoadmapStages
            SET    IsActive  = @IsActive,
                   UpdatedAt = datetime('now')
            WHERE  Id = @Id",
            new { Id = id, IsActive = req.IsActive ? 1 : 0 });

        if (affected == 0) return NotFound("השלב לא נמצא");
        return Ok();
    }

    // PUT /api/roadmap-stages/{id}/milestones
    // Replaces the full set of AcademicYearMilestones bound to this stage.
    // Items not in the list become unbound (RoadmapStageId = NULL); items
    // bound to a different stage get re-bound to this one.
    [HttpPut("{id:int}/milestones")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> LinkMilestones(int id, [FromBody] LinkStageMilestonesRequest req)
    {
        var stage = (await _db.GetRecordsAsync<StageOwnerRow>(
            "SELECT Id, AcademicYearId FROM RoadmapStages WHERE Id = @Id LIMIT 1",
            new { Id = id }))?.FirstOrDefault();
        if (stage is null) return NotFound("השלב לא נמצא");

        // Clear current bindings (only within the same cycle — never touch
        // AYM rows in other cycles, even if they erroneously reference this
        // stage id, since stage ids are unique across cycles via PK).
        await _db.SaveDataAsync(@"
            UPDATE AcademicYearMilestones
            SET    RoadmapStageId = NULL
            WHERE  RoadmapStageId = @Id",
            new { Id = id });

        if (req?.AcademicYearMilestoneIds is { Count: > 0 })
        {
            // Bind only AYM rows that actually belong to the stage's cycle.
            // This both prevents cross-cycle binding and silently ignores
            // bogus ids sent by a misbehaving client.
            var idList = string.Join(",", req.AcademicYearMilestoneIds.Where(x => x > 0).Distinct());
            if (idList.Length > 0)
            {
                var bindSql = $@"
                    UPDATE AcademicYearMilestones
                    SET    RoadmapStageId = @Id
                    WHERE  Id IN ({idList})
                      AND  AcademicYearId = @YearId";
                await _db.SaveDataAsync(bindSql, new { Id = id, YearId = stage.AcademicYearId });
            }
        }

        return Ok(await LoadOneAsync(id));
    }

    // DELETE /api/roadmap-stages/{id}
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Delete(int id)
    {
        // The trigger trg_roadmap_stage_delete_nullify clears
        // AcademicYearMilestones.RoadmapStageId pointing at this stage so
        // bound milestones become "unassigned" rather than orphaned.
        int affected = await _db.SaveDataAsync(
            "DELETE FROM RoadmapStages WHERE Id = @Id",
            new { Id = id });

        if (affected == 0) return NotFound("השלב לא נמצא");
        return Ok();
    }

    // ───── Mentor / Admin progress ──────────────────────────────────────────

    // GET /api/roadmap-stages/mentor/projects-progress
    //
    // Batched overview endpoint backing the "ציר התקדמות פרויקטים" page in
    // the mentor side-nav. One round-trip returns one ProjectRoadmapProgressDto
    // per team the caller can see:
    //   - Mentor       → projects they are listed on in ProjectMentors
    //   - Admin / Staff → every active project in every active cycle
    // The list is naturally small (a mentor supervises a handful of teams),
    // so we just call BuildProgressAsync per project instead of inventing a
    // separate aggregate query.
    [HttpGet("mentor/projects-progress")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Mentor)]
    public async Task<IActionResult> GetMentorProjectsProgress(int authUserId)
    {
        bool isMentorOnly = User.IsInRole(Roles.Mentor)
                            && !User.IsInRole(Roles.Admin)
                            && !User.IsInRole(Roles.Staff);

        // Pull (ProjectId, AcademicYearId, Title, ProjectNumber, TeamName)
        // for every project the caller can see. Mentors get only their own
        // projects; admin/staff get everything in active cycles.
        string sql = isMentorOnly
            ? @"
                SELECT  p.Id            AS ProjectId,
                        p.AcademicYearId,
                        p.Title         AS ProjectTitle,
                        p.ProjectNumber,
                        COALESCE(t.TeamName, '')  AS TeamName
                FROM    Projects        p
                JOIN    ProjectMentors  pm ON pm.ProjectId = p.Id
                LEFT JOIN Teams         t  ON t.Id = p.TeamId
                WHERE   pm.UserId = @UserId
                ORDER   BY p.ProjectNumber"
            : @"
                SELECT  p.Id            AS ProjectId,
                        p.AcademicYearId,
                        p.Title         AS ProjectTitle,
                        p.ProjectNumber,
                        COALESCE(t.TeamName, '')  AS TeamName
                FROM    Projects        p
                LEFT JOIN Teams         t  ON t.Id = p.TeamId
                JOIN    AcademicYears   y  ON y.Id = p.AcademicYearId
                WHERE   y.IsActive = 1
                ORDER   BY p.ProjectNumber";

        var projects = (await _db.GetRecordsAsync<MentorProjectListRow>(
            sql, new { UserId = authUserId }))?.ToList()
            ?? new List<MentorProjectListRow>();

        var result = new List<MentorRoadmapEntryDto>(projects.Count);
        foreach (var p in projects)
        {
            var progress = await _roadmapService.GetProjectRoadmapAsync(p.ProjectId, p.AcademicYearId);
            result.Add(new MentorRoadmapEntryDto
            {
                ProjectId     = p.ProjectId,
                ProjectNumber = p.ProjectNumber,
                ProjectTitle  = p.ProjectTitle,
                TeamName      = p.TeamName,
                Progress      = progress,
            });
        }

        return Ok(result);
    }

    // GET /api/roadmap-stages/projects/{projectId}/progress
    [HttpGet("projects/{projectId:int}/progress")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Mentor)]
    public async Task<IActionResult> GetProjectProgress(int projectId, int authUserId)
    {
        // Resolve the cycle + ownership check in a single round-trip.
        var ctx = (await _db.GetRecordsAsync<ProjectCycleRow>(@"
            SELECT  p.Id AS ProjectId, p.AcademicYearId
            FROM    Projects p
            WHERE   p.Id = @Id LIMIT 1",
            new { Id = projectId }))?.FirstOrDefault();
        if (ctx is null) return NotFound("הפרויקט לא נמצא");

        // Mentor scope: only projects they actually mentor.
        if (User.IsInRole(Roles.Mentor)
            && !User.IsInRole(Roles.Admin)
            && !User.IsInRole(Roles.Staff))
        {
            bool isMentorOf = (await _db.GetRecordsAsync<int>(
                "SELECT 1 FROM ProjectMentors WHERE ProjectId = @P AND UserId = @U LIMIT 1",
                new { P = projectId, U = authUserId }))?.Any() ?? false;
            if (!isMentorOf) return NotFound("הפרויקט לא נמצא");
        }

        var result = await _roadmapService.GetProjectRoadmapAsync(projectId, ctx.AcademicYearId);
        return Ok(result);
    }

    // GET /api/roadmap-stages/my-progress
    // Student-facing: roadmap progress for the CALLER's own project.
    // Mirrors ProjectsController's "my-*" endpoints — no role restriction
    // beyond the class-level [Authorize], because the query below already
    // scopes every result to the caller's own team/project via authUserId.
    // Reuses the same BuildProgressAsync the mentor/admin endpoint above
    // uses; that endpoint's own authorization is left untouched.
    [HttpGet("my-progress")]
    public async Task<IActionResult> GetMyProgress(int authUserId)
    {
        // Draft assignments stay hidden from students, same rule as
        // ProjectsController.GetMyDashboard.
        var ctx = (await _db.GetRecordsAsync<ProjectCycleRow>(@"
            SELECT  p.Id AS ProjectId, p.AcademicYearId
            FROM    Projects     p
            JOIN    Teams        t  ON p.TeamId  = t.Id
            JOIN    TeamMembers  tm ON t.Id       = tm.TeamId
            WHERE   tm.UserId   = @UserId
              AND   tm.IsActive = 1
              AND   COALESCE(p.AssignmentIsDraft, 0) = 0
            LIMIT 1",
            new { UserId = authUserId }))?.FirstOrDefault();

        // No assigned project → empty payload (ProjectId = 0), not an error.
        // The client renders its own "not assigned yet" state, same pattern
        // as the student tasks/dashboard endpoints.
        if (ctx is null) return Ok(new ProjectRoadmapProgressDto());

        var result = await _roadmapService.GetProjectRoadmapAsync(ctx.ProjectId, ctx.AcademicYearId);
        return Ok(result);
    }

    // ───── Helpers ──────────────────────────────────────────────────────────

    private async Task<RoadmapStageDto?> LoadOneAsync(int id)
    {
        const string sql = @"
            SELECT  Id, AcademicYearId, Code, Name, Description,
                    SuggestedStartDate, SuggestedEndDate,
                    DisplayOrder, IsActive, CreatedAt, UpdatedAt
            FROM    RoadmapStages
            WHERE   Id = @Id LIMIT 1";

        var stage = (await _db.GetRecordsAsync<RoadmapStageDto>(sql, new { Id = id }))?.FirstOrDefault();
        if (stage is null) return null;

        await AttachLinkedMilestonesAsync(new List<RoadmapStageDto> { stage }, stage.AcademicYearId);
        return stage;
    }

    private async Task AttachLinkedMilestonesAsync(List<RoadmapStageDto> stages, int academicYearId)
    {
        const string sql = @"
            SELECT  aym.Id                AS AcademicYearMilestoneId,
                    aym.MilestoneTemplateId,
                    aym.RoadmapStageId    AS StageId,
                    mt.Title              AS Title,
                    mt.OrderIndex         AS OrderIndex
            FROM    AcademicYearMilestones aym
            JOIN    MilestoneTemplates     mt ON mt.Id = aym.MilestoneTemplateId
            WHERE   aym.AcademicYearId = @YearId
              AND   aym.RoadmapStageId IS NOT NULL
            ORDER   BY mt.OrderIndex, aym.Id";

        var rows = (await _db.GetRecordsAsync<LinkedMilestoneRow>(
            sql, new { YearId = academicYearId }))?.ToList()
            ?? new List<LinkedMilestoneRow>();

        var byStage = rows.GroupBy(r => r.StageId)
                          .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var s in stages)
        {
            if (byStage.TryGetValue(s.Id, out var list))
            {
                s.LinkedMilestones = list.Select(r => new RoadmapStageMilestoneDto
                {
                    AcademicYearMilestoneId = r.AcademicYearMilestoneId,
                    MilestoneTemplateId     = r.MilestoneTemplateId,
                    Title                   = r.Title,
                    OrderIndex              = r.OrderIndex,
                }).ToList();
            }
        }
    }

    // Roadmap/progress computation itself lives in Server/Services/ProjectRoadmapService.cs
    // (design/business-logic-consolidation-epic.md, Concepts 2 & 3) — this
    // controller no longer computes it. See that file for the calculation
    // rules previously documented here.

    private static string? ValidateRequest(SaveRoadmapStageRequest req, bool isCreate)
    {
        if (req is null) return "גוף בקשה ריק";

        if (isCreate)
        {
            string code = (req.Code ?? "").Trim();
            if (string.IsNullOrEmpty(code))
                return "קוד השלב הוא שדה חובה";
            if (code.Length > 64)
                return "קוד השלב ארוך מדי (עד 64 תווים)";
            foreach (var ch in code)
                if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'))
                    return "קוד השלב יכול להכיל אותיות באנגלית, ספרות, מקף ותחתון בלבד";
        }

        if (string.IsNullOrWhiteSpace(req.Name))
            return "שם השלב הוא שדה חובה";
        if (req.Name.Length > 120)
            return "שם השלב ארוך מדי (עד 120 תווים)";

        if (req.SuggestedStartDate.HasValue && req.SuggestedEndDate.HasValue
            && req.SuggestedEndDate.Value.Date < req.SuggestedStartDate.Value.Date)
            return "תאריך סיום חייב להיות אחרי תאריך התחלה";

        if (req.DisplayOrder < 0 || req.DisplayOrder > 999)
            return "סדר תצוגה חייב להיות בטווח 0–999";

        return null;
    }

    // ── Private row types ───────────────────────────────────────────────────

    private sealed class StageOwnerRow
    {
        public int Id             { get; set; }
        public int AcademicYearId { get; set; }
    }

    private sealed class AvailableMilestoneRow
    {
        public int    AcademicYearMilestoneId { get; set; }
        public int    MilestoneTemplateId     { get; set; }
        public string Title                   { get; set; } = "";
        public int    OrderIndex              { get; set; }
        public int?   CurrentStageId          { get; set; }
    }

    private sealed class LinkedMilestoneRow
    {
        public int    AcademicYearMilestoneId { get; set; }
        public int    MilestoneTemplateId     { get; set; }
        public int    StageId                 { get; set; }
        public string Title                   { get; set; } = "";
        public int    OrderIndex              { get; set; }
    }

    private sealed class ProjectCycleRow
    {
        public int ProjectId      { get; set; }
        public int AcademicYearId { get; set; }
    }

    private sealed class MentorProjectListRow
    {
        public int    ProjectId       { get; set; }
        public int    AcademicYearId  { get; set; }
        public string ProjectTitle    { get; set; } = "";
        public int    ProjectNumber   { get; set; }
        public string TeamName        { get; set; } = "";
    }
}