using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  ProjectOverviewController — /api/projects/{projectId}/overview
//
//  Single-page payload for the lecturer/mentor project-detail mini dashboard.
//  Effective due dates use the canonical override chain:
//    TeamTaskDueDateOverrides → TeamMilestoneDueDateOverrides → Tasks.DueDate
//  Globals are never compared raw.
//
//  Active-project guard:
//    p.AcademicYearId = current
//    p.TeamId IS NOT NULL
//    COALESCE(p.AssignmentIsDraft, 0) = 0
//    p.Status NOT IN ('Available','Unavailable')   — exclude catalog states
//
//  Scope:
//    Admin / Staff → any active project
//    Mentor        → only projects in ProjectMentors for this user
//    Student       → blocked by [Authorize(Roles=…)] on the action
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/projects")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
public class ProjectOverviewController : ControllerBase
{
    private readonly DbRepository _db;
    public ProjectOverviewController(DbRepository db) => _db = db;

    [HttpGet("{projectId:int}/overview")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Mentor)]
    public async Task<IActionResult> GetOverview(int projectId, int authUserId)
    {
        // ── 1. Resolve project + scope-check ───────────────────────────────
        const string headerSql = @"
            SELECT  p.Id            AS ProjectId,
                    p.ProjectNumber,
                    p.Title         AS ProjectTitle,
                    p.TeamId,
                    t.TeamName,
                    pt.Name         AS ProjectType,
                    p.HealthStatus,
                    p.Status        AS ProjectStatus,
                    p.AcademicYearId,
                    COALESCE(p.AssignmentIsDraft, 0) AS AssignmentIsDraft,
                    (SELECT GROUP_CONCAT(u.FirstName || ' ' || u.LastName, ', ')
                     FROM   ProjectMentors pmm
                     JOIN   users          u ON u.Id = pmm.UserId
                     WHERE  pmm.ProjectId = p.Id) AS MentorNames
            FROM    Projects     p
            LEFT JOIN Teams       t  ON t.Id = p.TeamId
            JOIN    ProjectTypes pt  ON pt.Id = p.ProjectTypeId
            WHERE   p.Id = @ProjectId
            LIMIT   1";

        var head = (await _db.GetRecordsAsync<ProjectHeaderRow>(
            headerSql, new { ProjectId = projectId }))?.FirstOrDefault();

        if (head is null) return NotFound("פרויקט לא נמצא");

        // Active-project guard
        if (head.TeamId is null
            || head.AssignmentIsDraft != 0
            || head.ProjectStatus is "Available" or "Unavailable")
            return NotFound("פרויקט לא נמצא או אינו פעיל");

        // Mentor scope check
        if (User.IsInRole(Roles.Mentor)
            && !User.IsInRole(Roles.Admin)
            && !User.IsInRole(Roles.Staff))
        {
            var ok = (await _db.GetRecordsAsync<int>(
                "SELECT 1 FROM ProjectMentors WHERE ProjectId = @P AND UserId = @U LIMIT 1",
                new { P = projectId, U = authUserId }))?.FirstOrDefault();
            if (ok != 1) return NotFound("פרויקט לא נמצא");
        }

        int teamId = head.TeamId!.Value;

        // ── 2. Milestones (with effective due dates per team) ──────────────
        const string milestonesSql = @"
            SELECT  pm.Id                                 AS ProjectMilestoneId,
                    mt.Title,
                    mt.OrderIndex,
                    pm.Status,
                    COALESCE(mo.OverrideDueDate, aym.DueDate) AS DueDate
            FROM    ProjectMilestones       pm
            JOIN    AcademicYearMilestones  aym ON aym.Id = pm.AcademicYearMilestoneId
            JOIN    MilestoneTemplates      mt  ON mt.Id  = aym.MilestoneTemplateId
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = @TeamId AND mo.ProjectMilestoneId = pm.Id
            WHERE   pm.ProjectId = @ProjectId
            ORDER   BY mt.OrderIndex";
        var milestones = (await _db.GetRecordsAsync<MilestoneRow>(
            milestonesSql, new { ProjectId = projectId, TeamId = teamId }))?.ToList() ?? new();

        // ── 3. Tasks (effective due date + overdue + has-submission) ───────
        const string tasksSql = @"
            SELECT  t.Id                AS TaskId,
                    t.Title,
                    t.ProjectMilestoneId AS ProjectMilestoneId,
                    COALESCE(mt.Title, '') AS MilestoneTitle,
                    t.Status,
                    t.IsSubmission,
                    t.ClosedAt,
                    COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate) AS DueDate,
                    EXISTS (SELECT 1 FROM TaskSubmissions s WHERE s.TaskId = t.Id) AS HasSubmission
            FROM    Tasks                    t
            LEFT JOIN ProjectMilestones      pm  ON pm.Id  = t.ProjectMilestoneId
            LEFT JOIN AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            LEFT JOIN MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            LEFT JOIN TeamTaskDueDateOverrides tto
                            ON tto.TeamId = @TeamId AND tto.TaskId = t.Id
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = @TeamId AND mo.ProjectMilestoneId = t.ProjectMilestoneId
            WHERE   t.ProjectId = @ProjectId
            ORDER   BY mt.OrderIndex, t.DueDate, t.Id";
        var taskRows = (await _db.GetRecordsAsync<TaskRow>(
            tasksSql, new { ProjectId = projectId, TeamId = teamId }))?.ToList() ?? new();

        // ── 4. Open requests ───────────────────────────────────────────────
        const string requestsSql = @"
            SELECT  r.Id                                   AS RequestId,
                    r.RequestType,
                    r.Title,
                    r.Status,
                    r.CreatedAt,
                    u.FirstName || ' ' || u.LastName       AS CreatedByName
            FROM    ProjectRequests r
            JOIN    users           u ON u.Id = r.CreatedByUserId
            WHERE   r.ProjectId = @ProjectId
              AND   r.Status NOT IN ('Resolved', 'Closed')
            ORDER   BY r.CreatedAt DESC
            LIMIT   50";
        var openRequests = (await _db.GetRecordsAsync<ProjectOverviewRequestDto>(
            requestsSql, new { ProjectId = projectId }))?.ToList() ?? new();

        // ── 5. Recent submissions (latest 10) ──────────────────────────────
        const string submissionsSql = @"
            SELECT  s.Id                              AS SubmissionId,
                    s.TaskId,
                    t.Title                           AS TaskTitle,
                    u.FirstName || ' ' || u.LastName  AS SubmittedByName,
                    s.SubmittedAt,
                    s.Status,
                    s.MentorStatus                    AS LatestMentorStatus,
                    CASE WHEN COALESCE(s.IsFeedbackPublished, 0) = 1
                         THEN s.ReviewerFeedback
                         ELSE NULL
                    END                               AS LatestFeedback
            FROM    TaskSubmissions s
            JOIN    Tasks  t ON t.Id = s.TaskId
            JOIN    users  u ON u.Id = s.SubmittedByUserId
            WHERE   t.ProjectId = @ProjectId
            ORDER   BY s.SubmittedAt DESC
            LIMIT   10";
        var recent = (await _db.GetRecordsAsync<ProjectOverviewSubmissionDto>(
            submissionsSql, new { ProjectId = projectId }))?.ToList() ?? new();

        // ── 6. Per-task post-processing (status, overdue) ──────────────────
        var taskDtos = taskRows.Select(r =>
        {
            bool open       = r.Status is null
                              || (r.Status != "Done" && r.Status != "Completed" && r.Status != "SubmittedToMentor");
            bool isOverdue  = open
                              && r.ClosedAt is null
                              && r.DueDate is not null
                              && r.DueDate.Value.Date < DateTime.Today
                              && r.HasSubmission == 0;
            return new TaskWithMs
            {
                ProjectMilestoneId = r.ProjectMilestoneId,
                Dto = new ProjectOverviewTaskDto
                {
                    TaskId         = r.TaskId,
                    Title          = r.Title,
                    MilestoneTitle = r.MilestoneTitle,
                    Status         = r.Status ?? "",
                    IsSubmission   = r.IsSubmission == 1,
                    DueDate        = r.DueDate,
                    IsOverdue      = isOverdue,
                    HasSubmission  = r.HasSubmission == 1,
                },
            };
        }).ToList();

        var tasksByMilestone = taskDtos.Where(t => t.ProjectMilestoneId is not null)
                                       .GroupBy(t => t.ProjectMilestoneId!.Value)
                                       .ToDictionary(g => g.Key, g => g.Select(x => x.Dto).ToList());

        // ── 7. Milestones DTOs with derived counts ─────────────────────────
        var milestoneDtos = milestones.Select(m =>
        {
            var tasks = tasksByMilestone.GetValueOrDefault(m.ProjectMilestoneId) ?? new();
            int completed = tasks.Count(t => t.Status is "Done" or "Completed" or "SubmittedToMentor"
                                          || t.HasSubmission);
            int missing   = tasks.Count(t => t.IsSubmission && !t.HasSubmission && t.IsOverdue);
            bool overdue  = m.Status != "Completed"
                            && m.DueDate is not null
                            && m.DueDate.Value.Date < DateTime.Today;
            return new ProjectOverviewMilestoneDto
            {
                ProjectMilestoneId      = m.ProjectMilestoneId,
                Title                   = m.Title,
                OrderIndex              = m.OrderIndex,
                Status                  = m.Status,
                DueDate                 = m.DueDate,
                TasksCompleted          = completed,
                TasksTotal              = tasks.Count,
                MissingSubmissionCount  = missing,
                IsOverdue               = overdue,
                Tasks                   = tasks,
            };
        }).ToList();

        // ── 8. Summary aggregation ─────────────────────────────────────────
        int totalTasks     = taskDtos.Count;
        int completedTasks = taskDtos.Count(t => t.Dto.Status is "Done" or "Completed" or "SubmittedToMentor"
                                              || t.Dto.HasSubmission);
        int overdueTasks   = taskDtos.Count(t => t.Dto.IsOverdue);
        int missingSubs    = taskDtos.Count(t => t.Dto.IsSubmission && !t.Dto.HasSubmission && t.Dto.IsOverdue);
        int msTotal        = milestoneDtos.Count;
        int msCompleted    = milestoneDtos.Count(m => m.Status == "Completed");
        int progressPct    = totalTasks == 0 ? 0 : (int)Math.Round(completedTasks * 100.0 / totalTasks);

        var summary = new ProjectOverviewSummaryDto
        {
            OverallProgressPercent = progressPct,
            MilestonesCompleted    = msCompleted,
            MilestonesTotal        = msTotal,
            TasksCompleted         = completedTasks,
            TasksTotal             = totalTasks,
            MissingSubmissions     = missingSubs,
            OpenRequestCount       = openRequests.Count,
            OverdueTaskCount       = overdueTasks,
        };

        var header = new ProjectOverviewHeaderDto
        {
            ProjectId     = head.ProjectId,
            ProjectNumber = head.ProjectNumber,
            ProjectTitle  = head.ProjectTitle,
            TeamName      = string.IsNullOrWhiteSpace(head.TeamName) ? null : head.TeamName,
            ProjectType   = head.ProjectType,
            MentorNames   = string.IsNullOrWhiteSpace(head.MentorNames) ? null : head.MentorNames,
            HealthStatus  = head.HealthStatus,
        };

        return Ok(new ProjectOverviewDto
        {
            Header            = header,
            Summary           = summary,
            Milestones        = milestoneDtos,
            Tasks             = taskDtos.Select(x => x.Dto).ToList(),
            OpenRequests      = openRequests,
            RecentSubmissions = recent,
        });
    }

    // ── Private row types ────────────────────────────────────────────────────

    private sealed class ProjectHeaderRow
    {
        public int      ProjectId         { get; set; }
        public int      ProjectNumber     { get; set; }
        public string   ProjectTitle      { get; set; } = "";
        public int?     TeamId            { get; set; }
        public string?  TeamName          { get; set; }
        public string   ProjectType       { get; set; } = "";
        public string?  HealthStatus      { get; set; }
        public string   ProjectStatus     { get; set; } = "";
        public int      AcademicYearId    { get; set; }
        public int      AssignmentIsDraft { get; set; }
        public string?  MentorNames       { get; set; }
    }

    private sealed class MilestoneRow
    {
        public int       ProjectMilestoneId { get; set; }
        public string    Title              { get; set; } = "";
        public int       OrderIndex         { get; set; }
        public string    Status             { get; set; } = "";
        public DateTime? DueDate            { get; set; }
    }

    private sealed class TaskRow
    {
        public int       TaskId             { get; set; }
        public string    Title              { get; set; } = "";
        public int?      ProjectMilestoneId { get; set; }
        public string    MilestoneTitle     { get; set; } = "";
        public string?   Status             { get; set; }
        public int       IsSubmission       { get; set; }
        public DateTime? ClosedAt           { get; set; }
        public DateTime? DueDate            { get; set; }
        public int       HasSubmission      { get; set; }
    }

    private sealed class TaskWithMs
    {
        public int? ProjectMilestoneId { get; set; }
        public ProjectOverviewTaskDto Dto { get; set; } = new();
    }
}
