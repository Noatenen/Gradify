using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  LecturerDashboardController — /api/dashboard
//
//  Single endpoint for both lecturer/admin AND mentor dashboards. Scope is
//  enforced server-side: mentor users are forced to "mentor" regardless of
//  the query string, so the URL alone cannot widen visibility.
//
//  Effective due dates use the standard priority chain:
//    TeamTaskDueDateOverrides → TeamMilestoneDueDateOverrides → Tasks.DueDate
//  Globals are never mutated by this endpoint.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/dashboard")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
public class LecturerDashboardController : ControllerBase
{
    private readonly DbRepository _db;
    public LecturerDashboardController(DbRepository db) => _db = db;

    // ── GET /api/dashboard?scope=lecturer|mentor ────────────────────────────
    [HttpGet]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Mentor)]
    public async Task<IActionResult> GetOverview(int authUserId, [FromQuery] string scope = "lecturer")
    {
        // ── Scope resolution (server-side enforcement) ─────────────────────
        bool isAdminOrStaff = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Staff);
        bool isMentor       = User.IsInRole(Roles.Mentor);
        string effectiveScope =
            (isAdminOrStaff && scope == "lecturer") ? "lecturer" :
            isMentor                                ? "mentor"   :
            isAdminOrStaff                          ? "lecturer" :
            "mentor";

        // Mentor → only projects this user mentors. Admin/Staff → no restriction.
        // The mentor filter is added to all aggregate queries below.
        bool restrictToMentor = effectiveScope == "mentor";

        // ── Resolve current academic year ──────────────────────────────────
        const string yearSql = @"
            SELECT Id FROM AcademicYears WHERE IsCurrent = 1
            UNION ALL
            SELECT Id FROM AcademicYears ORDER BY Id DESC
            LIMIT 1";
        var currentYearId = (await _db.GetRecordsAsync<int>(yearSql))?.FirstOrDefault() ?? 0;

        // ── 1. Base project list within scope ──────────────────────────────
        //
        // Definition of an "active assigned project" — single source of truth
        // for both the lecturer and mentor dashboards:
        //
        //   1. p.AcademicYearId = current
        //   2. p.TeamId IS NOT NULL                    (a team is assigned)
        //   3. COALESCE(p.AssignmentIsDraft, 0) = 0    (assignment was published)
        //   4. p.Status NOT IN ('Available','Unavailable')
        //                                              (excludes catalog rows
        //                                               whose TeamId is leftover
        //                                               from earlier sessions)
        //
        // Rationale for #4: in this DB, every truly published project has
        // p.Status = 'InProgress' (or another non-catalog state). Many catalog
        // rows retained legacy TeamId values from prior backfills; if we look
        // only at TeamId + AssignmentIsDraft, the dashboard surfaces 109 rows
        // even though only 2 are real. The Status condition here is a catalog-
        // state EXCLUSION (we drop browse states), not a catalog-state
        // INCLUSION rule, which matches "don't show catalog projects".
        const string projectsSqlAll = @"
            SELECT  p.Id           AS ProjectId,
                    p.ProjectNumber,
                    p.Title        AS ProjectTitle,
                    p.Status       AS ProjectStatus,
                    p.TeamId,
                    t.TeamName,
                    p.AcademicYearId,
                    (SELECT GROUP_CONCAT(u.FirstName || ' ' || u.LastName, ', ')
                     FROM   ProjectMentors pmm
                     JOIN   users          u  ON u.Id = pmm.UserId
                     WHERE  pmm.ProjectId = p.Id) AS MentorNames
            FROM    Projects p
            LEFT JOIN Teams  t ON t.Id = p.TeamId
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
            return Ok(new DashboardOverviewDto
            {
                Scope    = effectiveScope,
                Summary  = new DashboardSummaryDto(),
                Charts   = new DashboardChartsDto(),
                Projects = new(),
            });
        }

        var projectIds = projects.Select(p => p.ProjectId).ToList();
        string idsCsv  = string.Join(",", projectIds);

        // ── 2. Per-project aggregate queries (one round-trip each) ─────────
        // 2a. Overdue mandatory tasks (per project) — uses effective due date.
        string overdueSql = $@"
            SELECT  t.ProjectId AS ProjectId,
                    COUNT(*)    AS Cnt
            FROM    Tasks t
            LEFT JOIN ProjectMilestones pm ON pm.Id = t.ProjectMilestoneId
            LEFT JOIN Projects          p  ON p.Id = t.ProjectId
            LEFT JOIN TeamTaskDueDateOverrides tto
                            ON tto.TeamId = p.TeamId AND tto.TaskId = t.Id
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = p.TeamId AND mo.ProjectMilestoneId = t.ProjectMilestoneId
            WHERE   t.ProjectId IN ({idsCsv})
              AND   t.IsMandatory = 1
              AND   (t.Status IS NULL OR t.Status NOT IN ('Done','Completed','SubmittedToMentor'))
              AND   t.ClosedAt IS NULL
              AND   NOT EXISTS (SELECT 1 FROM TaskSubmissions s WHERE s.TaskId = t.Id)
              AND   date(COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate)) < date('now')
            GROUP   BY t.ProjectId";
        var overdueByProject = (await _db.GetRecordsAsync<ProjectIntRow>(overdueSql))?.ToList() ?? new();

        // 2b. Missing required submissions (per project): submission tasks
        //     past their effective due date with NO submission attached.
        string missingSubsSql = $@"
            SELECT  t.ProjectId AS ProjectId,
                    COUNT(*)    AS Cnt
            FROM    Tasks t
            LEFT JOIN Projects p  ON p.Id = t.ProjectId
            LEFT JOIN TeamTaskDueDateOverrides tto
                            ON tto.TeamId = p.TeamId AND tto.TaskId = t.Id
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = p.TeamId AND mo.ProjectMilestoneId = t.ProjectMilestoneId
            WHERE   t.ProjectId IN ({idsCsv})
              AND   t.IsSubmission = 1
              AND   t.IsMandatory  = 1
              AND   NOT EXISTS (SELECT 1 FROM TaskSubmissions s WHERE s.TaskId = t.Id)
              AND   date(COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate)) < date('now')
            GROUP   BY t.ProjectId";
        var missingSubsByProject = (await _db.GetRecordsAsync<ProjectIntRow>(missingSubsSql))?.ToList() ?? new();

        // 2c. Submitted/total submission counts (for the chart split).
        string submittedSql = $@"
            SELECT  t.ProjectId AS ProjectId,
                    SUM(CASE WHEN EXISTS (SELECT 1 FROM TaskSubmissions s WHERE s.TaskId = t.Id) THEN 1 ELSE 0 END) AS Submitted,
                    COUNT(*) AS Total
            FROM    Tasks t
            WHERE   t.ProjectId IN ({idsCsv})
              AND   t.IsSubmission = 1
            GROUP   BY t.ProjectId";
        var subsByProject = (await _db.GetRecordsAsync<ProjectSubsRow>(submittedSql))?.ToList() ?? new();

        // 2d. Open requests (per project, not closed). Plus old-open count
        //     for the health formula (open older than 7 days).
        string requestsSql = $@"
            SELECT  r.ProjectId AS ProjectId,
                    COUNT(*) AS OpenCnt,
                    SUM(CASE WHEN datetime(r.CreatedAt) < datetime('now', '-7 days') THEN 1 ELSE 0 END) AS OldOpenCnt
            FROM    ProjectRequests r
            WHERE   r.ProjectId IN ({idsCsv})
              AND   r.Status NOT IN ('Resolved','Closed')
            GROUP   BY r.ProjectId";
        var requestsByProject = (await _db.GetRecordsAsync<ProjectRequestsRow>(requestsSql))?.ToList() ?? new();

        // 2e. Current milestone per project — first InProgress / Delayed /
        //     NotStarted by OrderIndex. Also flag: milestone date passed and
        //     milestone not completed (drives a 5-point health deduction).
        string milestonesSql = $@"
            SELECT  pm.ProjectId               AS ProjectId,
                    pm.Id                      AS ProjectMilestoneId,
                    aym.MilestoneTemplateId    AS MilestoneTemplateId,
                    mt.Title                   AS MilestoneTitle,
                    mt.OrderIndex              AS OrderIndex,
                    pm.Status                  AS Status,
                    COALESCE(mo.OverrideDueDate, aym.DueDate) AS DueDate
            FROM    ProjectMilestones pm
            JOIN    AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            JOIN    MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            LEFT JOIN Projects p ON p.Id = pm.ProjectId
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = p.TeamId AND mo.ProjectMilestoneId = pm.Id
            WHERE   pm.ProjectId IN ({idsCsv})
            ORDER   BY pm.ProjectId, mt.OrderIndex";
        var milestoneRows = (await _db.GetRecordsAsync<MilestoneRow>(milestonesSql))?.ToList() ?? new();

        // ── 3. Per-project assembly + health calculation ───────────────────
        var overdueMap     = overdueByProject.ToDictionary(r => r.ProjectId, r => r.Cnt);
        var missingSubsMap = missingSubsByProject.ToDictionary(r => r.ProjectId, r => r.Cnt);
        var subsMap        = subsByProject.ToDictionary(r => r.ProjectId, r => r);
        var reqMap         = requestsByProject.ToDictionary(r => r.ProjectId, r => r);
        var msByProject    = milestoneRows.GroupBy(m => m.ProjectId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<DashboardProjectRowDto>(projects.Count);
        foreach (var p in projects)
        {
            int overdue       = overdueMap.GetValueOrDefault(p.ProjectId,     0);
            int missing       = missingSubsMap.GetValueOrDefault(p.ProjectId, 0);
            var reqRow        = reqMap.GetValueOrDefault(p.ProjectId);
            int openRequests  = reqRow?.OpenCnt    ?? 0;
            int oldOpenReqs   = reqRow?.OldOpenCnt ?? 0;

            // Pick the project's "current" milestone — InProgress > Delayed > NotStarted (order).
            MilestoneRow? currentMs = null;
            if (msByProject.TryGetValue(p.ProjectId, out var msList))
            {
                currentMs = msList.FirstOrDefault(m => m.Status == "InProgress")
                         ?? msList.FirstOrDefault(m => m.Status == "Delayed")
                         ?? msList.FirstOrDefault(m => m.Status == "NotStarted");
            }
            bool currentMsOverdue = currentMs is not null
                                 && currentMs.Status != "Completed"
                                 && currentMs.DueDate is not null
                                 && currentMs.DueDate.Value.Date < DateTime.Today;

            // Health formula (per spec).
            int score = 100
                      - (overdue      * 10)
                      - (missing      * 8)
                      - (oldOpenReqs  * 5)
                      - (currentMsOverdue ? 5 : 0);
            if (score < 0)   score = 0;
            if (score > 100) score = 100;

            rows.Add(new DashboardProjectRowDto
            {
                ProjectId               = p.ProjectId,
                ProjectNumber           = p.ProjectNumber,
                ProjectTitle            = p.ProjectTitle,
                TeamName                = string.IsNullOrWhiteSpace(p.TeamName) ? null : p.TeamName,
                MentorNames             = string.IsNullOrWhiteSpace(p.MentorNames) ? null : p.MentorNames,
                CurrentMilestoneTitle   = currentMs?.MilestoneTitle,
                CurrentMilestoneDueDate = currentMs?.DueDate,
                OverdueTaskCount        = overdue,
                MissingSubmissionCount  = missing,
                OpenRequestCount        = openRequests,
                HealthScore             = score,
                HealthBucket            = HealthBuckets.FromScore(score),
            });
        }

        // ── 4. Charts data ─────────────────────────────────────────────────
        // Submission split: aggregated across all submission tasks in scope.
        int subsSubmitted = subsMap.Values.Sum(s => s.Submitted);
        int subsTotal     = subsMap.Values.Sum(s => s.Total);
        int subsNotYet    = subsTotal - subsSubmitted;
        if (subsNotYet < 0) subsNotYet = 0;

        // Overdue tasks grouped by milestone template.
        var overdueByMsSql = restrictToMentor
            ? @"
                SELECT  aym.MilestoneTemplateId AS ProjectMilestoneTemplateId,
                        mt.Title                AS MilestoneTitle,
                        COUNT(*)                AS OverdueTaskCount
                FROM    Tasks t
                JOIN    Projects          p   ON p.Id = t.ProjectId
                LEFT JOIN ProjectMilestones pm ON pm.Id = t.ProjectMilestoneId
                LEFT JOIN AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
                LEFT JOIN MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
                LEFT JOIN TeamTaskDueDateOverrides tto
                                ON tto.TeamId = p.TeamId AND tto.TaskId = t.Id
                LEFT JOIN TeamMilestoneDueDateOverrides mo
                                ON mo.TeamId = p.TeamId AND mo.ProjectMilestoneId = t.ProjectMilestoneId
                WHERE   p.AcademicYearId = @YearId
                  AND   COALESCE(p.AssignmentIsDraft, 0) = 0
                  AND   p.TeamId IS NOT NULL
                  AND   p.Status NOT IN ('Available', 'Unavailable')
                  AND   p.Id IN (SELECT ProjectId FROM ProjectMentors WHERE UserId = @UserId)
                  AND   t.IsMandatory = 1
                  AND   (t.Status IS NULL OR t.Status NOT IN ('Done','Completed','SubmittedToMentor'))
                  AND   t.ClosedAt IS NULL
                  AND   NOT EXISTS (SELECT 1 FROM TaskSubmissions s WHERE s.TaskId = t.Id)
                  AND   date(COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate)) < date('now')
                GROUP   BY aym.MilestoneTemplateId, mt.Title, mt.OrderIndex
                ORDER   BY mt.OrderIndex"
            : @"
                SELECT  aym.MilestoneTemplateId AS ProjectMilestoneTemplateId,
                        mt.Title                AS MilestoneTitle,
                        COUNT(*)                AS OverdueTaskCount
                FROM    Tasks t
                JOIN    Projects          p   ON p.Id = t.ProjectId
                LEFT JOIN ProjectMilestones pm ON pm.Id = t.ProjectMilestoneId
                LEFT JOIN AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
                LEFT JOIN MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
                LEFT JOIN TeamTaskDueDateOverrides tto
                                ON tto.TeamId = p.TeamId AND tto.TaskId = t.Id
                LEFT JOIN TeamMilestoneDueDateOverrides mo
                                ON mo.TeamId = p.TeamId AND mo.ProjectMilestoneId = t.ProjectMilestoneId
                WHERE   p.AcademicYearId    = @YearId
                  AND   COALESCE(p.AssignmentIsDraft, 0) = 0
                  AND   p.TeamId IS NOT NULL
                  AND   p.Status NOT IN ('Available', 'Unavailable')
                  AND   t.IsMandatory = 1
                  AND   (t.Status IS NULL OR t.Status NOT IN ('Done','Completed','SubmittedToMentor'))
                  AND   t.ClosedAt IS NULL
                  AND   NOT EXISTS (SELECT 1 FROM TaskSubmissions s WHERE s.TaskId = t.Id)
                  AND   date(COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate)) < date('now')
                GROUP   BY aym.MilestoneTemplateId, mt.Title, mt.OrderIndex
                ORDER   BY mt.OrderIndex";

        var overdueByMs = (await _db.GetRecordsAsync<MilestoneOverdueBarDto>(
            overdueByMsSql, new { YearId = currentYearId, UserId = authUserId }))?.ToList() ?? new();

        // ── 5. Summary cards ───────────────────────────────────────────────
        var summary = new DashboardSummaryDto
        {
            TotalProjects        = projects.Count,
            ActiveProjects       = projects.Count(p => p.ProjectStatus != "Completed" && p.ProjectStatus != "Archived"),
            SubmissionsCompleted = subsSubmitted,
            SubmissionsMissing   = subsNotYet,
            OverdueTasks         = rows.Sum(r => r.OverdueTaskCount),
            OpenRequests         = rows.Sum(r => r.OpenRequestCount),
        };

        var charts = new DashboardChartsDto
        {
            HealthHealthy        = rows.Count(r => r.HealthBucket == HealthBuckets.Healthy),
            HealthAttention      = rows.Count(r => r.HealthBucket == HealthBuckets.Attention),
            HealthAtRisk         = rows.Count(r => r.HealthBucket == HealthBuckets.AtRisk),
            SubmissionsSubmitted = subsSubmitted,
            SubmissionsNotYet    = subsNotYet,
            OverdueByMilestone   = overdueByMs,
        };

        return Ok(new DashboardOverviewDto
        {
            Scope    = effectiveScope,
            Summary  = summary,
            Charts   = charts,
            Projects = rows,
        });
    }

    // ── Private row types ────────────────────────────────────────────────────

    private sealed class ProjectBaseRow
    {
        public int      ProjectId       { get; set; }
        public int      ProjectNumber   { get; set; }
        public string   ProjectTitle    { get; set; } = "";
        public string   ProjectStatus   { get; set; } = "";
        public int?     TeamId          { get; set; }
        public string?  TeamName        { get; set; }
        public int      AcademicYearId  { get; set; }
        public string?  MentorNames     { get; set; }
    }

    private sealed class ProjectIntRow
    {
        public int ProjectId { get; set; }
        public int Cnt       { get; set; }
    }

    private sealed class ProjectSubsRow
    {
        public int ProjectId { get; set; }
        public int Submitted { get; set; }
        public int Total     { get; set; }
    }

    private sealed class ProjectRequestsRow
    {
        public int ProjectId  { get; set; }
        public int OpenCnt    { get; set; }
        public int OldOpenCnt { get; set; }
    }

    private sealed class MilestoneRow
    {
        public int       ProjectId             { get; set; }
        public int       ProjectMilestoneId    { get; set; }
        public int       MilestoneTemplateId   { get; set; }
        public string    MilestoneTitle        { get; set; } = "";
        public int       OrderIndex            { get; set; }
        public string    Status                { get; set; } = "";
        public DateTime? DueDate               { get; set; }
    }
}
