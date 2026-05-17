using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  MilestonesOverviewController — /api/milestones/overview
//
//  Cross-project milestone snapshot for the lecturer/admin "אבני דרך" page.
//  Mentor scope is also supported (only their mentored projects). Server-side
//  scope enforcement: a Mentor cannot widen to lecturer scope by URL.
//
//  Definition of an "active assigned project" matches the lecturer dashboard:
//    1. p.AcademicYearId = current
//    2. p.TeamId IS NOT NULL
//    3. COALESCE(p.AssignmentIsDraft, 0) = 0
//    4. p.Status NOT IN ('Available','Unavailable')   — exclude catalog states
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/milestones/overview")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
public class MilestonesOverviewController : ControllerBase
{
    private readonly DbRepository _db;
    public MilestonesOverviewController(DbRepository db) => _db = db;

    [HttpGet]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Mentor)]
    public async Task<IActionResult> GetOverview(int authUserId, [FromQuery] string scope = "lecturer")
    {
        // Scope binding — same pattern as the lecturer dashboard.
        bool isAdminOrStaff = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Staff);
        bool isMentor       = User.IsInRole(Roles.Mentor);
        string effectiveScope =
            (isAdminOrStaff && scope == "lecturer") ? "lecturer" :
            isMentor                                ? "mentor"   :
            isAdminOrStaff                          ? "lecturer" :
            "mentor";
        bool restrictToMentor = effectiveScope == "mentor";

        // Resolve current academic year (fall back to most recent).
        const string yearSql = @"
            SELECT Id FROM AcademicYears WHERE IsCurrent = 1
            UNION ALL
            SELECT Id FROM AcademicYears ORDER BY Id DESC
            LIMIT 1";
        var currentYearId = (await _db.GetRecordsAsync<int>(yearSql))?.FirstOrDefault() ?? 0;

        // Active project list within scope.
        const string projectsSqlAll = @"
            SELECT  p.Id            AS ProjectId,
                    p.ProjectNumber,
                    p.Title         AS ProjectTitle,
                    p.TeamId,
                    t.TeamName,
                    pt.Name         AS ProjectType,
                    (SELECT GROUP_CONCAT(u.FirstName || ' ' || u.LastName, ', ')
                     FROM   ProjectMentors pmm
                     JOIN   users          u ON u.Id = pmm.UserId
                     WHERE  pmm.ProjectId = p.Id) AS MentorNames
            FROM    Projects     p
            LEFT JOIN Teams       t  ON t.Id = p.TeamId
            JOIN    ProjectTypes pt  ON pt.Id = p.ProjectTypeId
            WHERE   p.AcademicYearId   = @YearId
              AND   COALESCE(p.AssignmentIsDraft, 0) = 0
              AND   p.TeamId IS NOT NULL
              AND   p.Status NOT IN ('Available', 'Unavailable')";

        const string projectsSqlMentorOnly = @"
              AND   p.Id IN (SELECT ProjectId FROM ProjectMentors WHERE UserId = @UserId)";

        string projectsSql = projectsSqlAll
                           + (restrictToMentor ? projectsSqlMentorOnly : "")
                           + " ORDER BY p.ProjectNumber";

        var projects = (await _db.GetRecordsAsync<ProjectBaseRow>(
            projectsSql, new { YearId = currentYearId, UserId = authUserId }))?.ToList() ?? new();

        if (projects.Count == 0)
        {
            return Ok(new MilestonesOverviewDto
            {
                Scope = effectiveScope,
            });
        }

        var projectIds = projects.Select(p => p.ProjectId).ToList();
        string idsCsv  = string.Join(",", projectIds);

        // Per-project milestone rows — picks the project's CURRENT milestone
        // (first by mt.OrderIndex whose Status is not Completed). Effective
        // due date = COALESCE(team milestone override, AYM.DueDate).
        string milestonesSql = $@"
            SELECT  pm.ProjectId   AS ProjectId,
                    pm.Id          AS ProjectMilestoneId,
                    mt.Title       AS Title,
                    mt.OrderIndex  AS OrderIndex,
                    pm.Status      AS Status,
                    COALESCE(mo.OverrideDueDate, aym.DueDate) AS DueDate
            FROM    ProjectMilestones       pm
            JOIN    AcademicYearMilestones  aym ON aym.Id = pm.AcademicYearMilestoneId
            JOIN    MilestoneTemplates      mt  ON mt.Id  = aym.MilestoneTemplateId
            LEFT JOIN Projects p ON p.Id = pm.ProjectId
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = p.TeamId AND mo.ProjectMilestoneId = pm.Id
            WHERE   pm.ProjectId IN ({idsCsv})
            ORDER   BY pm.ProjectId, mt.OrderIndex";
        var milestoneRows = (await _db.GetRecordsAsync<MilestoneRow>(milestonesSql))?.ToList() ?? new();

        // Per-(project, milestone) task counts — effective due date via the
        // full priority chain. Includes:
        //   - TasksCompleted: task.Status IN ('Done','Completed','SubmittedToMentor')
        //                     OR has any submission row
        //   - TasksTotal:     all tasks under that milestone
        //   - MissingSubmissions: mandatory IsSubmission=1 tasks past their
        //                         effective due date with no submission yet
        string milestoneTasksSql = $@"
            SELECT  t.ProjectId          AS ProjectId,
                    t.ProjectMilestoneId AS ProjectMilestoneId,
                    COUNT(*)             AS TasksTotal,
                    SUM(CASE
                          WHEN t.Status IN ('Done','Completed','SubmittedToMentor')
                            OR EXISTS (SELECT 1 FROM TaskSubmissions s WHERE s.TaskId = t.Id)
                          THEN 1 ELSE 0
                        END) AS TasksCompleted,
                    SUM(CASE
                          WHEN t.IsSubmission = 1
                            AND t.IsMandatory  = 1
                            AND NOT EXISTS (SELECT 1 FROM TaskSubmissions s WHERE s.TaskId = t.Id)
                            AND date(COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate)) < date('now')
                          THEN 1 ELSE 0
                        END) AS MissingSubmissions
            FROM    Tasks t
            LEFT JOIN Projects p  ON p.Id = t.ProjectId
            LEFT JOIN TeamTaskDueDateOverrides tto
                            ON tto.TeamId = p.TeamId AND tto.TaskId = t.Id
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = p.TeamId AND mo.ProjectMilestoneId = t.ProjectMilestoneId
            WHERE   t.ProjectId IN ({idsCsv})
            GROUP   BY t.ProjectId, t.ProjectMilestoneId";
        var taskCounts = (await _db.GetRecordsAsync<TaskCountsRow>(milestoneTasksSql))?.ToList() ?? new();

        var milestonesByProject = milestoneRows.GroupBy(m => m.ProjectId)
                                               .ToDictionary(g => g.Key, g => g.ToList());
        var taskCountsByPair    = taskCounts.ToDictionary(
            r => (r.ProjectId, r.ProjectMilestoneId), r => r);

        // Assemble per-project rows.
        var rows = new List<MilestonesOverviewRowDto>(projects.Count);
        foreach (var p in projects)
        {
            // Pick the first non-completed milestone by order. If every
            // milestone is completed, surface the highest-order one with
            // status="Completed" so the row still shows "where they finished".
            MilestoneRow? current = null;
            if (milestonesByProject.TryGetValue(p.ProjectId, out var msList))
            {
                current = msList.FirstOrDefault(m => m.Status != "Completed")
                       ?? msList.LastOrDefault();
            }

            int completed = 0, total = 0, missing = 0;
            if (current is not null
                && taskCountsByPair.TryGetValue((p.ProjectId, current.ProjectMilestoneId), out var tc))
            {
                completed = tc.TasksCompleted;
                total     = tc.TasksTotal;
                missing   = tc.MissingSubmissions;
            }

            // Status:
            //   Overdue        — current milestone effective due date passed
            //                    AND milestone is not completed.
            //   NeedsAttention — has any missing required submission.
            //   Healthy        — neither.
            bool isOverdue = current is not null
                          && current.Status != "Completed"
                          && current.DueDate is not null
                          && current.DueDate.Value.Date < DateTime.Today;

            string status = isOverdue
                ? MilestoneOverviewStatuses.Overdue
                : (missing > 0 ? MilestoneOverviewStatuses.NeedsAttention
                               : MilestoneOverviewStatuses.Healthy);

            rows.Add(new MilestonesOverviewRowDto
            {
                ProjectId               = p.ProjectId,
                ProjectNumber           = p.ProjectNumber,
                ProjectTitle            = p.ProjectTitle,
                TeamName                = string.IsNullOrWhiteSpace(p.TeamName) ? null : p.TeamName,
                MentorNames             = string.IsNullOrWhiteSpace(p.MentorNames) ? null : p.MentorNames,
                ProjectType             = p.ProjectType,
                CurrentMilestoneTitle   = current?.Title,
                CurrentMilestoneDueDate = current?.DueDate,
                MilestoneTasksCompleted = completed,
                MilestoneTasksTotal     = total,
                MissingSubmissionCount  = missing,
                Status                  = status,
            });
        }

        // Summary cards — aggregate across all rows in scope.
        var summary = new MilestonesOverviewSummaryDto
        {
            TotalActiveProjects   = rows.Count,
            OverdueMilestones     = rows.Count(r => r.Status == MilestoneOverviewStatuses.Overdue),
            CompletedTasks        = rows.Sum(r => r.MilestoneTasksCompleted),
            OpenTasks             = rows.Sum(r => r.MilestoneTasksTotal - r.MilestoneTasksCompleted),
            TeamsNeedingAttention = rows.Count(r => r.Status != MilestoneOverviewStatuses.Healthy),
        };

        // Filter options — distinct values from the actual rows so dropdowns
        // never show ghosts that won't filter anything.
        var filters = new MilestonesOverviewFiltersDto
        {
            ProjectTypes = rows.Select(r => r.ProjectType)
                               .Where(s => !string.IsNullOrWhiteSpace(s))
                               .Distinct().OrderBy(s => s).ToList(),
            Milestones   = rows.Where(r => !string.IsNullOrWhiteSpace(r.CurrentMilestoneTitle))
                               .Select(r => r.CurrentMilestoneTitle!)
                               .Distinct().OrderBy(s => s).ToList(),
            Mentors      = rows.Where(r => !string.IsNullOrWhiteSpace(r.MentorNames))
                               .SelectMany(r => r.MentorNames!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                               .Distinct().OrderBy(s => s).ToList(),
        };

        return Ok(new MilestonesOverviewDto
        {
            Scope   = effectiveScope,
            Summary = summary,
            Filters = filters,
            Rows    = rows,
        });
    }

    // ── Private row types ────────────────────────────────────────────────────

    private sealed class ProjectBaseRow
    {
        public int     ProjectId     { get; set; }
        public int     ProjectNumber { get; set; }
        public string  ProjectTitle  { get; set; } = "";
        public int?    TeamId        { get; set; }
        public string? TeamName      { get; set; }
        public string  ProjectType   { get; set; } = "";
        public string? MentorNames   { get; set; }
    }

    private sealed class MilestoneRow
    {
        public int       ProjectId          { get; set; }
        public int       ProjectMilestoneId { get; set; }
        public string    Title              { get; set; } = "";
        public int       OrderIndex         { get; set; }
        public string    Status             { get; set; } = "";
        public DateTime? DueDate            { get; set; }
    }

    private sealed class TaskCountsRow
    {
        public int ProjectId          { get; set; }
        public int ProjectMilestoneId { get; set; }
        public int TasksTotal         { get; set; }
        public int TasksCompleted     { get; set; }
        public int MissingSubmissions { get; set; }
    }
}
