using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// Access is open to all authenticated roles (Student, Mentor, Admin, Staff).
// Role restriction is intentionally omitted: the SQL query already scopes
// every result to the requesting user's own data via authUserId.
// Protection is enforced by: JWT [Authorize] + AuthCheck token-blacklist filter.
[Route("api/[controller]")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly DbRepository _db;

    public ProjectsController(DbRepository db) => _db = db;

    // ── GET /api/projects/my-dashboard ───────────────────────────────────────
    // Returns the complete dashboard payload for the authenticated student.
    // All DB joins are resolved here; the client receives a single clean DTO.
    // authUserId is injected automatically by the AuthCheck action filter.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-dashboard")]
    public async Task<IActionResult> GetMyDashboard(int authUserId)
    {
        // ── 1. Resolve user → team → project ─────────────────────────────────
        // Draft assignments (AssignmentIsDraft = 1) remain hidden from students
        // until the lecturer publishes them — the row is treated as "no project".
        const string projectSql = @"
            SELECT  p.Id,
                    p.ProjectNumber,
                    p.Title,
                    p.Description,
                    p.Status,
                    p.HealthStatus,
                    pt.Name  AS ProjectType,
                    t.Id     AS TeamId
            FROM    Projects     p
            JOIN    Teams        t   ON p.TeamId       = t.Id
            JOIN    TeamMembers  tm  ON t.Id            = tm.TeamId
            JOIN    ProjectTypes pt  ON p.ProjectTypeId = pt.Id
            WHERE   tm.UserId  = @UserId
              AND   tm.IsActive = 1
              AND   COALESCE(p.AssignmentIsDraft, 0) = 0
            LIMIT 1";

        var projectRow = (await _db.GetRecordsAsync<ProjectRow>(
                projectSql, new { UserId = authUserId }))
            .FirstOrDefault();

        // User is not yet assigned to a project — return minimal dashboard with HasTeam flag.
        if (projectRow is null)
        {
            var teamCount = (await _db.GetRecordsAsync<int>(
                "SELECT COUNT(1) FROM TeamMembers WHERE UserId = @UserId AND IsActive = 1",
                new { UserId = authUserId })).FirstOrDefault();
            return Ok(new DashboardDto { HasTeam = teamCount > 0 });
        }

        int projectId = projectRow.Id;
        int teamId    = projectRow.TeamId;

        // ── 2. Team members ───────────────────────────────────────────────────
        const string membersSql = @"
            SELECT  u.Id                                  AS UserId,
                    u.FirstName || ' ' || u.LastName      AS FullName,
                    tm.MemberRole
            FROM    TeamMembers tm
            JOIN    Users       u  ON tm.UserId = u.Id
            WHERE   tm.TeamId  = @TeamId
              AND   tm.IsActive = 1";

        var members = await _db.GetRecordsAsync<TeamMemberDto>(
            membersSql, new { TeamId = teamId });

        // ── 3. Mentors ────────────────────────────────────────────────────────
        const string mentorsSql = @"
            SELECT  u.Id                             AS UserId,
                    u.FirstName || ' ' || u.LastName AS FullName,
                    u.Email,
                    u.Phone
            FROM    ProjectMentors pm
            JOIN    Users          u  ON pm.UserId = u.Id
            WHERE   pm.ProjectId = @ProjectId";

        var mentors = await _db.GetRecordsAsync<ContactDto>(
            mentorsSql, new { ProjectId = projectId });

        // ── 4. Milestones (3-table flatten) ───────────────────────────────────
        // Merges: MilestoneTemplates → AcademicYearMilestones → ProjectMilestones
        const string milestonesSql = @"
            SELECT  pm.Id          AS ProjectMilestoneId,
                    mt.Title,
                    mt.OrderIndex,
                    pm.Status,
                    aym.DueDate,
                    pm.CompletedAt
            FROM    ProjectMilestones       pm
            JOIN    AcademicYearMilestones  aym ON pm.AcademicYearMilestoneId = aym.Id
            JOIN    MilestoneTemplates      mt  ON aym.MilestoneTemplateId    = mt.Id
            WHERE   pm.ProjectId = @ProjectId
            ORDER   BY mt.OrderIndex";

        var milestoneRows = await _db.GetRecordsAsync<MilestoneRow>(
            milestonesSql, new { ProjectId = projectId });

        // ── 5. Tasks (all, grouped into milestones below) ─────────────────────
        const string tasksSql = @"
            SELECT  t.Id,
                    t.Title,
                    t.Status,
                    t.DueDate,
                    t.ProjectMilestoneId,
                    t.IsSubmission,
                    COALESCE(u.FirstName || ' ' || u.LastName, '') AS AssignedToName,
                    (SELECT s.Status
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1) AS LatestSubmissionStatus,
                    (SELECT s.MentorStatus
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1) AS LatestMentorStatus,
                    (SELECT s.SubmittedAt
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1) AS LatestSubmittedAt
            FROM    Tasks t
            LEFT JOIN Users u ON t.AssignedToUserId = u.Id
            WHERE   t.ProjectId = @ProjectId
            ORDER   BY t.DueDate";

        var taskRows = await _db.GetRecordsAsync<TaskRow>(
            tasksSql, new { ProjectId = projectId });

        // ── 6. Open requests ──────────────────────────────────────────────────
        // Reads from ProjectRequests (unified requests module).
        // Maps CreatedAt → OpenedAt to satisfy OpenRequestDto column mapping.
        const string requestsSql = @"
            SELECT  r.Id,
                    r.Title,
                    r.RequestType,
                    r.Status,
                    r.CreatedAt AS OpenedAt
            FROM    ProjectRequests r
            WHERE   r.ProjectId = @ProjectId
              AND   r.Status   != 'Closed'
            ORDER   BY r.CreatedAt DESC";

        var requests = await _db.GetRecordsAsync<OpenRequestDto>(
            requestsSql, new { ProjectId = projectId });

        // ── Assemble milestones with nested tasks ─────────────────────────────
        var tasksByMilestone = taskRows.ToLookup(t => t.ProjectMilestoneId);

        var milestones = milestoneRows.Select(m => new MilestoneSummaryDto
        {
            ProjectMilestoneId = m.ProjectMilestoneId,
            Title              = m.Title,
            OrderIndex         = m.OrderIndex,
            Status             = NormalizeMilestoneStatus(m.Status),
            DueDate            = m.DueDate,
            CompletedAt        = m.CompletedAt,
            Tasks              = tasksByMilestone[m.ProjectMilestoneId]
                .Select(t => new TaskSummaryDto
                {
                    Id                     = t.Id,
                    Title                  = t.Title,
                    Status                 = NormalizeTaskStatus(t.Status, t.LatestMentorStatus, t.LatestSubmissionStatus),
                    DueDate                = t.DueDate,
                    AssignedToName         = t.AssignedToName,
                    LatestSubmissionStatus = t.LatestSubmissionStatus,
                    LatestMentorStatus     = t.LatestMentorStatus,
                    LatestSubmittedAt      = t.LatestSubmittedAt,
                })
                .ToList(),
        }).ToList();

        // ── Derive next deadline ──────────────────────────────────────────────
        // Prefer the nearest incomplete submission task; fall back to nearest milestone.
        var nearestSubmissionTask = taskRows?
            .Where(t => t.IsSubmission && t.Status != "Done" && t.DueDate.HasValue)
            .OrderBy(t => t.DueDate)
            .FirstOrDefault();

        UpcomingDeadlineDto? nextDeadline;
        if (nearestSubmissionTask is not null)
        {
            nextDeadline = new UpcomingDeadlineDto
            {
                TaskId             = nearestSubmissionTask.Id,
                Title              = nearestSubmissionTask.Title,
                DueDate            = nearestSubmissionTask.DueDate!.Value,
                LatestMentorStatus = nearestSubmissionTask.LatestMentorStatus,
            };
        }
        else
        {
            nextDeadline = milestones
                .Where(m => !IsMilestoneCompleted(m.Status) && m.DueDate.HasValue)
                .OrderBy(m => m.DueDate)
                .Select(m => new UpcomingDeadlineDto { Title = m.Title, DueDate = m.DueDate!.Value })
                .FirstOrDefault();
        }

        // ── Build and return the dashboard DTO ────────────────────────────────
        var dashboard = new DashboardDto
        {
            HasTeam = true,
            Project = new ProjectInfoDto
            {
                Id            = projectRow.Id,
                ProjectNumber = projectRow.ProjectNumber,
                Title         = projectRow.Title,
                Description   = projectRow.Description ?? "",
                Status        = projectRow.Status,
                HealthStatus  = projectRow.HealthStatus,
                ProjectType   = projectRow.ProjectType,
            },
            TeamMembers  = members.ToList(),
            Mentors      = mentors.ToList(),
            Milestones   = milestones,
            NextDeadline = nextDeadline,
            OpenRequests = requests.ToList(),
        };

        return Ok(dashboard);
    }

    // ── GET /api/projects/my-context ─────────────────────────────────────────
    // Returns project identity + sidebar widget data for the authenticated user.
    // Three small queries: project row → milestones → tasks.
    // Result is cached client-side for the session — one API call per tab.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-context")]
    public async Task<IActionResult> GetMyContext(int authUserId)
    {
        // ── 1. Resolve user → project + team quick-info ──────────────────────
        // Single query joins identity + team-context fields. Names and emails
        // are paired as "FullName<#>Email" records joined with '||' so they
        // stay aligned regardless of GROUP_CONCAT ordering. Two tiny splits
        // on the C# side recover the parallel lists. No N+1.
        const string projectSql = @"
            SELECT  p.Id                        AS ProjectId,
                    p.ProjectNumber,
                    p.Title                     AS ProjectTitle,
                    t.TeamName                  AS TeamName,
                    pt.Name                     AS TrackName,
                    (SELECT GROUP_CONCAT(
                                su.FirstName || ' ' || su.LastName
                                || '<#>' || COALESCE(su.Email, ''),
                                '||')
                     FROM   TeamMembers stm
                     JOIN   users       su ON su.Id = stm.UserId
                     WHERE  stm.TeamId   = t.Id
                       AND  stm.IsActive = 1)   AS StudentDetailsCsv,
                    (SELECT GROUP_CONCAT(
                                mu.FirstName || ' ' || mu.LastName
                                || '<#>' || COALESCE(mu.Email, ''),
                                '||')
                     FROM   ProjectMentors pm
                     JOIN   users          mu ON mu.Id = pm.UserId
                     WHERE  pm.ProjectId = p.Id) AS MentorDetailsCsv
            FROM    Projects     p
            JOIN    Teams        t   ON p.TeamId  = t.Id
            LEFT JOIN ProjectTypes pt ON pt.Id = p.ProjectTypeId
            JOIN    TeamMembers  tm  ON t.Id      = tm.TeamId
            WHERE   tm.UserId  = @UserId
              AND   tm.IsActive = 1
            LIMIT 1";

        var projectRow = (await _db.GetRecordsAsync<ContextProjectRow>(
                projectSql, new { UserId = authUserId }))
            .FirstOrDefault();

        if (projectRow is null)
            return Ok((ProjectContextDto?)null);

        int projectId = projectRow.ProjectId;
        var (studentNames, studentEmails) = SplitNameEmailPairs(projectRow.StudentDetailsCsv);
        var (mentorNames,  mentorEmails)  = SplitNameEmailPairs(projectRow.MentorDetailsCsv);

        // ── 2. Milestones (for current-milestone + progress) ──────────────────
        const string milestonesSql = @"
            SELECT  mt.Title,
                    pm.Status,
                    aym.DueDate
            FROM    ProjectMilestones       pm
            JOIN    AcademicYearMilestones  aym ON pm.AcademicYearMilestoneId = aym.Id
            JOIN    MilestoneTemplates      mt  ON aym.MilestoneTemplateId    = mt.Id
            WHERE   pm.ProjectId = @ProjectId
            ORDER   BY mt.OrderIndex";

        var milestones = (await _db.GetRecordsAsync<ContextMilestoneRow>(
                milestonesSql, new { ProjectId = projectId }))
            .ToList();

        // ── 3. Tasks (for task counts + next open task) ───────────────────────
        const string tasksSql = @"
            SELECT  t.Status,
                    t.Title,
                    t.DueDate
            FROM    Tasks t
            WHERE   t.ProjectId = @ProjectId
            ORDER   BY t.DueDate NULLS LAST, t.Id";

        var tasks = (await _db.GetRecordsAsync<ContextTaskRow>(
                tasksSql, new { ProjectId = projectId }))
            .ToList();

        // ── Derive current milestone ──────────────────────────────────────────
        // Priority: InProgress → Delayed → NotStarted (first by OrderIndex)
        var currentMs = milestones.FirstOrDefault(m => m.Status == "InProgress")
                     ?? milestones.FirstOrDefault(m => m.Status == "Delayed")
                     ?? milestones.FirstOrDefault(m => m.Status == "NotStarted");

        // ── Derive next task ──────────────────────────────────────────────────
        var nextTask = tasks.FirstOrDefault(t => t.Status != "Done" && t.DueDate.HasValue)
                    ?? tasks.FirstOrDefault(t => t.Status != "Done");

        return Ok(new ProjectContextDto
        {
            ProjectId                = projectRow.ProjectId,
            ProjectNumber            = projectRow.ProjectNumber,
            ProjectTitle             = projectRow.ProjectTitle,
            TeamName                 = string.IsNullOrWhiteSpace(projectRow.TeamName)  ? null : projectRow.TeamName,
            TrackName                = string.IsNullOrWhiteSpace(projectRow.TrackName) ? null : projectRow.TrackName,
            StudentNames             = studentNames,
            StudentEmails            = studentEmails,
            MentorNames              = mentorNames,
            MentorEmails             = mentorEmails,
            CurrentMilestoneTitle    = currentMs?.Title,
            CurrentMilestoneStatus   = NormalizeMilestoneStatus(currentMs?.Status),
            CurrentMilestoneDueDate  = currentMs?.DueDate,
            MilestonesCompleted      = milestones.Count(m => IsMilestoneCompleted(m.Status)),
            MilestonesTotal          = milestones.Count,
            TasksDone                = tasks.Count(t => t.Status == "Done"),
            TasksTotal               = tasks.Count,
            NextTaskTitle            = nextTask?.Title,
            NextTaskDueDate          = nextTask?.DueDate,
        });
    }

    // Splits the GROUP_CONCAT result of "Name<#>Email||Name<#>Email|..." into
    // two parallel lists. Pairs without a name are dropped; missing emails
    // become empty strings so the client can render a "לא הוגדר" placeholder.
    private static (List<string> Names, List<string> Emails) SplitNameEmailPairs(string? csv)
    {
        var names  = new List<string>();
        var emails = new List<string>();
        if (string.IsNullOrWhiteSpace(csv)) return (names, emails);

        foreach (var record in csv.Split("||", StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = record.Split("<#>", 2);
            var name  = parts[0].Trim();
            var email = parts.Length > 1 ? parts[1].Trim() : "";
            if (name.Length == 0) continue;
            names.Add(name);
            emails.Add(email);
        }
        return (names, emails);
    }

    // ── GET /api/projects/my-milestones ──────────────────────────────────────
    // Returns all milestones for the authenticated student's project,
    // with per-milestone task counts and pre-calculated progress %.
    // A single aggregated SQL query avoids N+1 task lookups.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-milestones")]
    public async Task<IActionResult> GetMyMilestones(int authUserId)
    {
        // ── 1. Resolve user → project ─────────────────────────────────────────
        const string projectSql = @"
            SELECT  p.Id
            FROM    Projects     p
            JOIN    Teams        t   ON p.TeamId  = t.Id
            JOIN    TeamMembers  tm  ON t.Id       = tm.TeamId
            WHERE   tm.UserId  = @UserId
              AND   tm.IsActive = 1
            LIMIT 1";

        var projectIdRow = (await _db.GetRecordsAsync<MilestoneProjectIdRow>(
                projectSql, new { UserId = authUserId }))
            .FirstOrDefault();

        if (projectIdRow is null)
            return Ok(new MilestonesPageDto());

        int projectId = projectIdRow.Id;

        // ── 2. Milestones with aggregated task counts (single query) ──────────
        // LEFT JOIN on Tasks ensures milestones with no tasks still appear.
        // COUNT/SUM aggregate per milestone group.
        // Progress % is derived server-side so the client receives ready values.
        const string milestonesSql = @"
            SELECT  pm.Id          AS ProjectMilestoneId,
                    mt.Title,
                    mt.OrderIndex,
                    pm.Status,
                    aym.DueDate,
                    pm.CompletedAt,
                    COUNT(t.Id)    AS TotalTasks,
                    COALESCE(SUM(CASE WHEN t.Status = 'Done' THEN 1 ELSE 0 END), 0)
                                   AS CompletedTasks,
                    CASE WHEN (
                        SELECT COUNT(*)
                        FROM   Tasks t2
                        WHERE  t2.ProjectMilestoneId = pm.Id
                          AND  (SELECT s.MentorStatus
                                FROM   TaskSubmissions s
                                WHERE  s.TaskId = t2.Id
                                ORDER  BY s.Id DESC LIMIT 1) = 'Returned'
                    ) > 0 THEN 1 ELSE 0 END AS HasReturnedTask
            FROM    ProjectMilestones       pm
            JOIN    AcademicYearMilestones  aym ON pm.AcademicYearMilestoneId = aym.Id
            JOIN    MilestoneTemplates      mt  ON aym.MilestoneTemplateId    = mt.Id
            LEFT JOIN Tasks                 t   ON t.ProjectMilestoneId = pm.Id
                                               AND t.ProjectId          = @ProjectId
            WHERE   pm.ProjectId = @ProjectId
            GROUP   BY pm.Id, mt.Title, mt.OrderIndex, pm.Status, aym.DueDate, pm.CompletedAt
            ORDER   BY mt.OrderIndex";

        var rows = (await _db.GetRecordsAsync<MilestoneWithTasksRow>(
                milestonesSql, new { ProjectId = projectId }))
            .ToList();

        // ── 3. Determine the "current" milestone ──────────────────────────────
        // Priority: first InProgress → first Delayed → first NotStarted.
        // This matches the sidebar widget and the project context endpoint.
        var currentRow = rows.FirstOrDefault(r => r.Status == "InProgress")
                      ?? rows.FirstOrDefault(r => r.Status == "Delayed")
                      ?? rows.FirstOrDefault(r => r.Status == "NotStarted");

        // ── 4. Build DTO items ────────────────────────────────────────────────
        var items = rows.Select(r =>
        {
            // Progress: any "done" status (Completed OR Submitted) = 100 %.
            // InProgress/Delayed = task ratio. NotStarted = 0.
            int pct = IsMilestoneCompleted(r.Status)
                ? 100
                : (r.TotalTasks == 0 ? 0 : r.CompletedTasks * 100 / r.TotalTasks);

            return new MilestoneItemDto
            {
                ProjectMilestoneId = r.ProjectMilestoneId,
                Title              = r.Title,
                Status             = NormalizeMilestoneStatus(r.Status),
                OrderIndex         = r.OrderIndex,
                DueDate            = r.DueDate,
                CompletedAt        = r.CompletedAt,
                TotalTasks         = r.TotalTasks,
                CompletedTasks     = r.CompletedTasks,
                ProgressPct        = pct,
                IsCurrent          = r.ProjectMilestoneId == currentRow?.ProjectMilestoneId,
                HasReturnedTask    = r.HasReturnedTask,
            };
        }).ToList();

        return Ok(new MilestonesPageDto
        {
            TotalCount           = items.Count,
            CompletedCount       = items.Count(m => m.Status == "Completed"),
            CurrentMilestoneName = currentRow?.Title ?? "",
            Milestones           = items,
        });
    }

    // ── GET /api/projects/my-tasks ────────────────────────────────────────────
    // Returns the full tasks page payload for the authenticated student.
    // All grouping and count logic is resolved server-side.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-tasks")]
    public async Task<IActionResult> GetMyTasks(int authUserId)
    {
        // ── 1. Resolve user → team → project ─────────────────────────────────
        const string projectSql = @"
            SELECT  p.Id,
                    p.ProjectNumber,
                    p.Title,
                    p.Description,
                    p.Status,
                    p.HealthStatus,
                    pt.Name AS ProjectType,
                    t.Id    AS TeamId,
                    u.FirstName || ' ' || u.LastName AS StudentName
            FROM    Projects     p
            JOIN    Teams        t   ON p.TeamId       = t.Id
            JOIN    TeamMembers  tm  ON t.Id            = tm.TeamId
            JOIN    ProjectTypes pt  ON p.ProjectTypeId = pt.Id
            JOIN    Users        u   ON tm.UserId       = u.Id
            WHERE   tm.UserId  = @UserId
              AND   tm.IsActive = 1
            LIMIT 1";

        var projectRow = (await _db.GetRecordsAsync<TasksProjectRow>(
                projectSql, new { UserId = authUserId }))
            .FirstOrDefault();

        if (projectRow is null)
            return Ok(new TasksPageDto());

        int projectId = projectRow.Id;

        // ── 2. All tasks with milestone context (flat join) ───────────────────
        // Only tasks assigned to a milestone are included.
        // Tasks with null ProjectMilestoneId (unassigned) are excluded here.
        const string tasksSql = @"
            SELECT  pm.Id              AS ProjectMilestoneId,
                    mt.Title           AS MilestoneTitle,
                    mt.OrderIndex      AS MilestoneOrderIndex,
                    pm.Status          AS MilestoneStatus,
                    aym.DueDate        AS MilestoneDueDate,
                    t.Id               AS TaskId,
                    t.Title            AS TaskTitle,
                    t.Status           AS TaskStatus,
                    t.TaskType,
                    t.IsMandatory,
                    t.DueDate          AS TaskDueDate,
                    t.ClosedAt         AS CompletedAt,
                    COALESCE(u.FirstName || ' ' || u.LastName, '') AS AssignedToName,
                    t.IsSubmission,
                    (SELECT s.Status
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1) AS LatestSubmissionStatus,
                    (SELECT s.MentorStatus
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1) AS LatestMentorStatus,
                    (SELECT s.SubmittedAt
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1) AS LatestSubmittedAt
            FROM    ProjectMilestones       pm
            JOIN    AcademicYearMilestones  aym ON pm.AcademicYearMilestoneId = aym.Id
            JOIN    MilestoneTemplates      mt  ON aym.MilestoneTemplateId    = mt.Id
            JOIN    Tasks                   t   ON t.ProjectMilestoneId = pm.Id
                                               AND t.ProjectId          = @ProjectId
            LEFT JOIN Users                 u   ON t.AssignedToUserId   = u.Id
            WHERE   pm.ProjectId = @ProjectId
            ORDER   BY mt.OrderIndex, t.DueDate";

        var flatRows = (await _db.GetRecordsAsync<TaskFlatRow>(
            tasksSql, new { ProjectId = projectId })).ToList();

        // ── 3. Group by milestone ─────────────────────────────────────────────
        var milestoneGroups = flatRows
            .GroupBy(r => r.ProjectMilestoneId)
            .Select(g =>
            {
                var allTasks = g.Select(r => new TaskItemDto
                {
                    Id                     = r.TaskId,
                    Title                  = r.TaskTitle,
                    Status                 = NormalizeTaskStatus(r.TaskStatus, r.LatestMentorStatus, r.LatestSubmissionStatus),
                    TaskType               = r.TaskType,
                    IsMandatory            = r.IsMandatory,
                    DueDate                = r.TaskDueDate,
                    CompletedAt            = r.CompletedAt,
                    AssignedToName         = r.AssignedToName,
                    IsSubmission           = r.IsSubmission,
                    MilestoneStatus        = NormalizeMilestoneStatus(r.MilestoneStatus),
                    LatestSubmissionStatus = r.LatestSubmissionStatus,
                    LatestMentorStatus     = r.LatestMentorStatus,
                    LatestSubmittedAt      = r.LatestSubmittedAt,
                }).ToList();

                return new TaskMilestoneGroupDto
                {
                    ProjectMilestoneId = g.Key,
                    MilestoneTitle     = g.First().MilestoneTitle,
                    OrderIndex         = g.First().MilestoneOrderIndex,
                    MilestoneStatus    = NormalizeMilestoneStatus(g.First().MilestoneStatus),
                    DueDate            = g.First().MilestoneDueDate,
                    DoneCount          = allTasks.Count(t => t.Status == "Done"),
                    TotalCount         = allTasks.Count,
                    Tasks              = allTasks,
                };
            })
            .OrderBy(g => g.OrderIndex)
            .ToList();

        // ── 4. Split into active / completed groups ───────────────────────────
        // Active groups:    milestones with at least one non-Done task
        //                   → group's Tasks contains only non-Done tasks
        // Completed groups: milestones with at least one Done task
        //                   → group's Tasks contains only Done tasks
        var activeGroups = milestoneGroups
            .Where(g => g.Tasks.Any(t => t.Status != "Done"))
            .Select(g => new TaskMilestoneGroupDto
            {
                ProjectMilestoneId = g.ProjectMilestoneId,
                MilestoneTitle     = g.MilestoneTitle,
                OrderIndex         = g.OrderIndex,
                MilestoneStatus    = g.MilestoneStatus,
                DueDate            = g.DueDate,
                DoneCount          = g.DoneCount,
                TotalCount         = g.TotalCount,
                Tasks              = g.Tasks.Where(t => t.Status != "Done").ToList(),
            })
            .ToList();

        var completedGroups = milestoneGroups
            .Where(g => g.Tasks.Any(t => t.Status == "Done"))
            .Select(g => new TaskMilestoneGroupDto
            {
                ProjectMilestoneId = g.ProjectMilestoneId,
                MilestoneTitle     = g.MilestoneTitle,
                OrderIndex         = g.OrderIndex,
                MilestoneStatus    = g.MilestoneStatus,
                DueDate            = g.DueDate,
                DoneCount          = g.DoneCount,
                TotalCount         = g.TotalCount,
                Tasks              = g.Tasks.Where(t => t.Status == "Done").ToList(),
            })
            .ToList();

        // ── 5. Summary counts ─────────────────────────────────────────────────
        // NeedsAttention: tasks that require student action (returned or awaiting review)
        // Active:         remaining non-Done tasks in open milestones that don't need attention
        // Completed:      Done tasks regardless of milestone status
        var allTaskItems = milestoneGroups.SelectMany(g => g.Tasks).ToList();
        int pendingCount   = allTaskItems.Count(t => IsNeedsAttentionStatus(t.Status));
        int activeCount    = allTaskItems.Count(t => t.Status != "Done"
                                && !IsNeedsAttentionStatus(t.Status)
                                && (t.MilestoneStatus == "InProgress" || t.MilestoneStatus == "Delayed"));
        int completedCount = allTaskItems.Count(t => t.Status == "Done");

        return Ok(new TasksPageDto
        {
            StudentName    = projectRow.StudentName,
            ProjectNumber  = projectRow.ProjectNumber,
            ProjectTitle   = projectRow.Title,
            ActiveCount    = activeCount,
            PendingCount   = pendingCount,
            CompletedCount = completedCount,
            ActiveGroups   = activeGroups,
            CompletedGroups = completedGroups,
        });
    }

    // ── Milestone status helpers ──────────────────────────────────────────────
    // The DB stores milestone status as an open string. "Submitted" means the
    // student submitted deliverables — semantically identical to "Completed"
    // for all UI and count purposes. Normalize at the boundary so the client
    // always receives the canonical four-value set:
    //   NotStarted | InProgress | Delayed | Completed
    private static bool IsMilestoneCompleted(string? status) =>
        status is "Completed" or "Submitted";

    private static string NormalizeMilestoneStatus(string? status) =>
        IsMilestoneCompleted(status) ? "Completed" : status ?? "NotStarted";

    // ── Task status helpers ───────────────────────────────────────────────────
    // Some legacy tasks in the DB carry "Completed" instead of "Done".
    // Normalize at the read boundary so all downstream UI checks use "Done".
    private static string NormalizeTaskStatus(string? status) =>
        status is "Completed" ? "Done" : status ?? "Open";

    // Overload used when building TaskItemDto for the tasks-page list.
    // Latest submission review state takes priority over the stored task status:
    // a "Done" task whose latest submission was returned is NOT actually done.
    private static string NormalizeTaskStatus(
        string? status, string? latestMentorStatus, string? latestSubmissionStatus)
    {
        if (latestMentorStatus   == "Returned")      return "ReturnedForRevision";
        if (latestSubmissionStatus == "NeedsRevision") return "ReturnedForRevision";
        return NormalizeTaskStatus(status);
    }

    // Tasks that require immediate student action: returned or awaiting review.
    private static bool IsNeedsAttentionStatus(string status) =>
        status is "ReturnedForRevision" or "SubmittedToMentor" or "RevisionSubmitted";

    // Valid student progress status values (7-step workflow).
    private static readonly HashSet<string> ValidTaskProgressStatuses =
        new(StringComparer.Ordinal)
        {
            "Open", "InProgress",
            "SubmittedToMentor", "ReturnedForRevision",
            "RevisionSubmitted", "ApprovedForSubmission",
            "Done",
        };

    // ── Team resolution helper ────────────────────────────────────────────────
    private async Task<int?> GetTeamIdForUserAsync(int userId)
    {
        const string sql = @"
            SELECT t.Id
            FROM   Teams       t
            JOIN   TeamMembers tm ON t.Id = tm.TeamId
            WHERE  tm.UserId   = @UserId AND tm.IsActive = 1
            LIMIT 1";
        var rows = await _db.GetRecordsAsync<SubTeamIdRow>(sql, new { UserId = userId });
        return rows?.FirstOrDefault()?.Id;
    }
    private sealed class SubTeamIdRow { public int Id { get; set; } }

    // ── Private Dapper mapping rows ───────────────────────────────────────────
    // These are intermediate shapes for Dapper to fill from raw SQL results.
    // They are never exposed outside this controller.

    private sealed class ProjectRow
    {
        public int     Id            { get; set; }
        public int     ProjectNumber { get; set; }
        public string  Title        { get; set; } = "";
        public string? Description  { get; set; }
        public string  Status       { get; set; } = "";
        public string? HealthStatus { get; set; }
        public string  ProjectType  { get; set; } = "";
        public int     TeamId       { get; set; }
    }

    private sealed class MilestoneRow
    {
        public int       ProjectMilestoneId { get; set; }
        public string    Title              { get; set; } = "";
        public int       OrderIndex         { get; set; }
        public string    Status             { get; set; } = "";
        public DateTime? DueDate            { get; set; }
        public DateTime? CompletedAt        { get; set; }
    }

    private sealed class TaskRow
    {
        public int       Id                 { get; set; }
        public string    Title              { get; set; } = "";
        public string    Status             { get; set; } = "";
        public DateTime? DueDate            { get; set; }
        public int?      ProjectMilestoneId { get; set; }
        public bool      IsSubmission       { get; set; }
        public string    AssignedToName     { get; set; } = "";
        public string?   LatestSubmissionStatus { get; set; }
        public string?   LatestMentorStatus     { get; set; }
        public DateTime? LatestSubmittedAt      { get; set; }
    }

    // Used only by GetMyTasks — includes StudentName and milestone context per row.
    private sealed class TasksProjectRow
    {
        public int     Id            { get; set; }
        public int     ProjectNumber { get; set; }
        public string  Title        { get; set; } = "";
        public string  Status       { get; set; } = "";
        public string? HealthStatus { get; set; }
        public string  ProjectType  { get; set; } = "";
        public int     TeamId       { get; set; }
        public string  StudentName  { get; set; } = "";
    }

    // Used by GetMyMilestones ─────────────────────────────────────────────────
    private sealed class MilestoneProjectIdRow { public int Id { get; set; } }

    private sealed class MilestoneWithTasksRow
    {
        public int       ProjectMilestoneId { get; set; }
        public string    Title              { get; set; } = "";
        public int       OrderIndex         { get; set; }
        public string    Status             { get; set; } = "";
        public DateTime? DueDate            { get; set; }
        public DateTime? CompletedAt        { get; set; }
        public int       TotalTasks         { get; set; }
        public int       CompletedTasks     { get; set; }
        public bool      HasReturnedTask    { get; set; }
    }

    // Used by GetMyContext ─────────────────────────────────────────────────────
    private sealed class ContextProjectRow
    {
        public int     ProjectId         { get; set; }
        public int     ProjectNumber     { get; set; }
        public string  ProjectTitle      { get; set; } = "";
        public string? TeamName          { get; set; }
        public string? TrackName         { get; set; }
        public string? StudentDetailsCsv { get; set; }
        public string? MentorDetailsCsv  { get; set; }
    }

    private sealed class ContextMilestoneRow
    {
        public string    Title   { get; set; } = "";
        public string    Status  { get; set; } = "";
        public DateTime? DueDate { get; set; }
    }

    private sealed class ContextTaskRow
    {
        public string    Status  { get; set; } = "";
        public string    Title   { get; set; } = "";
        public DateTime? DueDate { get; set; }
    }

    // Flat row returned by the tasks+milestones join query.
    private sealed class TaskFlatRow
    {
        public int       ProjectMilestoneId  { get; set; }
        public string    MilestoneTitle      { get; set; } = "";
        public int       MilestoneOrderIndex { get; set; }
        public string    MilestoneStatus     { get; set; } = "";
        public DateTime? MilestoneDueDate    { get; set; }
        public int       TaskId              { get; set; }
        public string    TaskTitle           { get; set; } = "";
        public string    TaskStatus          { get; set; } = "";
        public string    TaskType            { get; set; } = "";
        public bool      IsMandatory         { get; set; }
        public DateTime? TaskDueDate         { get; set; }
        public DateTime? CompletedAt         { get; set; }
        public string    AssignedToName      { get; set; } = "";
        public bool      IsSubmission        { get; set; }
        public string?   LatestSubmissionStatus { get; set; }
        public string?   LatestMentorStatus     { get; set; }
        public DateTime? LatestSubmittedAt      { get; set; }
    }

    // ── GET /api/projects/my-submission-tasks ────────────────────────────────
    // Returns all submission tasks for the authenticated student's project,
    // each with its latest submission state.
    // Used by the /submissions page and the SubmissionModal.
    [HttpGet("my-submission-tasks")]
    public async Task<IActionResult> GetMySubmissionTasks(int authUserId)
    {
        // ── 1. Resolve project ────────────────────────────────────────────────
        const string projectSql = @"
            SELECT  p.Id
            FROM    Projects     p
            JOIN    Teams        t   ON p.TeamId  = t.Id
            JOIN    TeamMembers  tm  ON t.Id       = tm.TeamId
            WHERE   tm.UserId  = @UserId
              AND   tm.IsActive = 1
            LIMIT 1";

        var projectIdRow = (await _db.GetRecordsAsync<MilestoneProjectIdRow>(
                projectSql, new { UserId = authUserId }))
            .FirstOrDefault();

        if (projectIdRow is null)
            return Ok(Enumerable.Empty<StudentSubmissionTaskDto>());

        int projectId = projectIdRow.Id;

        // ── 2. Submission tasks with latest submission state ──────────────────
        // Correlated subqueries on TaskSubmissions give the latest row's
        // status and date without a GROUP BY complication.
        const string sql = @"
            SELECT  t.Id                                   AS TaskId,
                    t.Title                                AS TaskTitle,
                    t.Description,
                    COALESCE(mt.Title, '')                 AS MilestoneTitle,
                    t.DueDate,
                    t.Status                               AS TaskStatus,
                    t.SubmissionInstructions,
                    t.MaxFilesCount,
                    t.MaxFileSizeMb,
                    t.AllowedFileTypes,
                    (SELECT COUNT(*) FROM TaskSubmissions s WHERE s.TaskId = t.Id)
                                                           AS SubmissionCount,
                    (SELECT s.Id
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestSubmissionId,
                    (SELECT s.Status
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestSubmissionStatus,
                    (SELECT s.MentorStatus
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestMentorStatus,
                    (SELECT s.CourseSubmittedAt
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestCourseSubmittedAt,
                    (SELECT s.SubmittedAt
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestSubmittedAt
            FROM    Tasks                    t
            LEFT JOIN ProjectMilestones      pm  ON pm.Id  = t.ProjectMilestoneId
            LEFT JOIN AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            LEFT JOIN MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            WHERE   t.ProjectId    = @ProjectId
              AND   t.IsSubmission = 1
            ORDER   BY t.DueDate NULLS LAST, t.Id";

        var rows = await _db.GetRecordsAsync<StudentSubmissionTaskDto>(
            sql, new { ProjectId = projectId });

        return Ok(rows ?? Enumerable.Empty<StudentSubmissionTaskDto>());
    }

    // ── GET /api/projects/my-submission-tasks/{taskId} ───────────────────────
    // Returns a single submission task with its latest submission state.
    // Used by the SubmissionModal when it opens for a specific task.
    [HttpGet("my-submission-tasks/{taskId:int}")]
    public async Task<IActionResult> GetMySubmissionTask(int taskId, int authUserId)
    {
        // ── 1. Resolve project ────────────────────────────────────────────────
        const string projectSql = @"
            SELECT  p.Id
            FROM    Projects     p
            JOIN    Teams        t   ON p.TeamId  = t.Id
            JOIN    TeamMembers  tm  ON t.Id       = tm.TeamId
            WHERE   tm.UserId  = @UserId
              AND   tm.IsActive = 1
            LIMIT 1";

        var projectIdRow = (await _db.GetRecordsAsync<MilestoneProjectIdRow>(
                projectSql, new { UserId = authUserId }))
            .FirstOrDefault();

        if (projectIdRow is null) return NotFound("פרויקט לא נמצא");

        int projectId = projectIdRow.Id;

        // ── 2. Single task (must belong to the user's project) ────────────────
        const string sql = @"
            SELECT  t.Id                                   AS TaskId,
                    t.Title                                AS TaskTitle,
                    t.Description,
                    COALESCE(mt.Title, '')                 AS MilestoneTitle,
                    t.DueDate,
                    t.Status                               AS TaskStatus,
                    t.SubmissionInstructions,
                    t.MaxFilesCount,
                    t.MaxFileSizeMb,
                    t.AllowedFileTypes,
                    (SELECT COUNT(*) FROM TaskSubmissions s WHERE s.TaskId = t.Id)
                                                           AS SubmissionCount,
                    (SELECT s.Id
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestSubmissionId,
                    (SELECT s.Status
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestSubmissionStatus,
                    (SELECT s.MentorStatus
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestMentorStatus,
                    (SELECT s.CourseSubmittedAt
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestCourseSubmittedAt,
                    (SELECT s.SubmittedAt
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestSubmittedAt
            FROM    Tasks                    t
            LEFT JOIN ProjectMilestones      pm  ON pm.Id  = t.ProjectMilestoneId
            LEFT JOIN AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            LEFT JOIN MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            WHERE   t.Id         = @TaskId
              AND   t.ProjectId  = @ProjectId
              AND   t.IsSubmission = 1";

        var row = (await _db.GetRecordsAsync<StudentSubmissionTaskDto>(
                sql, new { TaskId = taskId, ProjectId = projectId }))
            .FirstOrDefault();

        if (row is null) return NotFound("משימת ההגשה לא נמצאה");
        return Ok(row);
    }

    // ── GET /api/projects/tasks/{taskId}/detail ──────────────────────────────
    // Returns full task details + complete submission history for the student.
    // Scoped to the requesting user's own project — students cannot access
    // tasks that belong to other projects.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("tasks/{taskId:int}/detail")]
    public async Task<IActionResult> GetTaskDetail(int taskId, int authUserId)
    {
        // ── 1. Resolve user → project ────────────────────────────────────────
        const string projectSql = @"
            SELECT p.Id
            FROM   Projects    p
            JOIN   Teams       t  ON p.TeamId = t.Id
            JOIN   TeamMembers tm ON t.Id     = tm.TeamId
            WHERE  tm.UserId   = @UserId AND tm.IsActive = 1
            LIMIT 1";

        var projectIdRow = (await _db.GetRecordsAsync<MilestoneProjectIdRow>(
                projectSql, new { UserId = authUserId }))
            .FirstOrDefault();

        if (projectIdRow is null) return NotFound("פרויקט לא נמצא");
        int projectId = projectIdRow.Id;

        // ── 2. Fetch task (must belong to the student's project) ─────────────
        const string taskSql = @"
            SELECT  t.Id,
                    t.Title,
                    t.Description,
                    COALESCE(mt.Title, '')  AS MilestoneTitle,
                    t.CreatedAt            AS OpenDate,
                    t.DueDate,
                    t.Status,
                    t.TaskType,
                    t.IsSubmission,
                    t.SubmissionInstructions,
                    t.MaxFilesCount,
                    t.MaxFileSizeMb,
                    t.AllowedFileTypes,
                    (SELECT s.MentorStatus
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1) AS LatestMentorStatus,
                    (SELECT s.Id
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1) AS LatestSubmissionId,
                    (SELECT s.CourseSubmittedAt
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1) AS LatestCourseSubmittedAt
            FROM    Tasks                    t
            LEFT JOIN ProjectMilestones      pm  ON pm.Id  = t.ProjectMilestoneId
            LEFT JOIN AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            LEFT JOIN MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            WHERE   t.Id        = @TaskId
              AND   t.ProjectId = @ProjectId";

        var task = (await _db.GetRecordsAsync<TaskDetailDto>(
                taskSql, new { TaskId = taskId, ProjectId = projectId }))
            .FirstOrDefault();

        if (task is null) return NotFound("המשימה לא נמצאה");

        // Normalize legacy "Completed" → "Done" so the client always receives
        // the canonical status string.
        task.Status = NormalizeTaskStatus(task.Status);

        // ── 3. Fetch all submissions (newest first) ──────────────────────────
        if (task.IsSubmission)
        {
            const string subsSql = @"
                SELECT  s.Id,
                        s.SubmittedAt,
                        s.Notes,
                        s.Status,
                        s.ReviewerFeedback,
                        s.MentorStatus,
                        s.MentorFeedback,
                        s.MentorReviewedAt,
                        s.CourseSubmittedAt
                FROM    TaskSubmissions s
                WHERE   s.TaskId = @TaskId
                ORDER   BY s.Id ASC";

            var subs = (await _db.GetRecordsAsync<SubmissionHistoryItemDto>(
                    subsSql, new { TaskId = taskId }))
                .ToList();

            // Attach files to each submission
            if (subs.Count > 0)
            {
                const string filesSql = @"
                    SELECT  f.Id,
                            f.TaskSubmissionId,
                            f.OriginalFileName,
                            f.StoredFileName,
                            f.ContentType,
                            f.SizeBytes,
                            f.UploadedAt
                    FROM    TaskSubmissionFiles f
                    WHERE   f.TaskSubmissionId IN
                        (SELECT s2.Id FROM TaskSubmissions s2 WHERE s2.TaskId = @TaskId)
                    ORDER   BY f.TaskSubmissionId DESC, f.Id";

                var allFiles = (await _db.GetRecordsAsync<TaskSubmissionFileDto>(
                        filesSql, new { TaskId = taskId }))
                    .ToList();

                var filesBySubmission = allFiles
                    .GroupBy(f => f.TaskSubmissionId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var sub in subs)
                    sub.Files = filesBySubmission.GetValueOrDefault(sub.Id) ?? new();
            }

            task.Submissions = subs;
        }

        return Ok(task);
    }

    // ── PATCH /api/projects/tasks/{taskId}/progress ──────────────────────────
    // Allows the authenticated student to update their personal progress status
    // on any task that belongs to their project.
    // Valid values: "Open" | "InProgress" | "Done"
    // Students control their own progress; this endpoint does NOT affect
    // submission approval statuses (MentorStatus, reviewer Status).
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPatch("tasks/{taskId:int}/progress")]
    public async Task<IActionResult> UpdateTaskProgress(
        int taskId, [FromBody] UpdateTaskProgressRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.Status) ||
            !ValidTaskProgressStatuses.Contains(req.Status))
            return BadRequest(
                "סטטוס לא תקין. ערכים חוקיים: Open, InProgress, SubmittedToMentor, " +
                "ReturnedForRevision, RevisionSubmitted, ApprovedForSubmission, Done");

        // Verify the task belongs to the student's project
        const string projectSql = @"
            SELECT p.Id
            FROM   Projects    p
            JOIN   Teams       t  ON p.TeamId = t.Id
            JOIN   TeamMembers tm ON t.Id     = tm.TeamId
            WHERE  tm.UserId   = @UserId AND tm.IsActive = 1
            LIMIT 1";

        var projectIdRow = (await _db.GetRecordsAsync<MilestoneProjectIdRow>(
                projectSql, new { UserId = authUserId }))
            .FirstOrDefault();

        if (projectIdRow is null) return NotFound("פרויקט לא נמצא");

        const string updateSql = @"
            UPDATE Tasks
            SET    Status   = @Status,
                   ClosedAt = CASE WHEN @Status = 'Done' THEN datetime('now') ELSE NULL END
            WHERE  Id        = @TaskId
              AND  ProjectId = @ProjectId";

        int affected = await _db.SaveDataAsync(updateSql, new
        {
            req.Status,
            TaskId    = taskId,
            ProjectId = projectIdRow.Id,
        });

        if (affected == 0) return NotFound("המשימה לא נמצאה");
        return Ok();
    }

    // ── GET /api/projects/tasks/{taskId}/subtasks ────────────────────────────
    // Returns all student sub-tasks for the calling team + parent task.
    // Scoped to the caller's active team — other teams' sub-tasks are invisible.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("tasks/{taskId:int}/subtasks")]
    public async Task<IActionResult> GetSubTasks(int taskId, int authUserId)
    {
        var teamId = await GetTeamIdForUserAsync(authUserId);
        if (teamId is null) return NotFound("צוות לא נמצא");

        const string sql = @"
            SELECT  st.Id,
                    st.TaskId,
                    st.Title,
                    st.IsDone,
                    COALESCE(st.Status, 'Open') AS Status,
                    st.DueDate,
                    st.Notes,
                    st.CreatedAt,
                    COALESCE(u.FirstName || ' ' || u.LastName, '') AS CreatedByName
            FROM    StudentSubTasks st
            LEFT JOIN users u ON u.Id = st.CreatedByUserId
            WHERE   st.TaskId = @TaskId
              AND   st.TeamId = @TeamId
            ORDER   BY st.CreatedAt";

        var rows = await _db.GetRecordsAsync<StudentSubTaskDto>(
            sql, new { TaskId = taskId, TeamId = teamId });
        return Ok(rows ?? Enumerable.Empty<StudentSubTaskDto>());
    }

    // ── POST /api/projects/tasks/{taskId}/subtasks ───────────────────────────
    // Creates a new student sub-task for the calling team under the given task.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("tasks/{taskId:int}/subtasks")]
    public async Task<IActionResult> CreateSubTask(
        int taskId, [FromBody] CreateSubTaskRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("כותרת המשימה לא יכולה להיות ריקה");

        var teamId = await GetTeamIdForUserAsync(authUserId);
        if (teamId is null) return NotFound("צוות לא נמצא");

        var validSubTaskStatuses = new HashSet<string>
            { "Open", "InProgress", "Done" };
        var status = validSubTaskStatuses.Contains(req.Status) ? req.Status : "Open";

        const string sql = @"
            INSERT INTO StudentSubTasks (TaskId, TeamId, Title, IsDone, Status, DueDate, Notes, CreatedByUserId)
            VALUES (@TaskId, @TeamId, @Title, 0, @Status, @DueDate, @Notes, @CreatedByUserId)";

        int newId = await _db.InsertReturnIdAsync(sql, new
        {
            TaskId          = taskId,
            TeamId          = teamId.Value,
            Title           = req.Title.Trim(),
            Status          = status,
            DueDate         = req.DueDate?.ToString("yyyy-MM-dd"),
            Notes           = req.Notes,
            CreatedByUserId = authUserId,
        });

        if (newId == 0) return StatusCode(500, "שגיאה ביצירת המשימה");

        var created = new StudentSubTaskDto
        {
            Id        = newId,
            TaskId    = taskId,
            Title     = req.Title.Trim(),
            IsDone    = false,
            Status    = status,
            DueDate   = req.DueDate,
            Notes     = req.Notes,
            CreatedAt = DateTime.UtcNow,
        };
        return Ok(created);
    }

    // ── PATCH /api/projects/subtasks/{id}/toggle ─────────────────────────────
    // Toggles the IsDone flag on a student sub-task.
    // Only the owning team may modify their own sub-tasks.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPatch("subtasks/{id:int}/toggle")]
    public async Task<IActionResult> ToggleSubTask(int id, int authUserId)
    {
        var teamId = await GetTeamIdForUserAsync(authUserId);
        if (teamId is null) return NotFound("צוות לא נמצא");

        const string sql = @"
            UPDATE StudentSubTasks
            SET    IsDone = CASE WHEN IsDone = 1 THEN 0 ELSE 1 END
            WHERE  Id     = @Id
              AND  TeamId = @TeamId";

        int affected = await _db.SaveDataAsync(sql, new { Id = id, TeamId = teamId.Value });
        if (affected == 0) return NotFound("המשימה לא נמצאה");
        return Ok();
    }

    // ── PATCH /api/projects/subtasks/{id} ───────────────────────────────────
    // Updates title, status, due date, and notes on a student sub-task.
    // Only the owning team may modify their own sub-tasks.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPatch("subtasks/{id:int}")]
    public async Task<IActionResult> UpdateSubTask(
        int id, [FromBody] UpdateSubTaskRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("כותרת המשימה לא יכולה להיות ריקה");

        var teamId = await GetTeamIdForUserAsync(authUserId);
        if (teamId is null) return NotFound("צוות לא נמצא");

        var validSubTaskStatuses = new HashSet<string>
            { "Open", "InProgress", "Done" };
        var status = validSubTaskStatuses.Contains(req.Status) ? req.Status : "Open";

        const string sql = @"
            UPDATE StudentSubTasks
            SET    Title   = @Title,
                   Status  = @Status,
                   DueDate = @DueDate,
                   Notes   = @Notes
            WHERE  Id     = @Id
              AND  TeamId = @TeamId";

        int affected = await _db.SaveDataAsync(sql, new
        {
            Id      = id,
            TeamId  = teamId.Value,
            Title   = req.Title.Trim(),
            Status  = status,
            DueDate = req.DueDate?.ToString("yyyy-MM-dd"),
            Notes   = req.Notes,
        });

        if (affected == 0) return NotFound("המשימה לא נמצאה");
        return Ok();
    }

    // ── DELETE /api/projects/subtasks/{id} ───────────────────────────────────
    // Deletes a student sub-task. Only the owning team may delete their rows.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("subtasks/{id:int}")]
    public async Task<IActionResult> DeleteSubTask(int id, int authUserId)
    {
        var teamId = await GetTeamIdForUserAsync(authUserId);
        if (teamId is null) return NotFound("צוות לא נמצא");

        const string sql = @"
            DELETE FROM StudentSubTasks
            WHERE  Id     = @Id
              AND  TeamId = @TeamId";

        int affected = await _db.SaveDataAsync(sql, new { Id = id, TeamId = teamId.Value });
        if (affected == 0) return NotFound("המשימה לא נמצאה");
        return NoContent();
    }

    // ── GET /api/projects/my-project-details ─────────────────────────────────
    // Returns the full student-safe details for the authenticated user's project.
    // Excludes all internal management fields (health status, priority,
    // internal notes, source type, Airtable IDs, assignment metadata).
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-project-details")]
    public async Task<IActionResult> GetMyProjectDetails(int authUserId)
    {
        const string sql = @"
            SELECT  p.Id,
                    p.ProjectNumber,
                    p.Title,
                    pt.Name   AS ProjectType,
                    ay.Name   AS AcademicYear,
                    p.Description,
                    p.Goals,
                    p.TargetAudience,
                    p.OrganizationName,
                    p.OrganizationType,
                    p.ContactPerson,
                    p.ContactRole,
                    p.ContactEmail,
                    p.ContactPhone,
                    p.ProjectTopic,
                    p.Contents
            FROM    Projects      p
            JOIN    ProjectTypes  pt  ON p.ProjectTypeId  = pt.Id
            JOIN    AcademicYears ay  ON p.AcademicYearId = ay.Id
            JOIN    Teams         t   ON p.TeamId         = t.Id
            JOIN    TeamMembers   tm  ON t.Id             = tm.TeamId
            WHERE   tm.UserId   = @UserId
              AND   tm.IsActive = 1
            LIMIT 1";

        var row = (await _db.GetRecordsAsync<StudentProjectDetailsDto>(
                sql, new { UserId = authUserId }))
            .FirstOrDefault();

        if (row is null) return NotFound("פרויקט לא נמצא");
        return Ok(row);
    }
}
