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

    /// <summary>
    /// Only used by DeleteTeamTask, to clean up any Google Calendar events the
    /// deleted task left behind. Injecting the service — rather than reaching for
    /// the Calendar API here — keeps every Google call inside that one service.
    /// </summary>
    private readonly GoogleCalendarEventService _calendarEvents;

    /// <summary>Project-logo upload/replace. The same helper every other image
    /// in the product goes through — SaveFile resizes to a 600px box and names
    /// the stored file, so this controller never invents a filename.</summary>
    private readonly FilesManage _files;

    public ProjectsController(
        DbRepository db, GoogleCalendarEventService calendarEvents, FilesManage files)
    {
        _db             = db;
        _calendarEvents = calendarEvents;
        _files          = files;
    }

    // ── Project logo ─────────────────────────────────────────────────────────
    // The wwwroot container the team's uploaded marks live in, alongside
    // "profile-images", "request-attachments", "resources" and "submissions".
    private const string LogoContainer = "project-logos";

    // Mirrors StudentController's avatar guard exactly. ImageSharp re-encodes
    // whatever it is handed, so this list is about what we are willing to
    // accept, not about what it can read.
    private static readonly HashSet<string> AllowedLogoExts =
        new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp" };

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
                    COALESCE(ay.Name, '') AS AcademicYear,
                    t.Id     AS TeamId
            FROM    Projects     p
            JOIN    Teams        t   ON p.TeamId         = t.Id
            JOIN    TeamMembers  tm  ON t.Id              = tm.TeamId
            JOIN    ProjectTypes pt  ON p.ProjectTypeId   = pt.Id
            LEFT JOIN AcademicYears ay ON ay.Id           = p.AcademicYearId
            WHERE   tm.UserId  = @UserId
              AND   tm.IsActive = 1
              AND   COALESCE(p.AssignmentIsDraft, 0) = 0
            LIMIT 1";

        // GetRecordsAsync swallows transient query failures and returns null —
        // every result is null-coalesced to an empty sequence below before any
        // LINQ call touches it, so a single transient hiccup can never bubble up
        // as an unhandled NullReferenceException (which the client surfaces as
        // a generic load error). This is what makes the first dashboard load
        // stable for every student/team, not just on retry.
        var projectRow = (await _db.GetRecordsAsync<ProjectRow>(
                projectSql, new { UserId = authUserId })
            ?? Enumerable.Empty<ProjectRow>())
            .FirstOrDefault();

        // User is not yet assigned to a project — return minimal dashboard with HasTeam flag.
        if (projectRow is null)
        {
            var teamCount = (await _db.GetRecordsAsync<int>(
                "SELECT COUNT(1) FROM TeamMembers WHERE UserId = @UserId AND IsActive = 1",
                new { UserId = authUserId })
                ?? Enumerable.Empty<int>()).FirstOrDefault();
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
            membersSql, new { TeamId = teamId })
            ?? Enumerable.Empty<TeamMemberDto>();

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
            mentorsSql, new { ProjectId = projectId })
            ?? Enumerable.Empty<ContactDto>();

        // ── 4. Milestones (3-table flatten) ───────────────────────────────────
        // Merges: MilestoneTemplates → AcademicYearMilestones → ProjectMilestones.
        // Effective DueDate = COALESCE(team milestone override, AYM.DueDate).
        // IsCurrentlyOpen mirrors GetMyTasks's date-window rule exactly — both
        // screens must agree on which milestones are "active" so the dashboard's
        // Active Tasks card and the Tasks tab never disagree on visibility.
        const string milestonesSql = @"
            SELECT  pm.Id          AS ProjectMilestoneId,
                    mt.Title,
                    mt.OrderIndex,
                    pm.Status,
                    aym.OpenDate   AS OpenDate,
                    COALESCE(mo.OverrideDueDate, aym.DueDate) AS DueDate,
                    aym.CloseDate  AS CloseDate,
                    pm.CompletedAt,
                    CASE
                        WHEN (aym.OpenDate  IS NULL OR date(aym.OpenDate)  <= date('now'))
                         AND (aym.CloseDate IS NULL OR date(aym.CloseDate) >= date('now'))
                        THEN 1 ELSE 0
                    END            AS IsCurrentlyOpen
            FROM    ProjectMilestones       pm
            JOIN    AcademicYearMilestones  aym ON pm.AcademicYearMilestoneId = aym.Id
            JOIN    MilestoneTemplates      mt  ON aym.MilestoneTemplateId    = mt.Id
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                                                ON mo.TeamId             = @TeamId
                                               AND mo.ProjectMilestoneId = pm.Id
            WHERE   pm.ProjectId = @ProjectId
            ORDER   BY mt.OrderIndex";

        var milestoneRows = await _db.GetRecordsAsync<MilestoneRow>(
            milestonesSql, new { ProjectId = projectId, TeamId = teamId })
            ?? Enumerable.Empty<MilestoneRow>();

        // ── 5. Tasks (all, grouped into milestones below) ─────────────────────
        // Effective DueDate priority chain (per-team only — globals are never
        // mutated by the postponement/override flow):
        //   1. TeamTaskDueDateOverrides       (per-task)
        //   2. TeamMilestoneDueDateOverrides  (per-milestone, fallback)
        //   3. Tasks.DueDate                  (global default)
        const string tasksSql = @"
            SELECT  t.Id,
                    t.Title,
                    t.Description,
                    t.Status,
                    COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate) AS DueDate,
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
                     ORDER  BY s.Id DESC LIMIT 1) AS LatestSubmittedAt,
                    (SELECT CASE WHEN s.MoodleSubmittedAt IS NOT NULL
                                   OR s.CourseSubmittedAt IS NOT NULL
                             THEN 1 ELSE 0 END
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1) AS LatestMoodleConfirmed
            FROM    Tasks t
            LEFT JOIN Users u ON t.AssignedToUserId = u.Id
            LEFT JOIN TeamTaskDueDateOverrides tto
                            ON tto.TeamId = @TeamId AND tto.TaskId = t.Id
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = @TeamId AND mo.ProjectMilestoneId = t.ProjectMilestoneId
            WHERE   t.ProjectId = @ProjectId
            ORDER   BY COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate)";

        var taskRows = (await _db.GetRecordsAsync<TaskRow>(
            tasksSql, new { ProjectId = projectId, TeamId = teamId })
            ?? Enumerable.Empty<TaskRow>()).ToList();

        // ── 6. Open requests ──────────────────────────────────────────────────
        // Reads from ProjectRequests (unified requests module).
        // Maps CreatedAt → OpenedAt to satisfy OpenRequestDto column mapping.
        //
        // 'Resolved' AND 'Closed' are both terminal (RequestStatuses documents
        // the lifecycle as New → InProgress → Resolved | Closed). Excluding only
        // 'Closed' put handled requests into the dashboard's "דורש התייחסות"
        // card labelled "ממתין לתגובה". ProjectOverviewController and
        // LecturerDashboardController already filter on both.
        const string requestsSql = @"
            SELECT  r.Id,
                    r.Title,
                    r.RequestType,
                    r.Status,
                    r.CreatedAt AS OpenedAt
            FROM    ProjectRequests r
            WHERE   r.ProjectId = @ProjectId
              AND   r.Status NOT IN ('Resolved', 'Closed')
            ORDER   BY r.CreatedAt DESC";

        var requests = await _db.GetRecordsAsync<OpenRequestDto>(
            requestsSql, new { ProjectId = projectId })
            ?? Enumerable.Empty<OpenRequestDto>();

        // ── Assemble milestones with nested tasks ─────────────────────────────
        var tasksByMilestone = taskRows.ToLookup(t => t.ProjectMilestoneId);

        var milestones = milestoneRows.Select(m => new MilestoneSummaryDto
        {
            ProjectMilestoneId = m.ProjectMilestoneId,
            Title              = m.Title,
            OrderIndex         = m.OrderIndex,
            Status             = NormalizeMilestoneStatus(m.Status),
            OpenDate           = m.OpenDate,
            DueDate            = m.DueDate,
            CloseDate          = m.CloseDate,
            CompletedAt        = m.CompletedAt,
            IsCurrentlyOpen    = m.IsCurrentlyOpen == 1,
            Tasks              = tasksByMilestone[m.ProjectMilestoneId]
                .Select(t => new TaskSummaryDto
                {
                    Id                     = t.Id,
                    Title                  = t.Title,
                    Description            = t.Description,
                    Status                 = NormalizeTaskStatus(t.Status, t.LatestMentorStatus, t.LatestSubmissionStatus),
                    DueDate                = t.DueDate,
                    AssignedToName         = t.AssignedToName,
                    IsSubmission           = t.IsSubmission,
                    LatestSubmissionStatus = t.LatestSubmissionStatus,
                    LatestMentorStatus     = t.LatestMentorStatus,
                    LatestSubmittedAt      = t.LatestSubmittedAt,
                    LatestMoodleConfirmed  = t.LatestMoodleConfirmed,
                })
                .ToList(),
        }).ToList();

        // ── Derive next deadline ──────────────────────────────────────────────
        // Prefer the nearest incomplete submission task; fall back to nearest milestone.
        var nearestSubmissionTask = taskRows
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
                AcademicYear  = projectRow.AcademicYear ?? "",
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
                    -- The team's own display name wins over the catalog title;
                    -- see ProjectTeamProfile in DatabaseMigrator. A project
                    -- with no row there resolves exactly as it did before.
                    COALESCE(NULLIF(TRIM(ptp.DisplayTitle), ''), p.Title)
                                                AS ProjectTitle,
                    t.Id                        AS TeamId,
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
                     WHERE  pm.ProjectId = p.Id) AS MentorDetailsCsv,
                    -- Parallel id list used by the client to open the
                    -- read-only mentor-profile modal. Order matches
                    -- MentorDetailsCsv so the i-th name/email pairs with
                    -- the i-th id.
                    (SELECT GROUP_CONCAT(pm.UserId, ',')
                     FROM   ProjectMentors pm
                     WHERE  pm.ProjectId = p.Id) AS MentorUserIdsCsv
            FROM    Projects     p
            JOIN    Teams        t   ON p.TeamId  = t.Id
            LEFT JOIN ProjectTypes pt ON pt.Id = p.ProjectTypeId
            LEFT JOIN ProjectTeamProfile ptp ON ptp.ProjectId = p.Id
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
        int teamId    = projectRow.TeamId;
        var (studentNames, studentEmails) = SplitNameEmailPairs(projectRow.StudentDetailsCsv);
        var (mentorNames,  mentorEmails)  = SplitNameEmailPairs(projectRow.MentorDetailsCsv);
        // Mentor IDs travel in a separate CSV ("12,34,56") to keep the
        // existing name/email parser untouched. Order is identical to
        // mentorNames / mentorEmails.
        var mentorUserIds = (projectRow.MentorUserIdsCsv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0)
            .Where(v => v > 0)
            .ToList();

        // ── 2. Milestones (for current-milestone + progress) ──────────────────
        // Effective DueDate = COALESCE(team milestone override, AYM.DueDate).
        const string milestonesSql = @"
            SELECT  mt.Title,
                    pm.Status,
                    COALESCE(mo.OverrideDueDate, aym.DueDate) AS DueDate
            FROM    ProjectMilestones       pm
            JOIN    AcademicYearMilestones  aym ON pm.AcademicYearMilestoneId = aym.Id
            JOIN    MilestoneTemplates      mt  ON aym.MilestoneTemplateId    = mt.Id
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                                                ON mo.TeamId             = @TeamId
                                               AND mo.ProjectMilestoneId = pm.Id
            WHERE   pm.ProjectId = @ProjectId
            ORDER   BY mt.OrderIndex";

        var milestones = (await _db.GetRecordsAsync<ContextMilestoneRow>(
                milestonesSql, new { ProjectId = projectId, TeamId = teamId }))
            .ToList();

        // ── 3. Tasks (for task counts + next open task) ───────────────────────
        // Effective DueDate priority: per-task override → per-milestone override → global.
        const string tasksSql = @"
            SELECT  t.Status,
                    t.Title,
                    COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate) AS DueDate
            FROM    Tasks t
            LEFT JOIN TeamTaskDueDateOverrides tto
                            ON tto.TeamId = @TeamId AND tto.TaskId = t.Id
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = @TeamId AND mo.ProjectMilestoneId = t.ProjectMilestoneId
            WHERE   t.ProjectId = @ProjectId
            ORDER   BY COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate) NULLS LAST, t.Id";

        var tasks = (await _db.GetRecordsAsync<ContextTaskRow>(
                tasksSql, new { ProjectId = projectId, TeamId = teamId }))
            .ToList();

        // ── Derive current milestone ──────────────────────────────────────────
        // Priority: InProgress → Delayed → NotStarted (first by OrderIndex)
        var currentMs = milestones.FirstOrDefault(m => m.Status == "InProgress")
                     ?? milestones.FirstOrDefault(m => m.Status == "Delayed")
                     ?? milestones.FirstOrDefault(m => m.Status == "NotStarted");

        // ── Derive next task ──────────────────────────────────────────────────
        // Compare against the NORMALIZED status. Some legacy rows store
        // "Completed" instead of "Done" (see NormalizeTaskStatus); a raw
        // `Status != "Done"` test treats those as still open, which surfaced a
        // task finished months ago as the student's next deadline and
        // under-counted TasksDone. my-dashboard and my-tasks already normalize
        // at their read boundary — this endpoint was the one that did not.
        var nextTask = tasks.FirstOrDefault(t => NormalizeTaskStatus(t.Status) != "Done" && t.DueDate.HasValue)
                    ?? tasks.FirstOrDefault(t => NormalizeTaskStatus(t.Status) != "Done");

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
            MentorUserIds            = mentorUserIds,
            CurrentMilestoneTitle    = currentMs?.Title,
            CurrentMilestoneStatus   = NormalizeMilestoneStatus(currentMs?.Status),
            CurrentMilestoneDueDate  = currentMs?.DueDate,
            MilestonesCompleted      = milestones.Count(m => IsMilestoneCompleted(m.Status)),
            MilestonesTotal          = milestones.Count,
            TasksDone                = tasks.Count(t => NormalizeTaskStatus(t.Status) == "Done"),
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
                    -- IN ('Done','Completed'): legacy rows store 'Completed'
                    -- for the same terminal state (see NormalizeTaskStatus).
                    -- Matching only 'Done' under-counted milestone progress —
                    -- a milestone with both tasks finished reported 1/2.
                    COALESCE(SUM(CASE WHEN t.Status IN ('Done','Completed') THEN 1 ELSE 0 END), 0)
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
        //
        // IsUrgent is derived in SQL (single source of truth):
        //   - The effective milestone due date has passed (per-team override
        //     applied when present)
        //   - The task is mandatory
        //   - The task is still "open" (not Done/Completed/SubmittedToMentor,
        //     not closed, no existing submission)
        // Same expression is reusable by the future project-health layer.
        const string tasksSql = @"
            SELECT  pm.Id              AS ProjectMilestoneId,
                    mt.Title           AS MilestoneTitle,
                    mt.OrderIndex      AS MilestoneOrderIndex,
                    pm.Status          AS MilestoneStatus,
                    aym.OpenDate       AS MilestoneOpenDate,
                    COALESCE(o.OverrideDueDate, aym.DueDate)   AS MilestoneDueDate,
                    aym.CloseDate      AS MilestoneCloseDate,
                    CASE
                        WHEN (aym.OpenDate  IS NULL OR date(aym.OpenDate)  <= date('now'))
                         AND (aym.CloseDate IS NULL OR date(aym.CloseDate) >= date('now'))
                        THEN 1 ELSE 0
                    END                AS MilestoneIsCurrentlyOpen,
                    t.Id               AS TaskId,
                    t.Title            AS TaskTitle,
                    t.Status           AS TaskStatus,
                    t.TaskType,
                    t.IsMandatory,
                    -- Effective task DueDate, per the per-team override priority:
                    --   1. TeamTaskDueDateOverrides       (per-task override)
                    --   2. TeamMilestoneDueDateOverrides  (per-milestone fallback)
                    --   3. Tasks.DueDate                  (global)
                    -- Globals are never mutated; the override tables are
                    -- written by the extension-decision flow only.
                    COALESCE(tto.OverrideDueDate, o.OverrideDueDate, t.DueDate)
                                       AS TaskDueDate,
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
                     ORDER  BY s.Id DESC LIMIT 1) AS LatestSubmittedAt,
                    CASE
                        WHEN t.IsMandatory  = 1
                         AND (t.Status IS NULL OR t.Status NOT IN ('Done','Completed','SubmittedToMentor'))
                         AND t.ClosedAt IS NULL
                         AND NOT EXISTS (SELECT 1 FROM TaskSubmissions s WHERE s.TaskId = t.Id)
                         -- Urgency uses the effective task date (same priority chain).
                         AND date(COALESCE(tto.OverrideDueDate, o.OverrideDueDate, t.DueDate)) < date('now')
                        THEN 1 ELSE 0
                    END AS IsUrgent
            FROM    ProjectMilestones       pm
            JOIN    AcademicYearMilestones  aym ON pm.AcademicYearMilestoneId = aym.Id
            JOIN    MilestoneTemplates      mt  ON aym.MilestoneTemplateId    = mt.Id
            JOIN    Tasks                   t   ON t.ProjectMilestoneId = pm.Id
                                               AND t.ProjectId          = @ProjectId
            LEFT JOIN Users                 u   ON t.AssignedToUserId   = u.Id
            LEFT JOIN TeamMilestoneDueDateOverrides o
                                                ON o.TeamId             = @TeamId
                                               AND o.ProjectMilestoneId = pm.Id
            LEFT JOIN TeamTaskDueDateOverrides tto
                                                ON tto.TeamId           = @TeamId
                                               AND tto.TaskId           = t.Id
            WHERE   pm.ProjectId = @ProjectId
            ORDER   BY mt.OrderIndex, COALESCE(tto.OverrideDueDate, o.OverrideDueDate, t.DueDate)";

        var flatRows = (await _db.GetRecordsAsync<TaskFlatRow>(
            tasksSql, new { ProjectId = projectId, TeamId = projectRow.TeamId })).ToList();

        // ── 2b. Milestones with no tasks attached yet ─────────────────────────
        // The tasks query above uses INNER JOIN Tasks, so a milestone the
        // staff defined but didn't fill with tasks would be silently dropped.
        // We surface them here so the visibility rule
        //   visible = IsCurrentlyOpen OR has open tasks
        // can decide whether to render them. Empty milestones get an empty
        // Tasks list and a server-derived IsCurrentlyOpen flag.
        const string emptyMilestonesSql = @"
            SELECT  pm.Id              AS ProjectMilestoneId,
                    mt.Title           AS MilestoneTitle,
                    mt.OrderIndex      AS MilestoneOrderIndex,
                    pm.Status          AS MilestoneStatus,
                    aym.OpenDate       AS MilestoneOpenDate,
                    COALESCE(o.OverrideDueDate, aym.DueDate)   AS MilestoneDueDate,
                    aym.CloseDate      AS MilestoneCloseDate,
                    CASE
                        WHEN (aym.OpenDate  IS NULL OR date(aym.OpenDate)  <= date('now'))
                         AND (aym.CloseDate IS NULL OR date(aym.CloseDate) >= date('now'))
                        THEN 1 ELSE 0
                    END                AS MilestoneIsCurrentlyOpen
            FROM    ProjectMilestones       pm
            JOIN    AcademicYearMilestones  aym ON pm.AcademicYearMilestoneId = aym.Id
            JOIN    MilestoneTemplates      mt  ON aym.MilestoneTemplateId    = mt.Id
            LEFT JOIN TeamMilestoneDueDateOverrides o
                                                ON o.TeamId             = @TeamId
                                               AND o.ProjectMilestoneId = pm.Id
            WHERE   pm.ProjectId = @ProjectId
              AND   NOT EXISTS (
                        SELECT 1 FROM Tasks t
                        WHERE  t.ProjectMilestoneId = pm.Id
                          AND  t.ProjectId          = @ProjectId
                    )
            ORDER   BY mt.OrderIndex";

        var emptyMilestones = (await _db.GetRecordsAsync<EmptyMilestoneRow>(
            emptyMilestonesSql, new { ProjectId = projectId, TeamId = projectRow.TeamId }))?.ToList() ?? new();

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
                    IsUrgent               = r.IsUrgent == 1,
                }).ToList();

                return new TaskMilestoneGroupDto
                {
                    ProjectMilestoneId = g.Key,
                    MilestoneTitle     = g.First().MilestoneTitle,
                    OrderIndex         = g.First().MilestoneOrderIndex,
                    MilestoneStatus    = NormalizeMilestoneStatus(g.First().MilestoneStatus),
                    OpenDate           = g.First().MilestoneOpenDate,
                    DueDate            = g.First().MilestoneDueDate,
                    CloseDate          = g.First().MilestoneCloseDate,
                    IsCurrentlyOpen    = g.First().MilestoneIsCurrentlyOpen == 1,
                    DoneCount          = allTasks.Count(t => t.Status == "Done"),
                    TotalCount         = allTasks.Count,
                    Tasks              = allTasks,
                };
            })
            .OrderBy(g => g.OrderIndex)
            .ToList();

        // Append empty milestones (no tasks attached). Their visibility is
        // governed entirely by the date window — the active filter below
        // checks IsCurrentlyOpen for these.
        foreach (var em in emptyMilestones)
        {
            milestoneGroups.Add(new TaskMilestoneGroupDto
            {
                ProjectMilestoneId = em.ProjectMilestoneId,
                MilestoneTitle     = em.MilestoneTitle,
                OrderIndex         = em.MilestoneOrderIndex,
                MilestoneStatus    = NormalizeMilestoneStatus(em.MilestoneStatus),
                OpenDate           = em.MilestoneOpenDate,
                DueDate            = em.MilestoneDueDate,
                CloseDate          = em.MilestoneCloseDate,
                IsCurrentlyOpen    = em.MilestoneIsCurrentlyOpen == 1,
                DoneCount          = 0,
                TotalCount         = 0,
                Tasks              = new(),
            });
        }
        milestoneGroups = milestoneGroups.OrderBy(g => g.OrderIndex).ToList();

        // ── 4. Split into active / completed groups ───────────────────────────
        // Active groups (visibility rule, per the latest spec):
        //   - the milestone is currently open by date,    OR
        //   - it contains at least one non-Done task.
        // Group's Tasks contains only non-Done tasks. Empty-but-open milestones
        // pass through with an empty Tasks list so the team can still see the
        // milestone header.
        // Completed groups: milestones with at least one Done task.
        //                   Group's Tasks contains only Done tasks.
        var activeGroups = milestoneGroups
            .Where(g => g.IsCurrentlyOpen || g.Tasks.Any(t => t.Status != "Done"))
            .Select(g => new TaskMilestoneGroupDto
            {
                ProjectMilestoneId = g.ProjectMilestoneId,
                MilestoneTitle     = g.MilestoneTitle,
                OrderIndex         = g.OrderIndex,
                MilestoneStatus    = g.MilestoneStatus,
                OpenDate           = g.OpenDate,
                DueDate            = g.DueDate,
                CloseDate          = g.CloseDate,
                IsCurrentlyOpen    = g.IsCurrentlyOpen,
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
                OpenDate           = g.OpenDate,
                DueDate            = g.DueDate,
                CloseDate          = g.CloseDate,
                IsCurrentlyOpen    = g.IsCurrentlyOpen,
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

    // Statuses a student may set MANUALLY via PATCH /tasks/{id}/progress.
    // Everything past these two is owned by the mentor-review / Moodle
    // pipeline (SubmittedToMentor, ReturnedForRevision, RevisionSubmitted,
    // ApprovedForSubmission, Done, and any Moodle-confirmed state) and may
    // only be transitioned by TaskSubmissionsController's submit /
    // mentor-review / Moodle-confirm endpoints — never directly by students.
    private static readonly HashSet<string> StudentEditableTaskStatuses =
        new(StringComparer.Ordinal) { "Open", "InProgress" };

    // ── Team resolution helpers ───────────────────────────────────────────────

    // Used by StudentSubTasks endpoints: returns the user's team ID (LIMIT 1,
    // non-project-scoped — kept for backward compat with existing sub-task paths).
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

    // Used by TeamTasks endpoints: returns the user's active (non-draft) project
    // AND team together so all queries are project-scoped, preventing cross-team
    // access when a user somehow belongs to more than one team.
    private async Task<ProjectTeamRow?> GetProjectTeamForUserAsync(int userId)
    {
        const string sql = @"
            SELECT p.Id AS ProjectId, t.Id AS TeamId
            FROM   Projects    p
            JOIN   Teams       t  ON p.TeamId = t.Id
            JOIN   TeamMembers tm ON t.Id     = tm.TeamId
            WHERE  tm.UserId   = @UserId
              AND  tm.IsActive  = 1
              AND  COALESCE(p.AssignmentIsDraft, 0) = 0
            LIMIT  1";
        var rows = await _db.GetRecordsAsync<ProjectTeamRow>(sql, new { UserId = userId });
        return rows?.FirstOrDefault();
    }
    private sealed class ProjectTeamRow { public int ProjectId { get; set; } public int TeamId { get; set; } }
    private sealed class AssigneeNameRow { public string Name { get; set; } = ""; }

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
        public string? AcademicYear { get; set; }
        public int     TeamId       { get; set; }
    }

    private sealed class MilestoneRow
    {
        public int       ProjectMilestoneId { get; set; }
        public string    Title              { get; set; } = "";
        public int       OrderIndex         { get; set; }
        public string    Status             { get; set; } = "";
        public DateTime? OpenDate           { get; set; }
        public DateTime? DueDate            { get; set; }
        public DateTime? CloseDate          { get; set; }
        public DateTime? CompletedAt        { get; set; }
        public int       IsCurrentlyOpen    { get; set; }
    }

    private sealed class TaskRow
    {
        public int       Id                 { get; set; }
        public string    Title              { get; set; } = "";
        public string?   Description        { get; set; }
        public string    Status             { get; set; } = "";
        public DateTime? DueDate            { get; set; }
        public int?      ProjectMilestoneId { get; set; }
        public bool      IsSubmission       { get; set; }
        public string    AssignedToName     { get; set; } = "";
        public string?   LatestSubmissionStatus { get; set; }
        public string?   LatestMentorStatus     { get; set; }
        public DateTime? LatestSubmittedAt      { get; set; }
        public bool      LatestMoodleConfirmed  { get; set; }
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
        public int     TeamId            { get; set; }
        public string? TeamName          { get; set; }
        public string? TrackName         { get; set; }
        public string? StudentDetailsCsv { get; set; }
        public string? MentorDetailsCsv  { get; set; }
        public string? MentorUserIdsCsv  { get; set; }
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
        public DateTime? MilestoneOpenDate   { get; set; }
        public DateTime? MilestoneDueDate    { get; set; }
        public DateTime? MilestoneCloseDate  { get; set; }
        /// <summary>0/1 — derived in the SELECT.</summary>
        public int       MilestoneIsCurrentlyOpen { get; set; }
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
        /// <summary>0/1 — derived in the SELECT.</summary>
        public int       IsUrgent               { get; set; }
    }

    // GetTaskDetail needs the user's team to apply per-team override JOINs.
    private sealed class TaskDetailProjectRow
    {
        public int Id     { get; set; }
        public int TeamId { get; set; }
    }

    // Raw MoodleSubmittedAt stamps for a task's submissions, keyed by
    // submission Id. Queried separately (not via SQL COALESCE — see comment
    // at GetTaskDetail) and merged into CourseSubmittedAt in C#.
    private sealed class MoodleStampRow
    {
        public int      Id                { get; set; }
        public DateTime MoodleSubmittedAt { get; set; }
    }

    // Mirrors the columns selected by the "milestones with no tasks" SQL in
    // GetMyTasks. Kept private so the public DTO surface stays unchanged.
    private sealed class EmptyMilestoneRow
    {
        public int       ProjectMilestoneId       { get; set; }
        public string    MilestoneTitle           { get; set; } = "";
        public int       MilestoneOrderIndex      { get; set; }
        public string    MilestoneStatus          { get; set; } = "";
        public DateTime? MilestoneOpenDate        { get; set; }
        public DateTime? MilestoneDueDate         { get; set; }
        public DateTime? MilestoneCloseDate       { get; set; }
        public int       MilestoneIsCurrentlyOpen { get; set; }
    }

    // ── GET /api/projects/my-submission-tasks ────────────────────────────────
    // Returns all submission tasks for the authenticated student's project,
    // each with its latest submission state.
    // Used by the /submissions page and the SubmissionModal.
    [HttpGet("my-submission-tasks")]
    public async Task<IActionResult> GetMySubmissionTasks(int authUserId)
    {
        // ── 1. Resolve project + team (team needed for per-team override JOINs) ──
        const string projectSql = @"
            SELECT  p.Id, t.Id AS TeamId
            FROM    Projects     p
            JOIN    Teams        t   ON p.TeamId  = t.Id
            JOIN    TeamMembers  tm  ON t.Id       = tm.TeamId
            WHERE   tm.UserId  = @UserId
              AND   tm.IsActive = 1
            LIMIT 1";

        var projectRow = (await _db.GetRecordsAsync<TaskDetailProjectRow>(
                projectSql, new { UserId = authUserId }))
            .FirstOrDefault();

        if (projectRow is null)
            return Ok(Enumerable.Empty<StudentSubmissionTaskDto>());

        int projectId = projectRow.Id;
        int teamId    = projectRow.TeamId;

        // ── 2. Submission tasks with latest submission state ──────────────────
        // Correlated subqueries on TaskSubmissions give the latest row's
        // status and date without a GROUP BY complication. DueDate uses the
        // per-team override priority chain (task → milestone → global).
        const string sql = @"
            SELECT  t.Id                                   AS TaskId,
                    t.Title                                AS TaskTitle,
                    t.Description,
                    COALESCE(mt.Title, '')                 AS MilestoneTitle,
                    COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate) AS DueDate,
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
                    (SELECT COALESCE(s.MoodleSubmittedAt, s.CourseSubmittedAt)
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestCourseSubmittedAt,
                    (SELECT CASE
                              WHEN u.Id IS NULL THEN ''
                              ELSE TRIM(COALESCE(u.FirstName,'') || ' ' || COALESCE(u.LastName,''))
                            END
                     FROM   TaskSubmissions s
                     LEFT JOIN users u ON u.Id = s.MoodleSubmittedByUserId
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestMoodleSubmittedByName,
                    (SELECT s.SubmittedAt
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestSubmittedAt,
                    (SELECT s.DriveUrl
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestDriveUrl
            FROM    Tasks                    t
            LEFT JOIN ProjectMilestones      pm  ON pm.Id  = t.ProjectMilestoneId
            LEFT JOIN AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            LEFT JOIN MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = @TeamId AND mo.ProjectMilestoneId = pm.Id
            LEFT JOIN TeamTaskDueDateOverrides tto
                            ON tto.TeamId = @TeamId AND tto.TaskId = t.Id
            WHERE   t.ProjectId    = @ProjectId
              AND   t.IsSubmission = 1
            ORDER   BY COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate) NULLS LAST, t.Id";

        // NOTE: lecturer-feedback projection removed from this list endpoint —
        // the CASE-WHEN expressions on top of correlated subqueries were
        // making Microsoft.Data.Sqlite hand Dapper rows it couldn't materialize,
        // and DbRepository.GetRecordsAsync swallows the exception silently,
        // returning null. The page rendered "no submission tasks" instead of
        // the actual list. The student-side feedback view continues to work
        // through GET /api/projects/tasks/{id}/detail (which already returns
        // ReviewerFeedback per submission round).
        var rows = await _db.GetRecordsAsync<StudentSubmissionTaskDto>(
            sql, new { ProjectId = projectId, TeamId = teamId });

        return Ok(rows ?? Enumerable.Empty<StudentSubmissionTaskDto>());
    }

    // ── GET /api/projects/my-submission-tasks/{taskId} ───────────────────────
    // Returns a single submission task with its latest submission state.
    // Used by the SubmissionModal when it opens for a specific task.
    [HttpGet("my-submission-tasks/{taskId:int}")]
    public async Task<IActionResult> GetMySubmissionTask(int taskId, int authUserId)
    {
        // ── 1. Resolve project + team (team needed for per-team override JOINs) ──
        const string projectSql = @"
            SELECT  p.Id, t.Id AS TeamId
            FROM    Projects     p
            JOIN    Teams        t   ON p.TeamId  = t.Id
            JOIN    TeamMembers  tm  ON t.Id       = tm.TeamId
            WHERE   tm.UserId  = @UserId
              AND   tm.IsActive = 1
            LIMIT 1";

        var projectRow = (await _db.GetRecordsAsync<TaskDetailProjectRow>(
                projectSql, new { UserId = authUserId }))
            .FirstOrDefault();

        if (projectRow is null) return NotFound("פרויקט לא נמצא");

        int projectId = projectRow.Id;
        int teamId    = projectRow.TeamId;

        // ── 2. Single task (must belong to the user's project) ────────────────
        const string sql = @"
            SELECT  t.Id                                   AS TaskId,
                    t.Title                                AS TaskTitle,
                    t.Description,
                    COALESCE(mt.Title, '')                 AS MilestoneTitle,
                    COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate) AS DueDate,
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
                    (SELECT COALESCE(s.MoodleSubmittedAt, s.CourseSubmittedAt)
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestCourseSubmittedAt,
                    (SELECT CASE
                              WHEN u.Id IS NULL THEN ''
                              ELSE TRIM(COALESCE(u.FirstName,'') || ' ' || COALESCE(u.LastName,''))
                            END
                     FROM   TaskSubmissions s
                     LEFT JOIN users u ON u.Id = s.MoodleSubmittedByUserId
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestMoodleSubmittedByName,
                    (SELECT s.SubmittedAt
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestSubmittedAt,
                    (SELECT s.DriveUrl
                     FROM   TaskSubmissions s
                     WHERE  s.TaskId = t.Id
                     ORDER  BY s.Id DESC LIMIT 1)          AS LatestDriveUrl
            FROM    Tasks                    t
            LEFT JOIN ProjectMilestones      pm  ON pm.Id  = t.ProjectMilestoneId
            LEFT JOIN AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            LEFT JOIN MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = @TeamId AND mo.ProjectMilestoneId = pm.Id
            LEFT JOIN TeamTaskDueDateOverrides tto
                            ON tto.TeamId = @TeamId AND tto.TaskId = t.Id
            WHERE   t.Id         = @TaskId
              AND   t.ProjectId  = @ProjectId
              AND   t.IsSubmission = 1";

        // NOTE: lecturer-feedback projection removed (same reason as the list
        // endpoint above). Use GET /api/projects/tasks/{id}/detail for the
        // full submission history including ReviewerFeedback per round.
        var row = (await _db.GetRecordsAsync<StudentSubmissionTaskDto>(
                sql, new { TaskId = taskId, ProjectId = projectId, TeamId = teamId }))
            ?.FirstOrDefault();

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
        // ── 1. Resolve THE TASK'S project + team, gated on membership ─────
        //
        // WHY THIS IS KEYED ON THE TASK AND NOT ON THE USER. It used to ask
        // "which project is this user on?" with its own query — `FROM Projects
        // JOIN Teams JOIN TeamMembers WHERE UserId = @UserId LIMIT 1` — and
        // then require the task to belong to whatever that returned. Three
        // things were wrong with it, all of them producing the same symptom:
        // a task the dashboard had just rendered answering 404 here, which the
        // client can only surface as "אירעה שגיאה בטעינת פרטי המשימה".
        //
        //   * LIMIT 1 with no ORDER BY over a set that can hold more than one
        //     row. A student on two active teams (the seeded cohort has two)
        //     got an arbitrary pick, and my-dashboard's own resolution — a
        //     DIFFERENT query, with an extra JOIN — was free to pick the other
        //     one. Two endpoints, two answers, and the task guard below then
        //     rejected a perfectly legitimate task.
        //   * It did not filter draft assignments, which my-dashboard does
        //     (`COALESCE(p.AssignmentIsDraft,0) = 0`). So this endpoint could
        //     resolve to a project the student is not yet supposed to see, and
        //     serve task detail out of it.
        //   * It answered a question nobody asked. The access rule is "is this
        //     task's project one the caller is on?" — asking "which single
        //     project is the caller on?" first is a lossy way to get there.
        //
        // Resolving from the TASK removes the ambiguity entirely: one task has
        // one project, which has one team, and the JOIN onto TeamMembers is
        // what enforces the scoping — a task outside every team the caller
        // belongs to simply returns no row. A student on two teams can now open
        // tasks from either, which is what the dashboards already show them.
        // The team is still needed for the per-team override JOINs below, and
        // it is now provably the team that owns this task rather than whichever
        // team the old LIMIT 1 happened to surface.
        const string scopeSql = @"
            SELECT  p.Id     AS Id,
                    tm.TeamId AS TeamId
            FROM    Tasks       tk
            JOIN    Projects    p  ON p.Id      = tk.ProjectId
            JOIN    TeamMembers tm ON tm.TeamId = p.TeamId
            WHERE   tk.Id      = @TaskId
              AND   tm.UserId  = @UserId
              AND   tm.IsActive = 1
              AND   COALESCE(p.AssignmentIsDraft, 0) = 0
            LIMIT 1";

        // GetRecordsAsync swallows a failed query and answers null, so every
        // result here is null-coalesced before LINQ touches it — the same guard
        // GetMyDashboard documents. Without it a transient failure became a
        // NullReferenceException, i.e. a 500, i.e. this modal's generic error
        // with nothing in it to say what went wrong.
        var scopeRow = (await _db.GetRecordsAsync<TaskDetailProjectRow>(
                scopeSql, new { TaskId = taskId, UserId = authUserId })
            ?? Enumerable.Empty<TaskDetailProjectRow>())
            .FirstOrDefault();

        if (scopeRow is null) return NotFound("המשימה לא נמצאה");
        int projectId = scopeRow.Id;
        int teamId    = scopeRow.TeamId;

        // ── 2. Fetch task (must belong to the student's project) ─────────────
        // DueDate uses the per-team override priority chain so the team sees
        // its effective due date. Globals (Tasks.DueDate) are never mutated.
        const string taskSql = @"
            SELECT  t.Id,
                    t.Title,
                    t.Description,
                    COALESCE(mt.Title, '')  AS MilestoneTitle,
                    t.CreatedAt            AS OpenDate,
                    COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate) AS DueDate,
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
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                                                 ON mo.TeamId             = @TeamId
                                                AND mo.ProjectMilestoneId = pm.Id
            LEFT JOIN TeamTaskDueDateOverrides tto
                                                 ON tto.TeamId = @TeamId
                                                AND tto.TaskId = t.Id
            WHERE   t.Id        = @TaskId
              AND   t.ProjectId = @ProjectId";

        var task = (await _db.GetRecordsAsync<TaskDetailDto>(
                taskSql, new { TaskId = taskId, ProjectId = projectId, TeamId = teamId })
            ?? Enumerable.Empty<TaskDetailDto>())
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
                        s.CourseSubmittedAt,
                        s.DriveUrl,
                        COALESCE(s.ReviewStatus, 'PendingReview')   AS ReviewStatus,
                        COALESCE(s.IsFeedbackPublished, 0)          AS IsFeedbackPublished,
                        s.FeedbackPublishedAt
                FROM    TaskSubmissions s
                WHERE   s.TaskId = @TaskId
                ORDER   BY s.Id ASC";

            var subs = (await _db.GetRecordsAsync<SubmissionHistoryItemDto>(
                    subsSql, new { TaskId = taskId })
                ?? Enumerable.Empty<SubmissionHistoryItemDto>())
                .ToList();

            if (subs.Count > 0)
            {
                // Merge the new MoodleSubmittedAt column (post-2026-05-19
                // refactor) into the legacy CourseSubmittedAt field so the
                // client always sees one unified "submitted to Moodle"
                // timestamp. NOTE: this is intentionally done in C#, not via
                // SQL COALESCE(s.MoodleSubmittedAt, s.CourseSubmittedAt) — the
                // Sqlite/Dapper driver loses the DATETIME column-type metadata
                // for computed expressions, so a COALESCE result gets handed
                // to Dapper as a raw string and throws
                // "InvalidCastException: Unable to cast ... String ... to
                // Nullable<DateTime>" while mapping to DateTime? properties.
                const string moodleSql = @"
                    SELECT  Id, MoodleSubmittedAt
                    FROM    TaskSubmissions
                    WHERE   TaskId = @TaskId AND MoodleSubmittedAt IS NOT NULL";

                var moodleById = (await _db.GetRecordsAsync<MoodleStampRow>(
                        moodleSql, new { TaskId = taskId })
                    ?? Enumerable.Empty<MoodleStampRow>())
                    .ToDictionary(r => r.Id, r => r.MoodleSubmittedAt);

                foreach (var sub in subs)
                    if (moodleById.TryGetValue(sub.Id, out var moodleAt))
                        sub.CourseSubmittedAt = moodleAt;

                if (task.LatestSubmissionId.HasValue &&
                    moodleById.TryGetValue(task.LatestSubmissionId.Value, out var latestMoodleAt))
                    task.LatestCourseSubmittedAt = latestMoodleAt;
            }

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
                        filesSql, new { TaskId = taskId })
                    ?? Enumerable.Empty<TaskSubmissionFileDto>())
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
    // Allows the authenticated student to manually set their personal progress
    // on a task that belongs to their project — but ONLY between the two
    // student-owned states "Open" and "InProgress", and only BEFORE the task
    // has entered the mentor-review pipeline (i.e. before the first
    // TaskSubmissions row for it exists).
    //
    // Once a submission exists, the task's status is owned end-to-end by the
    // mentor-review / Moodle-confirmation workflow (SubmittedToMentor →
    // ReturnedForRevision/RevisionSubmitted → ApprovedForSubmission → Done /
    // Moodle-confirmed) and the student can no longer change it manually —
    // those transitions happen exclusively inside TaskSubmissionsController.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPatch("tasks/{taskId:int}/progress")]
    public async Task<IActionResult> UpdateTaskProgress(
        int taskId, [FromBody] UpdateTaskProgressRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.Status))
            return BadRequest("סטטוס לא תקין.");

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

        // Fetch IsSubmission so we can apply different rules per task type.
        const string taskTypeSql =
            "SELECT IsSubmission FROM Tasks WHERE Id = @TaskId AND ProjectId = @ProjectId LIMIT 1";
        var isSubmissionRows = await _db.GetRecordsAsync<bool>(
            taskTypeSql, new { TaskId = taskId, ProjectId = projectIdRow.Id });
        if (!isSubmissionRows.Any()) return NotFound("משימה לא נמצאה");
        bool isSubmission = isSubmissionRows.First();

        if (isSubmission)
        {
            // Submission Tasks: only Open ↔ InProgress, and only before the
            // mentor pipeline begins (no TaskSubmissions rows yet).
            if (!StudentEditableTaskStatuses.Contains(req.Status))
                return BadRequest(
                    "סטטוס לא תקין. ניתן לעדכן ידנית רק בין \"ממתין לביצוע\" (Open) ו-\"בעבודה\" " +
                    "(InProgress) — שאר הסטטוסים מנוהלים אוטומטית ע״י תהליך בדיקת המנחה ו-Moodle");

            const string submissionCountSql =
                "SELECT COUNT(1) FROM TaskSubmissions WHERE TaskId = @TaskId";
            int submissionCount = (await _db.GetRecordsAsync<int>(
                    submissionCountSql, new { TaskId = taskId }))
                .FirstOrDefault();

            if (submissionCount > 0)
                return Conflict(
                    "המשימה כבר הועברה לבדיקת מנחה — הסטטוס שלה מנוהל כעת אוטומטית " +
                    "ע״י תהליך בדיקת המנחה ואישור ה-Moodle, ולא ניתן לשנותו ידנית");

            const string updateSql = @"
                UPDATE Tasks
                SET    Status   = @Status,
                       ClosedAt = NULL
                WHERE  Id        = @TaskId
                  AND  ProjectId = @ProjectId
                  AND  (Status IS NULL OR Status IN ('Open', 'InProgress'))";

            int affected = await _db.SaveDataAsync(updateSql, new
            {
                req.Status,
                TaskId    = taskId,
                ProjectId = projectIdRow.Id,
            });

            if (affected == 0)
                return Conflict("לא ניתן לעדכן את הסטטוס באופן ידני עבור משימה זו במצבה הנוכחי");

            return Ok();
        }
        else
        {
            // Activity Tasks: students may freely toggle Open / InProgress / Done.
            // No submission pipeline exists for these tasks.
            var activityAllowed = new HashSet<string>(StringComparer.Ordinal)
                { "Open", "InProgress", "Done" };

            if (!activityAllowed.Contains(req.Status))
                return BadRequest("סטטוס לא תקין עבור משימת פעילות.");

            // No status guard in WHERE — student may transition freely between all three.
            // ClosedAt is stamped when Done and cleared otherwise.
            const string updateActivitySql = @"
                UPDATE Tasks
                SET    Status   = @Status,
                       ClosedAt = CASE WHEN @Status = 'Done' THEN datetime('now') ELSE NULL END
                WHERE  Id           = @TaskId
                  AND  ProjectId    = @ProjectId
                  AND  IsSubmission = 0";

            int affected = await _db.SaveDataAsync(updateActivitySql, new
            {
                req.Status,
                TaskId    = taskId,
                ProjectId = projectIdRow.Id,
            });

            if (affected == 0)
                return Conflict("לא ניתן לעדכן את הסטטוס עבור משימה זו");

            return Ok();
        }
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
        // Title and Description resolve through ProjectTeamProfile: the team's
        // own display name / description wins where it exists, and the catalog
        // value is the fallback. See the ProjectTeamProfile block in
        // DatabaseMigrator for why the student's edit is not written into the
        // Projects row itself (Airtable sync overwrites those two columns).
        // Interpolated (and therefore not const) for one reason: LogoContainer.
        // Repeating the folder name as a literal inside the SQL would be a
        // second place to edit if the container ever moves.
        string sql = $@"
            SELECT  p.Id,
                    p.ProjectNumber,
                    COALESCE(NULLIF(TRIM(ptp.DisplayTitle), ''), p.Title)       AS Title,
                    pt.Name   AS ProjectType,
                    ay.Name   AS AcademicYear,
                    COALESCE(NULLIF(TRIM(ptp.Description),  ''), p.Description) AS Description,
                    p.Goals,
                    p.TargetAudience,
                    p.OrganizationName,
                    p.OrganizationType,
                    p.ContactPerson,
                    p.ContactRole,
                    p.ContactEmail,
                    p.ContactPhone,
                    p.ProjectTopic,
                    p.Contents,
                    -- The stored filename becomes a URL here and nowhere else,
                    -- so the container stays a server-side detail the client
                    -- never has to know. Concatenating with NULL yields NULL in
                    -- SQLite, so a missing or blank LogoPath falls out as a null
                    -- LogoUrl on its own -- no row and no upload both mean the
                    -- team has no logo.
                    '/{LogoContainer}/' || NULLIF(TRIM(ptp.LogoPath), '')   AS LogoUrl
            FROM    Projects      p
            JOIN    ProjectTypes  pt  ON p.ProjectTypeId  = pt.Id
            JOIN    AcademicYears ay  ON p.AcademicYearId = ay.Id
            JOIN    Teams         t   ON p.TeamId         = t.Id
            JOIN    TeamMembers   tm  ON t.Id             = tm.TeamId
            LEFT JOIN ProjectTeamProfile ptp ON ptp.ProjectId = p.Id
            WHERE   tm.UserId   = @UserId
              AND   tm.IsActive = 1
            LIMIT 1";

        var row = (await _db.GetRecordsAsync<StudentProjectDetailsDto>(
                sql, new { UserId = authUserId }))
            .FirstOrDefault();

        if (row is null) return NotFound("פרויקט לא נמצא");
        return Ok(row);
    }

    // ── PUT /api/projects/my-project ─────────────────────────────────────────
    // Updates the display name + description of the authenticated student's own
    // project. This is the only project write a student can make.
    //
    // The values are stored in ProjectTeamProfile, NOT in the Projects row:
    // Projects.Title / Projects.Description are catalog fields that
    // AirtableService rewrites on every sync, and that lecturers and mentors
    // read. See the ProjectTeamProfile block in DatabaseMigrator.
    //
    // Clearing a field (empty / whitespace) stores NULL, which makes the read
    // queries fall back to the catalog value — so a team can always get back to
    // the official title without an "undo" of its own.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("my-project")]
    public async Task<IActionResult> UpdateMyProject(
        [FromBody] UpdateMyProjectRequest req, int authUserId)
    {
        if (req is null) return BadRequest();

        var title       = (req.Title       ?? "").Trim();
        var description = (req.Description ?? "").Trim();

        // Length guards mirror what the fields realistically hold; the client
        // enforces the same numbers so an over-long value never round-trips.
        if (title.Length > MaxProjectTitleLength)
            return BadRequest($"שם הפרויקט ארוך מדי — עד {MaxProjectTitleLength} תווים");

        if (description.Length > MaxProjectDescriptionLength)
            return BadRequest($"תיאור הפרויקט ארוך מדי — עד {MaxProjectDescriptionLength} תווים");

        var projectId = await GetProjectIdForUserAsync(authUserId);
        if (projectId is null) return NotFound("פרויקט לא נמצא");

        // One row per project, so the write is an upsert on the primary key.
        const string sql = @"
            INSERT INTO ProjectTeamProfile
                        (ProjectId, DisplayTitle, Description, UpdatedAt, UpdatedByUserId)
            VALUES      (@ProjectId, @Title, @Description, datetime('now'), @UserId)
            ON CONFLICT(ProjectId) DO UPDATE SET
                        DisplayTitle    = excluded.DisplayTitle,
                        Description     = excluded.Description,
                        UpdatedAt       = excluded.UpdatedAt,
                        UpdatedByUserId = excluded.UpdatedByUserId";

        await _db.SaveDataAsync(sql, new
        {
            ProjectId   = projectId.Value,
            Title       = title.Length       == 0 ? null : title,
            Description = description.Length == 0 ? null : description,
            UserId      = authUserId,
        });

        return NoContent();
    }

    private const int MaxProjectTitleLength       = 120;
    private const int MaxProjectDescriptionLength = 2000;

    // ── PUT /api/projects/my-project/logo ────────────────────────────────────
    // Uploads or replaces the team's project logo.
    //
    // Deliberately a SEPARATE endpoint from PUT my-project rather than another
    // field on it: the text save is a form submit the student presses "שמירת
    // שינויים" for, and the image write happens the moment a file is chosen.
    // Folding a multi-megabyte base64 payload into the text save would also
    // mean re-uploading the logo on every rename.
    //
    // The whole shape is StudentController.UpdateMyAvatar's, because it is the
    // same job: validate the extension, hand the bytes to FilesManage.SaveFile
    // (which resizes and names the file), store the bare filename, then delete
    // the file it replaced — in that order, so a failed save never destroys the
    // logo that is still on screen.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("my-project/logo")]
    public async Task<IActionResult> UpdateMyProjectLogo(
        [FromBody] UploadProjectLogoRequest req, int authUserId)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.ImageBase64))
            return BadRequest("לא התקבל קובץ תמונה");

        var ext = (req.Extension ?? "").ToLowerInvariant().TrimStart('.');
        if (!AllowedLogoExts.Contains(ext))
            return BadRequest("סוג הקובץ אינו נתמך. נתמכים: JPG, PNG, WEBP");

        var projectId = await GetProjectIdForUserAsync(authUserId);
        if (projectId is null) return NotFound("פרויקט לא נמצא");

        var existing = (await _db.GetRecordsAsync<ProjectLogoPathRow>(
            "SELECT LogoPath FROM ProjectTeamProfile WHERE ProjectId = @ProjectId",
            new { ProjectId = projectId.Value }))?.FirstOrDefault();

        string newFileName;
        try
        {
            newFileName = await _files.SaveFile(req.ImageBase64, ext, LogoContainer);
        }
        catch
        {
            return StatusCode(500, "שמירת התמונה נכשלה");
        }

        // Upsert, because a team that has never edited its name has no row yet
        // and choosing a logo must not require saving the text form first.
        const string sql = @"
            INSERT INTO ProjectTeamProfile
                        (ProjectId, LogoPath, UpdatedAt, UpdatedByUserId)
            VALUES      (@ProjectId, @LogoPath, datetime('now'), @UserId)
            ON CONFLICT(ProjectId) DO UPDATE SET
                        LogoPath        = excluded.LogoPath,
                        UpdatedAt       = excluded.UpdatedAt,
                        UpdatedByUserId = excluded.UpdatedByUserId";

        await _db.SaveDataAsync(sql, new
        {
            ProjectId = projectId.Value,
            LogoPath  = newFileName,
            UserId    = authUserId,
        });

        if (!string.IsNullOrWhiteSpace(existing?.LogoPath))
            _files.DeleteFile(existing.LogoPath, LogoContainer);

        return Ok(new { url = $"/{LogoContainer}/{newFileName}" });
    }

    // ── DELETE /api/projects/my-project/logo ─────────────────────────────────
    // Removes the team's logo and falls the card back to its placeholder.
    // Nulls the column BEFORE deleting the file, so a delete that throws leaves
    // a missing image rather than a row pointing at a file that is gone.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("my-project/logo")]
    public async Task<IActionResult> RemoveMyProjectLogo(int authUserId)
    {
        var projectId = await GetProjectIdForUserAsync(authUserId);
        if (projectId is null) return NotFound("פרויקט לא נמצא");

        var existing = (await _db.GetRecordsAsync<ProjectLogoPathRow>(
            "SELECT LogoPath FROM ProjectTeamProfile WHERE ProjectId = @ProjectId",
            new { ProjectId = projectId.Value }))?.FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(existing?.LogoPath))
        {
            await _db.SaveDataAsync(
                @"UPDATE ProjectTeamProfile
                  SET    LogoPath = NULL, UpdatedAt = datetime('now'), UpdatedByUserId = @UserId
                  WHERE  ProjectId = @ProjectId",
                new { ProjectId = projectId.Value, UserId = authUserId });

            _files.DeleteFile(existing.LogoPath, LogoContainer);
        }

        return NoContent();
    }

    private sealed class ProjectLogoPathRow
    {
        public string? LogoPath { get; set; }
    }

    // ── GET /api/projects/my-resources ───────────────────────────────────────
    // The team's own links (משאבי הפרויקט), newest last so the grid keeps a
    // stable reading order as items are added.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-resources")]
    public async Task<IActionResult> GetMyResources(int authUserId)
    {
        var ctx = await GetProjectTeamForUserAsync(authUserId);
        if (ctx is null) return Ok(Enumerable.Empty<ProjectResourceDto>());

        const string sql = @"
            SELECT  Id, Label, Url, DeliverableKey
            FROM    ProjectResources
            WHERE   ProjectId = @ProjectId
            ORDER   BY Id";

        var rows = await _db.GetRecordsAsync<ProjectResourceDto>(
            sql, new { ProjectId = ctx.ProjectId });

        return Ok(rows ?? Enumerable.Empty<ProjectResourceDto>());
    }

    // ── POST /api/projects/my-resources ──────────────────────────────────────
    // Adds a link to the caller's own project.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("my-resources")]
    public async Task<IActionResult> CreateMyResource(
        [FromBody] CreateProjectResourceRequest req, int authUserId)
    {
        if (req is null) return BadRequest();

        var label = (req.Label ?? "").Trim();
        var url   = (req.Url   ?? "").Trim();

        if (label.Length == 0) return BadRequest("שם המשאב לא יכול להיות ריק");
        if (label.Length > MaxResourceLabelLength)
            return BadRequest($"שם המשאב ארוך מדי — עד {MaxResourceLabelLength} תווים");

        if (!TryNormalizeResourceUrl(url, out var safeUrl))
            return BadRequest("הקישור אינו תקין. יש להזין כתובת שמתחילה ב-http או ב-https");

        if (!TryNormalizeDeliverableKey(req.DeliverableKey, out var deliverableKey))
            return BadRequest("מזהה התוצר אינו תקין");

        var ctx = await GetProjectTeamForUserAsync(authUserId);
        if (ctx is null) return NotFound("פרויקט לא נמצא");

        const string sql = @"
            INSERT INTO ProjectResources (ProjectId, TeamId, Label, Url, DeliverableKey, CreatedByUserId)
            VALUES (@ProjectId, @TeamId, @Label, @Url, @DeliverableKey, @UserId)";

        int newId = await _db.InsertReturnIdAsync(sql, new
        {
            ctx.ProjectId,
            ctx.TeamId,
            Label          = label,
            Url            = safeUrl,
            DeliverableKey = deliverableKey,
            UserId         = authUserId,
        });

        return Ok(new ProjectResourceDto
        {
            Id             = newId,
            Label          = label,
            Url            = safeUrl,
            DeliverableKey = deliverableKey,
        });
    }

    // ── PUT /api/projects/my-resources/{id} ──────────────────────────────────
    // Edits a link the caller's team owns — the label, the URL, or both.
    //
    // Same validation as the POST above, deliberately: a resource must not be
    // able to become unsafe by being edited into something the create path
    // would have refused. The ProjectId predicate in the WHERE clause is the
    // authorization — a row belonging to another team simply does not match.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("my-resources/{id:int}")]
    public async Task<IActionResult> UpdateMyResource(
        int id, [FromBody] CreateProjectResourceRequest req, int authUserId)
    {
        if (req is null) return BadRequest();

        var label = (req.Label ?? "").Trim();
        var url   = (req.Url   ?? "").Trim();

        if (label.Length == 0) return BadRequest("שם המשאב לא יכול להיות ריק");
        if (label.Length > MaxResourceLabelLength)
            return BadRequest($"שם המשאב ארוך מדי — עד {MaxResourceLabelLength} תווים");

        if (!TryNormalizeResourceUrl(url, out var safeUrl))
            return BadRequest("הקישור אינו תקין. יש להזין כתובת שמתחילה ב-http או ב-https");

        if (!TryNormalizeDeliverableKey(req.DeliverableKey, out var deliverableKey))
            return BadRequest("מזהה התוצר אינו תקין");

        var ctx = await GetProjectTeamForUserAsync(authUserId);
        if (ctx is null) return NotFound("פרויקט לא נמצא");

        // DeliverableKey is written on every edit, including when it is being
        // CLEARED (null): the association is part of the resource, so an edit
        // that removes it has to persist that, and the deliverable it used to
        // belong to has to stop counting it as evidence of work.
        const string sql = @"
            UPDATE ProjectResources
            SET    Label          = @Label,
                   Url            = @Url,
                   DeliverableKey = @DeliverableKey
            WHERE  Id        = @Id
              AND  ProjectId = @ProjectId";

        int affected = await _db.SaveDataAsync(sql, new
        {
            Id = id,
            ctx.ProjectId,
            Label          = label,
            Url            = safeUrl,
            DeliverableKey = deliverableKey,
        });

        if (affected == 0) return NotFound("המשאב לא נמצא");

        return Ok(new ProjectResourceDto
        {
            Id             = id,
            Label          = label,
            Url            = safeUrl,
            DeliverableKey = deliverableKey,
        });
    }

    // ── DELETE /api/projects/my-resources/{id} ───────────────────────────────
    // Removes a link. The ProjectId predicate is the authorization: a row that
    // belongs to another team simply does not match.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("my-resources/{id:int}")]
    public async Task<IActionResult> DeleteMyResource(int id, int authUserId)
    {
        var ctx = await GetProjectTeamForUserAsync(authUserId);
        if (ctx is null) return NotFound("פרויקט לא נמצא");

        int affected = await _db.SaveDataAsync(
            "DELETE FROM ProjectResources WHERE Id = @Id AND ProjectId = @ProjectId",
            new { Id = id, ctx.ProjectId });

        if (affected == 0) return NotFound("המשאב לא נמצא");
        return NoContent();
    }

    // ── GET /api/projects/my-submission-progress ─────────────────────────────
    // The team's status per submission category (תוצרי ההגשה). Only categories
    // the team has actually touched have a row; everything else is
    // "NotStarted" by absence, which is why no seeding is needed when the
    // catalog gains an entry.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-submission-progress")]
    public async Task<IActionResult> GetMySubmissionProgress(int authUserId)
    {
        var ctx = await GetProjectTeamForUserAsync(authUserId);
        if (ctx is null) return Ok(Enumerable.Empty<SubmissionStatusDto>());

        const string sql = @"
            SELECT  DeliverableKey, Status
            FROM    ProjectSubmissionStatuses
            WHERE   ProjectId = @ProjectId";

        var rows = await _db.GetRecordsAsync<SubmissionStatusDto>(
            sql, new { ProjectId = ctx.ProjectId });

        return Ok(rows ?? Enumerable.Empty<SubmissionStatusDto>());
    }

    // ── PUT /api/projects/my-submission-progress/{deliverableKey} ────────────
    // Sets one category's status for the caller's team.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("my-submission-progress/{deliverableKey}")]
    public async Task<IActionResult> UpdateMySubmissionProgress(
        string deliverableKey, [FromBody] UpdateDeliverableStatusRequest req, int authUserId)
    {
        if (req is null) return BadRequest();

        var key = (deliverableKey ?? "").Trim();
        if (key.Length == 0 || key.Length > MaxDeliverableKeyLength)
            return BadRequest("מזהה התוצר אינו תקין");

        if (!SubmissionStatusValues.IsValid(req.Status))
            return BadRequest("מצב לא חוקי");

        var ctx = await GetProjectTeamForUserAsync(authUserId);
        if (ctx is null) return NotFound("פרויקט לא נמצא");

        const string sql = @"
            INSERT INTO ProjectSubmissionStatuses
                        (ProjectId, DeliverableKey, Status, UpdatedAt, UpdatedByUserId)
            VALUES      (@ProjectId, @Key, @Status, datetime('now'), @UserId)
            ON CONFLICT(ProjectId, DeliverableKey) DO UPDATE SET
                        Status          = excluded.Status,
                        UpdatedAt       = excluded.UpdatedAt,
                        UpdatedByUserId = excluded.UpdatedByUserId";

        await _db.SaveDataAsync(sql, new
        {
            ctx.ProjectId,
            Key    = key,
            req.Status,
            UserId = authUserId,
        });

        return NoContent();
    }

    private const int MaxDeliverableKeyLength = 60;

    private const int MaxResourceLabelLength = 80;
    private const int MaxResourceUrlLength   = 2000;

    /// <summary>
    /// Normalizes the optional deliverable association: empty / whitespace
    /// becomes NULL ("belongs to the project, not to a deliverable"), and a key
    /// longer than the column's contract is refused rather than truncated —
    /// a truncated key would silently associate the resource with nothing.
    ///
    /// <para>The key is NOT validated against a list of deliverables: the
    /// catalog is client-side content (SubmissionDeliverablesCatalog) with no
    /// table behind it, exactly as it already is for
    /// my-submission-progress, and duplicating it here would create a second
    /// place to update whenever the faculty list changes. A key that matches
    /// nothing simply reads as unassociated on the client.</para>
    /// </summary>
    private static bool TryNormalizeDeliverableKey(string? raw, out string? key)
    {
        key = null;

        var trimmed = (raw ?? "").Trim();
        if (trimmed.Length == 0) return true;
        if (trimmed.Length > MaxDeliverableKeyLength) return false;

        key = trimmed;
        return true;
    }

    /// <summary>
    /// Accepts only an absolute http/https URL. A bare host ("figma.com/...")
    /// is upgraded to https rather than rejected, because that is what a
    /// student pasting from an address bar produces; everything else —
    /// javascript:, data:, file:, mailto: — is refused, so nothing that reaches
    /// an anchor's href can execute or exfiltrate.
    /// </summary>
    private static bool TryNormalizeResourceUrl(string raw, out string safeUrl)
    {
        safeUrl = "";

        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxResourceUrlLength)
            return false;

        var candidate = raw.Trim();

        // Only add a scheme when there is none at all. A string that already
        // carries a scheme must be judged on that scheme, never rewritten.
        if (!candidate.Contains("://", StringComparison.Ordinal)
            && !candidate.Contains(':', StringComparison.Ordinal))
        {
            candidate = "https://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (string.IsNullOrWhiteSpace(uri.Host))
            return false;

        safeUrl = uri.ToString();
        return safeUrl.Length <= MaxResourceUrlLength;
    }

    // Resolves the caller's project the same way my-project-details does —
    // deliberately WITHOUT the AssignmentIsDraft filter that
    // GetProjectTeamForUserAsync applies, so a student can always edit the
    // project the workspace page just showed them.
    private async Task<int?> GetProjectIdForUserAsync(int userId)
    {
        const string sql = @"
            SELECT  p.Id
            FROM    Projects    p
            JOIN    Teams       t  ON p.TeamId = t.Id
            JOIN    TeamMembers tm ON t.Id     = tm.TeamId
            WHERE   tm.UserId   = @UserId
              AND   tm.IsActive = 1
            LIMIT   1";

        var rows = await _db.GetRecordsAsync<SubTeamIdRow>(sql, new { UserId = userId });
        return rows?.FirstOrDefault()?.Id;
    }

    // ── GET /api/projects/personal-tasks ─────────────────────────────────────
    // Returns the authenticated user's personal task list, newest first.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("personal-tasks")]
    public async Task<IActionResult> GetPersonalTasks(int authUserId)
    {
        // Project context is resolved through ProjectMentors, NOT through a
        // plain join on Projects. That single condition is what makes a stale
        // association safe: if the mentor no longer mentors the project, the
        // join misses, ProjectId/ProjectTitle/TeamName all come back NULL, and
        // the task renders as "ללא שיוך". The row itself is untouched and still
        // belongs to its owner — access is lost, the work item is not.
        const string sql = @"
            SELECT  pt.Id,
                    pt.Title,
                    pt.Description,
                    pt.DueDate,
                    pt.StartTime,
                    pt.EndTime,
                    pt.IsDone,
                    pt.CreatedAt,
                    pm.ProjectId          AS ProjectId,
                    p.Title               AS ProjectTitle,
                    t.TeamName            AS TeamName
            FROM    PersonalTasks pt
            LEFT JOIN ProjectMentors pm
                        ON  pm.ProjectId = pt.ProjectId
                        AND pm.UserId    = @UserId
            LEFT JOIN Projects p ON p.Id     = pm.ProjectId
            LEFT JOIN Teams    t ON t.Id     = p.TeamId
            WHERE   pt.UserId = @UserId
            ORDER   BY pt.CreatedAt DESC";

        var rows = await _db.GetRecordsAsync<PersonalTaskDto>(sql, new { UserId = authUserId });
        return Ok(rows ?? Enumerable.Empty<PersonalTaskDto>());
    }

    /// <summary>
    /// True when the caller currently mentors this project.
    ///
    /// <para>The ONLY place a client-supplied ProjectId is allowed to become
    /// trusted. It answers a bare existence question against ProjectMentors and
    /// returns nothing about the project, so a crafted id for someone else's
    /// project is refused without confirming that the project exists or
    /// revealing a single field of it.</para>
    /// </summary>
    private async Task<bool> MentorsProjectAsync(int projectId, int userId)
    {
        var rows = await _db.GetRecordsAsync<int>(
            "SELECT 1 FROM ProjectMentors WHERE ProjectId = @ProjectId AND UserId = @UserId",
            new { ProjectId = projectId, UserId = userId });

        return rows?.Any() == true;
    }

    // ── POST /api/projects/personal-tasks ────────────────────────────────────
    // Creates a new personal task for the authenticated user.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("personal-tasks")]
    public async Task<IActionResult> CreatePersonalTask(
        [FromBody] CreatePersonalTaskRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("כותרת המשימה לא יכולה להיות ריקה");

        // A project may be attached only by someone who actually mentors it. The
        // message says nothing about whether the id exists.
        if (req.ProjectId is int wantedProject && !await MentorsProjectAsync(wantedProject, authUserId))
            return BadRequest("לא ניתן לשייך את המשימה לפרויקט זה");

        if (!TryNormalizeWorkBlock(req.StartTime, req.EndTime, req.DueDate,
                                   out var newStart, out var newEnd, out var timeError))
            return BadRequest(timeError);

        const string sql = @"
            INSERT INTO PersonalTasks (UserId, Title, Description, DueDate, ProjectId, StartTime, EndTime)
            VALUES (@UserId, @Title, @Description, @DueDate, @ProjectId, @StartTime, @EndTime)";

        int newId = await _db.InsertReturnIdAsync(sql, new
        {
            UserId      = authUserId,
            Title       = req.Title.Trim(),
            Description = req.Description?.Trim(),
            DueDate     = req.DueDate?.ToString("yyyy-MM-dd"),
            ProjectId   = req.ProjectId,
            StartTime   = newStart,
            EndTime     = newEnd,
        });

        if (newId == 0) return StatusCode(500, "שגיאה ביצירת המשימה");

        // Echo the project context back so the client can render the new row
        // without refetching. Read through the same authorized path.
        var context = req.ProjectId is int pid ? await GetProjectContextAsync(pid, authUserId) : null;

        return Ok(new PersonalTaskDto
        {
            Id           = newId,
            Title        = req.Title.Trim(),
            Description  = req.Description?.Trim(),
            DueDate      = req.DueDate,
            StartTime    = newStart,
            EndTime      = newEnd,
            IsDone       = false,
            CreatedAt    = DateTime.UtcNow,
            ProjectId    = context is null ? null : req.ProjectId,
            ProjectTitle = context?.ProjectTitle,
            TeamName     = context?.TeamName,
        });
    }

    /// <summary>Project title/team for a project the caller mentors, or null.
    /// Goes through ProjectMentors for the same reason the read query does.</summary>
    private async Task<ProjectContextRow?> GetProjectContextAsync(int projectId, int userId)
    {
        const string sql = @"
            SELECT  p.Title    AS ProjectTitle,
                    t.TeamName AS TeamName
            FROM    ProjectMentors pm
            JOIN    Projects p ON p.Id = pm.ProjectId
            LEFT JOIN Teams  t ON t.Id = p.TeamId
            WHERE   pm.ProjectId = @ProjectId
              AND   pm.UserId    = @UserId";

        var rows = await _db.GetRecordsAsync<ProjectContextRow>(
            sql, new { ProjectId = projectId, UserId = userId });

        return rows?.FirstOrDefault();
    }

    private sealed class ProjectContextRow
    {
        public string? ProjectTitle { get; set; }
        public string? TeamName     { get; set; }
    }

    // ── PATCH /api/projects/personal-tasks/{id}/toggle ───────────────────────
    // Toggles the IsDone flag. Only the owning user may modify their tasks.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPatch("personal-tasks/{id:int}/toggle")]
    public async Task<IActionResult> TogglePersonalTask(int id, int authUserId)
    {
        const string sql = @"
            UPDATE PersonalTasks
            SET    IsDone = CASE WHEN IsDone = 1 THEN 0 ELSE 1 END
            WHERE  Id     = @Id
              AND  UserId = @UserId";

        int affected = await _db.SaveDataAsync(sql, new { Id = id, UserId = authUserId });
        if (affected == 0) return NotFound("המשימה לא נמצאה");
        return Ok();
    }

    // ── PUT /api/projects/personal-tasks/{id} ────────────────────────────────
    // Edits an existing personal task. Same ownership rule as the toggle above:
    // the UPDATE carries `AND UserId = @UserId`, so another user's row matches
    // zero rows and comes back 404 — the request never learns whether the id
    // exists at all. Ownership is therefore structural, not a separate check
    // that could be forgotten.
    //
    // Deliberately NOT editable here: IsDone, which has its own toggle endpoint,
    // and UserId, which nothing may reassign.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("personal-tasks/{id:int}")]
    public async Task<IActionResult> UpdatePersonalTask(
        int id, [FromBody] UpdatePersonalTaskRequest req, int authUserId)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("כותרת המשימה לא יכולה להיות ריקה");

        // Same gate as create, re-run on every save: a mentor who lost access to
        // a project cannot keep re-attaching it, and a crafted id is refused
        // here too. Null clears the association, which needs no permission.
        if (req.ProjectId is int wantedProject && !await MentorsProjectAsync(wantedProject, authUserId))
            return BadRequest("לא ניתן לשייך את המשימה לפרויקט זה");

        // Same gate as create. Clearing both fields is always allowed and is how
        // a scheduled task goes back to being date-only.
        if (!TryNormalizeWorkBlock(req.StartTime, req.EndTime, req.DueDate,
                                   out var editStart, out var editEnd, out var timeError))
            return BadRequest(timeError);

        const string sql = @"
            UPDATE PersonalTasks
            SET    Title       = @Title,
                   Description = @Description,
                   DueDate     = @DueDate,
                   ProjectId   = @ProjectId,
                   StartTime   = @StartTime,
                   EndTime     = @EndTime
            WHERE  Id     = @Id
              AND  UserId = @UserId";

        int affected = await _db.SaveDataAsync(sql, new
        {
            Id          = id,
            UserId      = authUserId,
            Title       = req.Title.Trim(),
            Description = req.Description?.Trim(),
            // Date-only, matching CreatePersonalTask — a due date is a date the
            // user picked, never an instant, so it must not acquire a time here.
            // The optional work block lives in its own two columns instead.
            DueDate     = req.DueDate?.ToString("yyyy-MM-dd"),
            ProjectId   = req.ProjectId,
            StartTime   = editStart,
            EndTime     = editEnd,
        });

        if (affected == 0) return NotFound("המשימה לא נמצאה");
        return Ok();
    }

    // ── DELETE /api/projects/personal-tasks/{id} ─────────────────────────────
    // Removes one personal task. Same `AND UserId = @UserId` scoping, so a
    // mentor can only ever delete their own row. Hard delete: a personal
    // reminder has no history anyone depends on, and PersonalTasks is not
    // referenced by any other table.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("personal-tasks/{id:int}")]
    public async Task<IActionResult> DeletePersonalTask(int id, int authUserId)
    {
        const string sql = @"
            DELETE FROM PersonalTasks
            WHERE  Id     = @Id
              AND  UserId = @UserId";

        int affected = await _db.SaveDataAsync(sql, new { Id = id, UserId = authUserId });
        if (affected == 0) return NotFound("המשימה לא נמצאה");

        // Best-effort Google cleanup, AFTER the row is gone — the identical
        // lifecycle DeleteTeamTask uses, and for the identical reason: this
        // never throws and never fails the delete, so a task cannot become
        // undeletable because Google is unreachable. Running it after the
        // scoped DELETE is also what keeps it safe: only a row this caller
        // actually owned can reach this line, so no other user's link is ever
        // touched.
        await _calendarEvents.RemoveLinksForDeletedTaskAsync(
            id, GoogleCalendarEventService.PersonalTaskType);

        return Ok();
    }

    /// <summary>
    /// Validates and normalizes the optional schedule on a personal task.
    ///
    /// <para>THREE VALID SHAPES, and no other:</para>
    /// <list type="bullet">
    ///   <item><b>nothing</b> — a task due on a day. The default, and what
    ///   every row written before these columns existed still is;</item>
    ///   <item><b>start alone</b> — due AT an hour. A deadline is a point in
    ///   time and genuinely has no end, so demanding one would force the user
    ///   to invent a duration that nobody decided;</item>
    ///   <item><b>start and end</b> — a scheduled block. This is the shape a
    ///   Google Calendar event needs, which is why the sync toggle asks for
    ///   it and this method does not.</item>
    /// </list>
    ///
    /// <para>An END WITHOUT A START is not a shape — it is a duration with no
    /// beginning — and a time of any kind needs a day to sit on. Both are
    /// refusals rather than repairs: nothing here invents a value.</para>
    ///
    /// <para>Enforced here and not only in the modal: these endpoints are
    /// reachable without the UI, and an inverted range would go on to become a
    /// Google event that silently ends before it begins.</para>
    /// </summary>
    private static bool TryNormalizeWorkBlock(
        string? rawStart, string? rawEnd, DateTime? dueDate,
        out string? start, out string? end, out string error)
    {
        start = end = null;
        error = "";

        bool hasStart = !string.IsNullOrWhiteSpace(rawStart);
        bool hasEnd   = !string.IsNullOrWhiteSpace(rawEnd);

        if (!hasStart && !hasEnd) return true;      // date-only, the default

        if (!hasStart)
        {
            error = "יש להזין שעת התחלה";
            return false;
        }

        if (!TryParseWallClock(rawStart!, out var parsedStart))
        {
            error = "שעה לא תקינה";
            return false;
        }

        TimeSpan? parsedEnd = null;

        if (hasEnd)
        {
            if (!TryParseWallClock(rawEnd!, out var e))
            {
                error = "שעה לא תקינה";
                return false;
            }

            if (e <= parsedStart)
            {
                error = "שעת הסיום חייבת להיות אחרי שעת ההתחלה";
                return false;
            }

            parsedEnd = e;
        }

        if (dueDate is null)
        {
            error = "יש לבחור תאריך יעד כדי לקבוע שעה למשימה";
            return false;
        }

        start = FormatWallClock(parsedStart);
        end   = parsedEnd is TimeSpan pe ? FormatWallClock(pe) : null;
        return true;
    }

    /// <summary>"HH:mm" is what &lt;input type="time"&gt; submits; "HH:mm:ss" is
    /// accepted so a non-browser caller is not tripped up by a seconds
    /// component. Same pair GoogleCalendarTasksController parses.</summary>
    private static bool TryParseWallClock(string value, out TimeSpan time) =>
        TimeSpan.TryParseExact(value.Trim(), new[] { @"hh\:mm", @"hh\:mm\:ss" },
                               System.Globalization.CultureInfo.InvariantCulture, out time);

    private static string FormatWallClock(TimeSpan t) => $"{t.Hours:D2}:{t.Minutes:D2}";

    // ═══════════════════════════════════════════════════════════════════════════
    //  TEAM TASKS
    //  Completely separate from official Tasks/TaskSubmissions.
    //  No milestone, no mentor review, no progress impact.
    //  All active team members of the project may read and mutate any row.
    //  Authorization: every query is scoped to (ProjectId, TeamId) derived
    //  project-scoped from authUserId — cross-team access is structurally impossible.
    // ═══════════════════════════════════════════════════════════════════════════

    // ── GET /api/projects/team-tasks ─────────────────────────────────────────
    // Returns all team tasks (incomplete first, then complete), newest-first within
    // each group. Assignee name is resolved server-side so no extra calls are needed.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("team-tasks")]
    public async Task<IActionResult> GetTeamTasks(int authUserId)
    {
        var pt = await GetProjectTeamForUserAsync(authUserId);
        if (pt is null) return NotFound("לא שויכת לפרויקט פעיל");

        const string sql = @"
            SELECT  tt.Id,
                    tt.Title,
                    tt.Description,
                    tt.AssignedToUserId,
                    CASE WHEN tt.AssignedToUserId IS NULL
                         THEN 'כל הצוות'
                         ELSE u.FirstName || ' ' || u.LastName
                    END  AS AssigneeName,
                    tt.DueDate,
                    tt.IsDone,
                    tt.CreatedByUserId,
                    tt.CreatedAt,
                    tt.UpdatedAt
            FROM    TeamTasks tt
            LEFT JOIN users u ON tt.AssignedToUserId = u.Id
            WHERE   tt.TeamId    = @TeamId
              AND   tt.ProjectId = @ProjectId
            ORDER BY tt.IsDone ASC, tt.CreatedAt DESC";

        var rows = await _db.GetRecordsAsync<TeamTaskDto>(
            sql, new { pt.TeamId, pt.ProjectId });
        return Ok(rows ?? Enumerable.Empty<TeamTaskDto>());
    }

    // ── POST /api/projects/team-tasks ────────────────────────────────────────
    // Creates a new team task. Returns the full TeamTaskDto for immediate UI update.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("team-tasks")]
    public async Task<IActionResult> CreateTeamTask(
        [FromBody] CreateTeamTaskRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("כותרת המשימה לא יכולה להיות ריקה");

        var pt = await GetProjectTeamForUserAsync(authUserId);
        if (pt is null) return NotFound("לא שויכת לפרויקט פעיל");

        const string sql = @"
            INSERT INTO TeamTasks
                (ProjectId, TeamId, CreatedByUserId, Title, Description, AssignedToUserId, DueDate)
            VALUES
                (@ProjectId, @TeamId, @CreatedByUserId, @Title, @Description, @AssignedToUserId, @DueDate)";

        int newId = await _db.InsertReturnIdAsync(sql, new
        {
            pt.ProjectId,
            pt.TeamId,
            CreatedByUserId  = authUserId,
            Title            = req.Title.Trim(),
            Description      = req.Description?.Trim(),
            AssignedToUserId = req.AssignedToUserId,
            DueDate          = req.DueDate?.ToString("yyyy-MM-dd"),
        });

        if (newId == 0) return StatusCode(500, "שגיאה ביצירת המשימה");

        string assigneeName = "כל הצוות";
        if (req.AssignedToUserId.HasValue)
        {
            var nameRow = (await _db.GetRecordsAsync<AssigneeNameRow>(
                "SELECT FirstName || ' ' || LastName AS Name FROM users WHERE Id = @Id",
                new { Id = req.AssignedToUserId.Value }))?.FirstOrDefault();
            if (nameRow is not null) assigneeName = nameRow.Name;
        }

        return Ok(new TeamTaskDto
        {
            Id               = newId,
            Title            = req.Title.Trim(),
            Description      = req.Description?.Trim(),
            AssignedToUserId = req.AssignedToUserId,
            AssigneeName     = assigneeName,
            DueDate          = req.DueDate,
            IsDone           = false,
            CreatedByUserId  = authUserId,
            CreatedAt        = DateTime.UtcNow,
        });
    }

    // ── PUT /api/projects/team-tasks/{id} ────────────────────────────────────
    // Updates title, description, assignee and due date.
    // The WHERE clause enforces TeamId + ProjectId so cross-team writes are blocked.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("team-tasks/{id:int}")]
    public async Task<IActionResult> UpdateTeamTask(
        int id, [FromBody] UpdateTeamTaskRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("כותרת המשימה לא יכולה להיות ריקה");

        var pt = await GetProjectTeamForUserAsync(authUserId);
        if (pt is null) return NotFound("לא שויכת לפרויקט פעיל");

        const string sql = @"
            UPDATE TeamTasks
            SET    Title            = @Title,
                   Description      = @Description,
                   AssignedToUserId = @AssignedToUserId,
                   DueDate          = @DueDate,
                   UpdatedAt        = datetime('now')
            WHERE  Id        = @Id
              AND  TeamId    = @TeamId
              AND  ProjectId = @ProjectId";

        int affected = await _db.SaveDataAsync(sql, new
        {
            Id               = id,
            pt.TeamId,
            pt.ProjectId,
            Title            = req.Title.Trim(),
            Description      = req.Description?.Trim(),
            AssignedToUserId = req.AssignedToUserId,
            DueDate          = req.DueDate?.ToString("yyyy-MM-dd"),
        });

        if (affected == 0) return NotFound("המשימה לא נמצאה");
        return Ok();
    }

    // ── PATCH /api/projects/team-tasks/{id}/toggle ───────────────────────────
    // Flips IsDone. Any team member may toggle any task.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPatch("team-tasks/{id:int}/toggle")]
    public async Task<IActionResult> ToggleTeamTask(int id, int authUserId)
    {
        var pt = await GetProjectTeamForUserAsync(authUserId);
        if (pt is null) return NotFound("לא שויכת לפרויקט פעיל");

        const string sql = @"
            UPDATE TeamTasks
            SET    IsDone    = CASE WHEN IsDone = 1 THEN 0 ELSE 1 END,
                   UpdatedAt = datetime('now')
            WHERE  Id        = @Id
              AND  TeamId    = @TeamId
              AND  ProjectId = @ProjectId";

        int affected = await _db.SaveDataAsync(sql, new { Id = id, pt.TeamId, pt.ProjectId });
        if (affected == 0) return NotFound("המשימה לא נמצאה");
        return Ok();
    }

    // ── DELETE /api/projects/team-tasks/{id} ─────────────────────────────────
    // Permanently deletes a team task. Any team member may delete any task.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("team-tasks/{id:int}")]
    public async Task<IActionResult> DeleteTeamTask(int id, int authUserId)
    {
        var pt = await GetProjectTeamForUserAsync(authUserId);
        if (pt is null) return NotFound("לא שויכת לפרויקט פעיל");

        const string sql = @"
            DELETE FROM TeamTasks
            WHERE  Id        = @Id
              AND  TeamId    = @TeamId
              AND  ProjectId = @ProjectId";

        int affected = await _db.SaveDataAsync(sql, new { Id = id, pt.TeamId, pt.ProjectId });
        if (affected == 0) return NotFound("המשימה לא נמצאה");

        // Best-effort Google cleanup, AFTER the task is gone and deliberately not
        // awaited for success: this never throws and never fails the delete. A
        // task must not become undeletable because Google is unreachable. Every
        // link row for the task is dropped regardless of what Google answered, so
        // no row survives pointing at an id that no longer exists.
        await _calendarEvents.RemoveLinksForDeletedTaskAsync(id);

        return Ok();
    }
}
