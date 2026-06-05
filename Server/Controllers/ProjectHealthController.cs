using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  ProjectHealthController — /api/project-health
//
//  Internal traffic-light view of project progress vs. the course schedule.
//  Available to Admin / Staff (full visibility) and Mentor (own projects).
//  Students MUST never reach this controller — it carries internal evaluation
//  data they are not entitled to see.
//
//  Health is derived per project as the maximum delay (in calendar days)
//  across the project's milestones. The effective due date follows the
//  standard override priority:
//      TeamMilestoneDueDateOverrides → AcademicYearMilestones.DueDate
//
//      delay == 0          → Green
//      1  <= delay <= 14   → Orange
//      delay > 14          → Red
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/project-health")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Mentor)]
public class ProjectHealthController : ControllerBase
{
    private readonly DbRepository _db;
    public ProjectHealthController(DbRepository db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(int authUserId)
    {
        // Mentors are scoped to their own ProjectMentors rows regardless of
        // the URL. Admin/Staff see every active project. The role check on
        // the action is the safety boundary — the SQL just narrows results.
        bool restrictToMentor =
            User.IsInRole(Roles.Mentor)
            && !User.IsInRole(Roles.Admin)
            && !User.IsInRole(Roles.Staff);

        // Resolve current academic year (mirrors the dashboard rule).
        const string yearSql = @"
            SELECT Id FROM AcademicYears WHERE IsCurrent = 1
            UNION ALL
            SELECT Id FROM AcademicYears ORDER BY Id DESC
            LIMIT 1";
        var currentYearId = (await _db.GetRecordsAsync<int>(yearSql))?.FirstOrDefault() ?? 0;

        // ── 1. Base project list — scope-aware definition ───────────────────
        //
        //  LECTURER scope: same "active assigned" definition as the lecturer
        //    dashboard — drops catalog-state rows so the page isn't flooded
        //    with hundreds of unassigned proposals.
        //
        //  MENTOR scope: ProjectMentors row is the sole project gate, identical
        //    to /api/mentor/projects ("הפרויקטים שלי") and to the dashboard's
        //    mentor scope after the 2026-06-03 fix. No Status/TeamId/Draft
        //    filters — those silently hid legitimate assignments whose
        //    Projects.Status hadn't been flipped to 'InProgress' yet (project
        //    #4 regression). The cycle filter is preserved so historical
        //    assignments don't bleed into a "current state" view.
        const string baseProjectsSelect = @"
            SELECT  p.Id           AS ProjectId,
                    p.ProjectNumber,
                    p.Title        AS ProjectTitle,
                    p.TeamId,
                    t.TeamName,
                    (SELECT GROUP_CONCAT(su.FirstName || ' ' || su.LastName, ', ')
                     FROM   TeamMembers tmem
                     JOIN   users       su ON su.Id = tmem.UserId
                     WHERE  tmem.TeamId  = p.TeamId
                       AND  tmem.IsActive = 1)               AS StudentNames,
                    (SELECT GROUP_CONCAT(mu.FirstName || ' ' || mu.LastName, ', ')
                     FROM   ProjectMentors pmm
                     JOIN   users          mu ON mu.Id = pmm.UserId
                     WHERE  pmm.ProjectId = p.Id)            AS MentorName
            FROM    Projects p
            LEFT JOIN Teams  t ON t.Id = p.TeamId
        ";

        const string projectsSqlLecturer = @"
            WHERE   p.AcademicYearId   = @YearId
              AND   COALESCE(p.AssignmentIsDraft, 0) = 0
              AND   p.TeamId IS NOT NULL
              AND   p.Status NOT IN ('Available', 'Unavailable')";

        const string projectsSqlMentor = @"
            WHERE   p.AcademicYearId   = @YearId
              AND   p.Id IN (SELECT ProjectId FROM ProjectMentors WHERE UserId = @UserId)";

        string projectsSql = baseProjectsSelect
                           + (restrictToMentor ? projectsSqlMentor : projectsSqlLecturer)
                           + " ORDER BY p.ProjectNumber";

        var projects = (await _db.GetRecordsAsync<ProjectBaseRow>(
            projectsSql, new { YearId = currentYearId, UserId = authUserId }))?.ToList() ?? new();

        if (projects.Count == 0)
            return Ok(Enumerable.Empty<ProjectHealthRowDto>());

        // ── 2. Milestones for those projects (one round-trip) ──────────────
        // Effective due date uses TeamMilestoneDueDateOverrides when present,
        // matching the override-aware reading pattern used everywhere else.
        var idsCsv = string.Join(",", projects.Select(p => p.ProjectId));
        string milestonesSql = $@"
            SELECT  pm.Id                       AS ProjectMilestoneId,
                    pm.ProjectId,
                    pm.Status,
                    pm.CompletedAt,
                    mt.Title                    AS MilestoneTitle,
                    mt.OrderIndex               AS OrderIndex,
                    COALESCE(mo.OverrideDueDate, aym.DueDate) AS DueDate
            FROM    ProjectMilestones pm
            JOIN    AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            JOIN    MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            LEFT JOIN Projects p ON p.Id = pm.ProjectId
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = p.TeamId AND mo.ProjectMilestoneId = pm.Id
            WHERE   pm.ProjectId IN ({idsCsv})
            ORDER   BY pm.ProjectId, mt.OrderIndex";
        var milestones = (await _db.GetRecordsAsync<MilestoneRow>(milestonesSql))?.ToList() ?? new();
        var msByProject = milestones.GroupBy(m => m.ProjectId)
                                    .ToDictionary(g => g.Key, g => g.ToList());

        // ── 3. Assemble per-project rows + traffic light ───────────────────
        DateTime today = DateTime.Today;
        var rows = new List<ProjectHealthRowDto>(projects.Count);
        foreach (var p in projects)
        {
            msByProject.TryGetValue(p.ProjectId, out var msList);
            msList ??= new List<MilestoneRow>();

            // Per-milestone delay: completed-late uses CompletedAt - DueDate,
            // open-and-overdue uses today - DueDate. Anything else is 0.
            int    maxDelay   = 0;
            MilestoneRow? worstDelayed = null;
            foreach (var m in msList)
            {
                int delay = ComputeMilestoneDelay(m, today);
                if (delay > maxDelay)
                {
                    maxDelay     = delay;
                    worstDelayed = m;
                }
            }

            string status = maxDelay switch
            {
                0                       => ProjectHealthStatuses.Green,
                <= 14                   => ProjectHealthStatuses.Orange,
                _                       => ProjectHealthStatuses.Red,
            };

            // Pick the milestone shown in the row:
            //   - delayed → the most-delayed one (drives the status display)
            //   - on track → first non-Completed milestone (current/next-up)
            //   - all done → the last completed milestone
            MilestoneRow? relevant = worstDelayed
                ?? msList.FirstOrDefault(m => m.Status != "Completed")
                ?? msList.LastOrDefault();

            rows.Add(new ProjectHealthRowDto
            {
                ProjectId               = p.ProjectId,
                ProjectNumber           = p.ProjectNumber,
                ProjectTitle            = p.ProjectTitle,
                TeamName                = p.TeamName,
                StudentNames            = p.StudentNames,
                MentorName              = p.MentorName,
                RelevantMilestoneTitle  = relevant?.MilestoneTitle,
                PlannedDueDate          = relevant?.DueDate,
                ActualCompletedAt       = relevant?.CompletedAt,
                Status                  = status,
                DelayDays               = maxDelay,
            });
        }

        // Most concerning first so a quick scan surfaces problems immediately.
        rows = rows
            .OrderBy(r => r.Status switch
            {
                ProjectHealthStatuses.Red    => 0,
                ProjectHealthStatuses.Orange => 1,
                _                            => 2,
            })
            .ThenByDescending(r => r.DelayDays)
            .ThenBy(r => r.ProjectNumber)
            .ToList();

        return Ok(rows);
    }

    private static int ComputeMilestoneDelay(MilestoneRow m, DateTime today)
    {
        if (m.DueDate is null) return 0;
        DateTime due = m.DueDate.Value.Date;

        if (m.Status == "Completed")
        {
            if (m.CompletedAt is null) return 0;
            int days = (m.CompletedAt.Value.Date - due).Days;
            return days > 0 ? days : 0;
        }

        // Open milestone — delay is measured up to today.
        int openDays = (today - due).Days;
        return openDays > 0 ? openDays : 0;
    }

    private sealed class ProjectBaseRow
    {
        public int     ProjectId     { get; set; }
        public int     ProjectNumber { get; set; }
        public string  ProjectTitle  { get; set; } = "";
        public int?    TeamId        { get; set; }
        public string? TeamName      { get; set; }
        public string? StudentNames  { get; set; }
        public string? MentorName    { get; set; }
    }

    private sealed class MilestoneRow
    {
        public int       ProjectMilestoneId { get; set; }
        public int       ProjectId          { get; set; }
        public string    Status             { get; set; } = "";
        public DateTime? CompletedAt        { get; set; }
        public string    MilestoneTitle     { get; set; } = "";
        public int       OrderIndex         { get; set; }
        public DateTime? DueDate            { get; set; }
    }
}
