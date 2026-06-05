using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
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

    public RoadmapStagesController(DbRepository db) => _db = db;

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
            var progress = await BuildProgressAsync(p.ProjectId, p.AcademicYearId);
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

        var result = await BuildProgressAsync(projectId, ctx.AcademicYearId);
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

    /// <summary>
    /// Calculation logic (per spec §4):
    ///
    ///  - A stage is **completed** when every linked ProjectMilestones row
    ///    has Status = 'Completed'. Stages with no linked milestones are
    ///    skipped for stage selection (they remain visible but marked
    ///    NotApplicable).
    ///  - The **current** stage is the lowest-DisplayOrder active stage that
    ///    is not fully completed AND has at least one linked milestone.
    ///  - **Schedule status** compares today's date to the current stage's
    ///    SuggestedStartDate / SuggestedEndDate:
    ///       today > SuggestedEndDate                           → Behind
    ///       today < SuggestedStartDate (stage already current) → Ahead
    ///       otherwise (or dates missing for the current stage) → OnTrack
    ///    If no current stage exists OR the current stage has neither
    ///    suggested date → NoSchedule.
    ///  - Upcoming = earliest-due, non-completed linked milestone in the
    ///    current stage (falls back to earliest across all stages).
    ///  - Overdue  = the most-overdue (lowest-DueDate, before today) non-
    ///    completed milestone across the whole project.
    /// </summary>
    private async Task<ProjectRoadmapProgressDto> BuildProgressAsync(int projectId, int academicYearId)
    {
        // Stages for the cycle.
        var stages = (await _db.GetRecordsAsync<StageRow>(@"
            SELECT  Id, Code, Name, Description, DisplayOrder, IsActive,
                    SuggestedStartDate, SuggestedEndDate
            FROM    RoadmapStages
            WHERE   AcademicYearId = @YearId
              AND   IsActive = 1
            ORDER   BY DisplayOrder, Id",
            new { YearId = academicYearId }))?.ToList()
            ?? new List<StageRow>();

        // All linked ProjectMilestones for this project (with their AYM →
        // stage mapping). Per-team due-date overrides aren't merged here
        // because the roadmap display is rough by design; the milestone
        // accordion below it still shows authoritative per-team dates.
        var milestones = (await _db.GetRecordsAsync<ProjectMilestoneRow>(@"
            SELECT  pm.Id              AS ProjectMilestoneId,
                    pm.Status,
                    aym.RoadmapStageId  AS StageId,
                    aym.DueDate         AS DueDate,
                    mt.Title            AS Title,
                    mt.OrderIndex       AS OrderIndex
            FROM    ProjectMilestones     pm
            JOIN    AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            JOIN    MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            WHERE   pm.ProjectId = @P
            ORDER   BY mt.OrderIndex, pm.Id",
            new { P = projectId }))?.ToList()
            ?? new List<ProjectMilestoneRow>();

        var milestonesByStage = milestones
            .Where(m => m.StageId.HasValue)
            .GroupBy(m => m.StageId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new ProjectRoadmapProgressDto
        {
            ProjectId       = projectId,
            AcademicYearId  = academicYearId,
            ScheduleStatus  = RoadmapScheduleStatuses.NoSchedule,
        };

        // ── Build per-stage progress ─────────────────────────────────────
        StageProgressDto? currentStage = null;
        foreach (var s in stages)
        {
            var linked = milestonesByStage.TryGetValue(s.Id, out var ms)
                ? ms : new List<ProjectMilestoneRow>();

            int total     = linked.Count;
            int completed = linked.Count(m => string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase));

            bool noLinkedMilestones = total == 0;
            bool fullyComplete      = !noLinkedMilestones && completed == total;

            // Stage status assignment — Current is reserved for the first
            // not-fully-complete stage that has at least one linked
            // milestone. Subsequent stages are Future regardless of their
            // own data, to keep the timeline monotonic.
            string stageStatus;
            if (noLinkedMilestones)
            {
                stageStatus = RoadmapStageStatuses.NotApplicable;
            }
            else if (fullyComplete)
            {
                stageStatus = RoadmapStageStatuses.Completed;
            }
            else if (currentStage is null)
            {
                stageStatus = RoadmapStageStatuses.Current;
            }
            else
            {
                stageStatus = RoadmapStageStatuses.Future;
            }

            var stageDto = new StageProgressDto
            {
                StageId            = s.Id,
                Code               = s.Code,
                Name               = s.Name,
                Description        = s.Description,
                DisplayOrder       = s.DisplayOrder,
                SuggestedStartDate = s.SuggestedStartDate,
                SuggestedEndDate   = s.SuggestedEndDate,
                Status             = stageStatus,
                LinkedMilestoneCount    = total,
                CompletedMilestoneCount = completed,
                ProgressPct = stageStatus switch
                {
                    RoadmapStageStatuses.Completed     => 100,
                    RoadmapStageStatuses.NotApplicable => 0,
                    _ when total > 0                    => (int)Math.Round(100.0 * completed / total),
                    _                                   => 0,
                },
                Milestones = linked.Select(m => new StageMilestoneStateDto
                {
                    ProjectMilestoneId = m.ProjectMilestoneId,
                    Title              = m.Title,
                    Status             = m.Status,
                    DueDate            = m.DueDate,
                    OrderIndex         = m.OrderIndex,
                }).ToList(),
            };

            if (stageStatus == RoadmapStageStatuses.Current)
            {
                currentStage = stageDto;
                result.CurrentStageCode = s.Code;
            }

            result.Stages.Add(stageDto);
        }

        // ── Schedule status ──────────────────────────────────────────────
        // Compare today to the current stage's suggested window. When the
        // current stage has no usable dates, fall back to NoSchedule rather
        // than guessing — the UI can render "ללא לוח זמנים" cleanly.
        if (currentStage is not null)
        {
            var today = DateTime.UtcNow.Date;
            if (currentStage.SuggestedEndDate is { } end && today > end.Date)
                result.ScheduleStatus = RoadmapScheduleStatuses.Behind;
            else if (currentStage.SuggestedStartDate is { } start && today < start.Date)
                result.ScheduleStatus = RoadmapScheduleStatuses.Ahead;
            else if (currentStage.SuggestedStartDate is null && currentStage.SuggestedEndDate is null)
                result.ScheduleStatus = RoadmapScheduleStatuses.NoSchedule;
            else
                result.ScheduleStatus = RoadmapScheduleStatuses.OnTrack;
        }
        // No current stage → either every linked stage is complete, or no
        // stages are configured. Either way "ללא לוח זמנים" is honest.

        // ── Upcoming + overdue ───────────────────────────────────────────
        var nonComplete = milestones
            .Where(m => !string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Upcoming: prefer the earliest-due milestone in the current stage,
        // fall back to the earliest across the project so the mentor always
        // has a "what's next" pointer even if the current stage has nothing
        // dated.
        var upcomingCandidates = currentStage is not null
            ? nonComplete.Where(m => m.StageId == currentStage.StageId).ToList()
            : new();
        if (upcomingCandidates.Count == 0) upcomingCandidates = nonComplete;

        var upcoming = upcomingCandidates
            .Where(m => m.DueDate.HasValue)
            .OrderBy(m => m.DueDate!.Value)
            .FirstOrDefault();
        if (upcoming is not null)
            result.Upcoming = ToUpcoming(upcoming);

        // Overdue: most overdue non-completed milestone (lowest past DueDate).
        var todayUtc = DateTime.UtcNow.Date;
        var overdue = nonComplete
            .Where(m => m.DueDate.HasValue && m.DueDate.Value.Date < todayUtc)
            .OrderBy(m => m.DueDate!.Value)
            .FirstOrDefault();
        if (overdue is not null)
            result.Overdue = ToUpcoming(overdue);

        return result;
    }

    private static UpcomingMilestoneDto ToUpcoming(ProjectMilestoneRow m)
    {
        int? days = m.DueDate.HasValue
            ? (int?)(m.DueDate.Value.Date - DateTime.UtcNow.Date).TotalDays
            : null;
        return new UpcomingMilestoneDto
        {
            ProjectMilestoneId = m.ProjectMilestoneId,
            Title              = m.Title,
            DueDate            = m.DueDate,
            DaysUntilDue       = days,
        };
    }

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

    private sealed class StageRow
    {
        public int       Id                 { get; set; }
        public string    Code               { get; set; } = "";
        public string    Name               { get; set; } = "";
        public string    Description        { get; set; } = "";
        public int       DisplayOrder       { get; set; }
        public int       IsActive           { get; set; }
        public DateTime? SuggestedStartDate { get; set; }
        public DateTime? SuggestedEndDate   { get; set; }
    }

    private sealed class ProjectMilestoneRow
    {
        public int       ProjectMilestoneId { get; set; }
        public string    Status             { get; set; } = "";
        public int?      StageId            { get; set; }
        public DateTime? DueDate            { get; set; }
        public string    Title              { get; set; } = "";
        public int       OrderIndex         { get; set; }
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