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
            LIMIT 1";

        var projectRow = (await _db.GetRecordsAsync<ProjectRow>(
                projectSql, new { UserId = authUserId }))
            .FirstOrDefault();

        // User is not yet assigned to a project — return an empty dashboard.
        if (projectRow is null)
            return Ok(new DashboardDto());

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
                    COALESCE(u.FirstName || ' ' || u.LastName, '') AS AssignedToName
            FROM    Tasks t
            LEFT JOIN Users u ON t.AssignedToUserId = u.Id
            WHERE   t.ProjectId = @ProjectId
            ORDER   BY t.DueDate";

        var taskRows = await _db.GetRecordsAsync<TaskRow>(
            tasksSql, new { ProjectId = projectId });

        // ── 6. Open requests ──────────────────────────────────────────────────
        const string requestsSql = @"
            SELECT  r.Id,
                    r.Title,
                    rt.Name AS RequestType,
                    r.Status,
                    r.OpenedAt
            FROM    Requests     r
            JOIN    RequestTypes rt ON r.RequestTypeId = rt.Id
            WHERE   r.ProjectId = @ProjectId
              AND   r.Status   != 'Closed'
            ORDER   BY r.OpenedAt DESC";

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
                    Id             = t.Id,
                    Title          = t.Title,
                    Status         = t.Status,
                    DueDate        = t.DueDate,
                    AssignedToName = t.AssignedToName,
                })
                .ToList(),
        }).ToList();

        // ── Derive next deadline from milestones ──────────────────────────────
        var nextDeadline = milestones
            .Where(m => !IsMilestoneCompleted(m.Status) && m.DueDate.HasValue)
            .OrderBy(m => m.DueDate)
            .Select(m => new UpcomingDeadlineDto { Title = m.Title, DueDate = m.DueDate!.Value })
            .FirstOrDefault();

        // ── Build and return the dashboard DTO ────────────────────────────────
        var dashboard = new DashboardDto
        {
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
        // ── 1. Resolve user → project ─────────────────────────────────────────
        const string projectSql = @"
            SELECT  p.Id            AS ProjectId,
                    p.ProjectNumber,
                    p.Title         AS ProjectTitle
            FROM    Projects     p
            JOIN    Teams        t   ON p.TeamId  = t.Id
            JOIN    TeamMembers  tm  ON t.Id       = tm.TeamId
            WHERE   tm.UserId  = @UserId
              AND   tm.IsActive = 1
            LIMIT 1";

        var projectRow = (await _db.GetRecordsAsync<ContextProjectRow>(
                projectSql, new { UserId = authUserId }))
            .FirstOrDefault();

        if (projectRow is null)
            return Ok((ProjectContextDto?)null);

        int projectId = projectRow.ProjectId;

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
            ProjectNumber            = projectRow.ProjectNumber,
            ProjectTitle             = projectRow.ProjectTitle,
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
                                   AS CompletedTasks
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
                    COALESCE(u.FirstName || ' ' || u.LastName, '') AS AssignedToName
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
                    Id              = r.TaskId,
                    Title           = r.TaskTitle,
                    Status          = r.TaskStatus,
                    TaskType        = r.TaskType,
                    IsMandatory     = r.IsMandatory,
                    DueDate         = r.TaskDueDate,
                    CompletedAt     = r.CompletedAt,
                    AssignedToName  = r.AssignedToName,
                    MilestoneStatus = NormalizeMilestoneStatus(r.MilestoneStatus),
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
        // Active:    non-Done tasks in InProgress or Delayed milestones (actionable now)
        // Pending:   non-Done tasks in NotStarted milestones (milestone not yet opened)
        // Completed: Done tasks regardless of milestone status
        var allTaskItems = milestoneGroups.SelectMany(g => g.Tasks).ToList();
        int activeCount    = allTaskItems.Count(t => t.Status != "Done"
                                && (t.MilestoneStatus == "InProgress" || t.MilestoneStatus == "Delayed"));
        int pendingCount   = allTaskItems.Count(t => t.Status != "Done"
                                && t.MilestoneStatus == "NotStarted");
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
        public string    AssignedToName     { get; set; } = "";
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
    }

    // Used by GetMyContext ─────────────────────────────────────────────────────
    private sealed class ContextProjectRow
    {
        public int    ProjectId     { get; set; }
        public int    ProjectNumber { get; set; }
        public string ProjectTitle  { get; set; } = "";
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
    }
}
