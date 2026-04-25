using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

[Route("api/mentor")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Mentor + "," + Roles.Admin + "," + Roles.Staff)]
public class MentorController : ControllerBase
{
    private readonly DbRepository _db;

    public MentorController(DbRepository db) => _db = db;

    // ── GET /api/mentor/projects ─────────────────────────────────────────────
    [HttpGet("projects")]
    public async Task<IActionResult> GetProjects(int authUserId)
    {
        const string sql = @"
            SELECT  p.Id,
                    p.ProjectNumber,
                    p.Title,
                    p.Status,
                    p.HealthStatus,
                    pt.Name                    AS ProjectType,
                    t.Id                       AS TeamId,
                    t.TeamName,

                    COALESCE((
                        SELECT GROUP_CONCAT(u2.FirstName || ' ' || u2.LastName, ', ')
                        FROM   TeamMembers tm2
                        JOIN   users       u2 ON tm2.UserId = u2.Id
                        WHERE  tm2.TeamId = t.Id AND tm2.IsActive = 1
                    ), '')                     AS StudentNames,
                    (SELECT COUNT(*)
                     FROM   TeamMembers tm2
                     WHERE  tm2.TeamId = t.Id AND tm2.IsActive = 1)
                                               AS StudentCount,

                    (SELECT mt2.Title
                     FROM   ProjectMilestones      pm2
                     JOIN   AcademicYearMilestones aym2 ON pm2.AcademicYearMilestoneId = aym2.Id
                     JOIN   MilestoneTemplates     mt2  ON aym2.MilestoneTemplateId    = mt2.Id
                     WHERE  pm2.ProjectId = p.Id
                       AND  pm2.Status NOT IN ('Completed','Done')
                     ORDER  BY mt2.OrderIndex
                     LIMIT  1)                 AS CurrentMilestoneTitle,
                    (SELECT pm2.Status
                     FROM   ProjectMilestones      pm2
                     JOIN   AcademicYearMilestones aym2 ON pm2.AcademicYearMilestoneId = aym2.Id
                     JOIN   MilestoneTemplates     mt2  ON aym2.MilestoneTemplateId    = mt2.Id
                     WHERE  pm2.ProjectId = p.Id
                       AND  pm2.Status NOT IN ('Completed','Done')
                     ORDER  BY mt2.OrderIndex
                     LIMIT  1)                 AS CurrentMilestoneStatus,
                    (SELECT aym2.DueDate
                     FROM   ProjectMilestones      pm2
                     JOIN   AcademicYearMilestones aym2 ON pm2.AcademicYearMilestoneId = aym2.Id
                     JOIN   MilestoneTemplates     mt2  ON aym2.MilestoneTemplateId    = mt2.Id
                     WHERE  pm2.ProjectId = p.Id
                       AND  pm2.Status NOT IN ('Completed','Done')
                     ORDER  BY mt2.OrderIndex
                     LIMIT  1)                 AS CurrentMilestoneDueDate,

                    (SELECT COUNT(*)
                     FROM   ProjectMilestones pm2
                     WHERE  pm2.ProjectId = p.Id)  AS TotalMilestones,
                    (SELECT COUNT(*)
                     FROM   ProjectMilestones pm2
                     WHERE  pm2.ProjectId = p.Id
                       AND  pm2.Status IN ('Completed','Done'))  AS CompletedMilestones,

                    (SELECT COUNT(*) FROM Tasks tk WHERE tk.ProjectId = p.Id)  AS TotalTasks,
                    (SELECT COUNT(*) FROM Tasks tk WHERE tk.ProjectId = p.Id
                     AND tk.Status = 'Open')                                   AS OpenTasks,
                    (SELECT COUNT(*) FROM Tasks tk WHERE tk.ProjectId = p.Id
                     AND tk.Status = 'InProgress')                             AS InProgressTasks,
                    (SELECT COUNT(*) FROM Tasks tk WHERE tk.ProjectId = p.Id
                     AND tk.Status IN ('Done','Completed'))                    AS CompletedTasks,
                    (SELECT COUNT(*) FROM Tasks tk WHERE tk.ProjectId = p.Id
                     AND tk.DueDate < datetime('now')
                     AND tk.Status NOT IN ('Done','Completed','ApprovedForSubmission'))
                                                                               AS OverdueTasks,

                    (SELECT COUNT(*)
                     FROM   TaskSubmissions ts2
                     JOIN   Tasks           tk2 ON ts2.TaskId = tk2.Id
                     WHERE  tk2.ProjectId   = p.Id
                       AND  ts2.MentorStatus = 'Pending')                      AS PendingMentorReview

            FROM    Projects       p
            JOIN    Teams          t   ON p.TeamId        = t.Id
            JOIN    ProjectTypes   pt  ON p.ProjectTypeId = pt.Id
            JOIN    ProjectMentors pmt ON pmt.ProjectId   = p.Id
            WHERE   pmt.UserId = @MentorId
            ORDER   BY p.ProjectNumber";

        var rows = (await _db.GetRecordsAsync<MentorProjectOverviewRow>(
            sql, new { MentorId = authUserId })) ?? Enumerable.Empty<MentorProjectOverviewRow>();

        var result = rows.Select(r => new MentorProjectSummaryDto
        {
            Id                      = r.Id,
            ProjectNumber           = r.ProjectNumber,
            Title                   = r.Title,
            Status                  = r.Status,
            HealthStatus            = r.HealthStatus,
            ProjectType             = r.ProjectType,
            TeamId                  = r.TeamId,
            TeamName                = r.TeamName,
            StudentNames            = r.StudentNames,
            StudentCount            = r.StudentCount,
            CurrentMilestoneTitle   = r.CurrentMilestoneTitle,
            CurrentMilestoneStatus  = r.CurrentMilestoneStatus,
            CurrentMilestoneDueDate = r.CurrentMilestoneDueDate,
            MilestoneProgressPct    = r.TotalMilestones > 0
                                          ? r.CompletedMilestones * 100 / r.TotalMilestones
                                          : 0,
            TotalTasks          = r.TotalTasks,
            OpenTasks           = r.OpenTasks,
            InProgressTasks     = r.InProgressTasks,
            CompletedTasks      = r.CompletedTasks,
            OverdueTasks        = r.OverdueTasks,
            PendingMentorReview = r.PendingMentorReview,
        }).ToList();

        return Ok(result);
    }

    // ── GET /api/mentor/projects/{id} ────────────────────────────────────────
    [HttpGet("projects/{id:int}")]
    public async Task<IActionResult> GetProjectDetail(int id, int authUserId)
    {
        // ── 1. Verify mentor access ──────────────────────────────────────────
        var accessRows = await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM ProjectMentors WHERE ProjectId = @ProjectId AND UserId = @UserId",
            new { ProjectId = id, UserId = authUserId });
        int accessCount = accessRows?.FirstOrDefault() ?? 0;

        var roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        bool isAdminOrStaff = roleClaim == Roles.Admin || roleClaim == Roles.Staff;

        if (accessCount == 0 && !isAdminOrStaff)
            return Forbid();

        // ── 2. Project header ────────────────────────────────────────────────
        const string projectSql = @"
            SELECT  p.Id,
                    p.ProjectNumber,
                    p.Title,
                    p.Status,
                    p.HealthStatus,
                    pt.Name             AS ProjectType,
                    p.Description,
                    p.OrganizationName  AS Organization,
                    t.TeamName
            FROM    Projects     p
            JOIN    Teams        t   ON p.TeamId        = t.Id
            JOIN    ProjectTypes pt  ON p.ProjectTypeId = pt.Id
            WHERE   p.Id = @ProjectId";

        var projectRow = (await _db.GetRecordsAsync<MentorProjectHeaderRow>(
            projectSql, new { ProjectId = id }))?.FirstOrDefault();

        if (projectRow is null) return NotFound();

        // ── 3. Team members ──────────────────────────────────────────────────
        const string membersSql = @"
            SELECT  u.Id                             AS UserId,
                    u.FirstName || ' ' || u.LastName AS FullName,
                    u.Email,
                    COALESCE(u.Phone, '')             AS Phone
            FROM    TeamMembers tm
            JOIN    users       u  ON tm.UserId = u.Id
            WHERE   tm.TeamId  = (SELECT TeamId FROM Projects WHERE Id = @ProjectId)
              AND   tm.IsActive = 1";

        var teamMembers = ((await _db.GetRecordsAsync<MentorTeamMemberDto>(
            membersSql, new { ProjectId = id })) ?? Enumerable.Empty<MentorTeamMemberDto>())
            .ToList();

        // ── 4. Milestones ────────────────────────────────────────────────────
        const string milestonesSql = @"
            SELECT  pm.Id          AS ProjectMilestoneId,
                    mt.Title,
                    mt.OrderIndex,
                    pm.Status,
                    aym.DueDate,
                    (SELECT COUNT(*) FROM Tasks tk WHERE tk.ProjectMilestoneId = pm.Id)
                                   AS TotalTasks,
                    (SELECT COUNT(*) FROM Tasks tk WHERE tk.ProjectMilestoneId = pm.Id
                     AND tk.Status IN ('Done','Completed'))
                                   AS CompletedTasks
            FROM    ProjectMilestones       pm
            JOIN    AcademicYearMilestones  aym ON pm.AcademicYearMilestoneId = aym.Id
            JOIN    MilestoneTemplates      mt  ON aym.MilestoneTemplateId    = mt.Id
            WHERE   pm.ProjectId = @ProjectId
            ORDER   BY mt.OrderIndex";

        var milestoneRows = ((await _db.GetRecordsAsync<MentorMilestoneRow>(
            milestonesSql, new { ProjectId = id })) ?? Enumerable.Empty<MentorMilestoneRow>())
            .ToList();

        // ── 5. Tasks ─────────────────────────────────────────────────────────
        const string tasksSql = @"
            SELECT  t.Id,
                    t.Title,
                    t.Status,
                    t.DueDate,
                    t.ProjectMilestoneId,
                    t.IsSubmission,
                    COALESCE(u.FirstName || ' ' || u.LastName, '') AS AssignedToName,
                    (SELECT ts2.Status
                     FROM   TaskSubmissions ts2
                     WHERE  ts2.TaskId = t.Id ORDER BY ts2.CreatedAt DESC LIMIT 1) AS LatestSubmissionStatus,
                    (SELECT ts2.MentorStatus
                     FROM   TaskSubmissions ts2
                     WHERE  ts2.TaskId = t.Id ORDER BY ts2.CreatedAt DESC LIMIT 1) AS LatestMentorStatus
            FROM    Tasks t
            LEFT JOIN users u ON t.AssignedToUserId = u.Id
            WHERE   t.ProjectId = @ProjectId
            ORDER   BY t.DueDate";

        var taskRows = ((await _db.GetRecordsAsync<MentorTaskRow>(
            tasksSql, new { ProjectId = id })) ?? Enumerable.Empty<MentorTaskRow>())
            .ToList();

        // ── 6. Pending submissions ───────────────────────────────────────────
        const string pendingSql = @"
            SELECT  ts.Id                              AS SubmissionId,
                    ts.TaskId,
                    t.Title                            AS TaskTitle,
                    mt.Title                           AS MilestoneTitle,
                    u.FirstName || ' ' || u.LastName   AS SubmittedBy,
                    ts.SubmittedAt,
                    ts.MentorStatus
            FROM    TaskSubmissions        ts
            JOIN    Tasks                  t   ON ts.TaskId                  = t.Id
            JOIN    ProjectMilestones      pm  ON t.ProjectMilestoneId       = pm.Id
            JOIN    AcademicYearMilestones aym ON pm.AcademicYearMilestoneId = aym.Id
            JOIN    MilestoneTemplates     mt  ON aym.MilestoneTemplateId    = mt.Id
            JOIN    users                  u   ON ts.SubmittedByUserId       = u.Id
            WHERE   t.ProjectId     = @ProjectId
              AND   ts.MentorStatus = 'Pending'
            ORDER   BY ts.SubmittedAt";

        var pendingRows = ((await _db.GetRecordsAsync<MentorPendingSubmissionDto>(
            pendingSql, new { ProjectId = id })) ?? Enumerable.Empty<MentorPendingSubmissionDto>())
            .ToList();

        // ── 7. Aggregates ────────────────────────────────────────────────────
        int totalTasks      = taskRows.Count;
        int openTasks       = taskRows.Count(t => t.Status == "Open");
        int inProgressTasks = taskRows.Count(t =>
            t.Status is "InProgress" or "SubmittedToMentor" or "ReturnedForRevision" or "RevisionSubmitted");
        int completedTasks = taskRows.Count(t => t.Status is "Done" or "Completed" or "ApprovedForSubmission");
        int overdueTasks   = taskRows.Count(t =>
            t.DueDate.HasValue && t.DueDate < DateTime.UtcNow &&
            t.Status is not ("Done" or "Completed" or "ApprovedForSubmission"));
        int pendingReview  = taskRows.Count(t => t.LatestMentorStatus == "Pending");

        int totalMs     = milestoneRows.Count;
        int completedMs = milestoneRows.Count(m => m.Status is "Completed" or "Done");

        // ── 8. Assemble milestones with nested tasks ─────────────────────────
        var tasksByMilestone = taskRows.ToLookup(t => t.ProjectMilestoneId);

        var milestones = milestoneRows.Select(m => new MentorMilestoneDto
        {
            ProjectMilestoneId = m.ProjectMilestoneId,
            Title              = m.Title,
            OrderIndex         = m.OrderIndex,
            Status             = m.Status,
            DueDate            = m.DueDate,
            TotalTasks         = m.TotalTasks,
            CompletedTasks     = m.CompletedTasks,
            ProgressPct        = m.TotalTasks > 0 ? m.CompletedTasks * 100 / m.TotalTasks : 0,
            Tasks = tasksByMilestone[m.ProjectMilestoneId]
                .Select(t => new MentorTaskDto
                {
                    Id                     = t.Id,
                    Title                  = t.Title,
                    Status                 = NormalizeTaskStatus(t.Status),
                    DueDate                = t.DueDate,
                    IsOverdue              = t.DueDate.HasValue && t.DueDate < DateTime.UtcNow
                                                && t.Status is not ("Done" or "ApprovedForSubmission"),
                    IsSubmission           = t.IsSubmission,
                    AssignedToName         = t.AssignedToName,
                    LatestSubmissionStatus = t.LatestSubmissionStatus,
                    LatestMentorStatus     = t.LatestMentorStatus,
                }).ToList(),
        }).ToList();

        return Ok(new MentorProjectDetailDto
        {
            Id                   = projectRow.Id,
            ProjectNumber        = projectRow.ProjectNumber,
            Title                = projectRow.Title,
            Status               = projectRow.Status,
            HealthStatus         = projectRow.HealthStatus,
            ProjectType          = projectRow.ProjectType,
            Description          = projectRow.Description,
            Organization         = projectRow.Organization,
            TeamName             = projectRow.TeamName,
            TeamMembers          = teamMembers,
            MilestoneProgressPct = totalMs > 0 ? completedMs * 100 / totalMs : 0,
            TaskProgressPct      = totalTasks > 0 ? completedTasks * 100 / totalTasks : 0,
            TotalTasks           = totalTasks,
            OpenTasks            = openTasks,
            InProgressTasks      = inProgressTasks,
            CompletedTasks       = completedTasks,
            OverdueTasks         = overdueTasks,
            PendingMentorReview  = pendingReview,
            Milestones           = milestones,
            PendingSubmissions   = pendingRows,
        });
    }

    // ── GET /api/mentor/submissions ──────────────────────────────────────────
    // Returns ALL submissions pending mentor review across every project the
    // mentor is assigned to. Used by the global mentor submissions page.
    [HttpGet("submissions")]
    public async Task<IActionResult> GetPendingSubmissions(int authUserId)
    {
        var roleClaim      = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        bool isAdminOrStaff = roleClaim == Roles.Admin || roleClaim == Roles.Staff;

        // Admin/Staff can see all projects; mentors only see their own.
        const string sql = @"
            SELECT  ts.Id                              AS SubmissionId,
                    ts.TaskId,
                    t.Title                            AS TaskTitle,
                    p.Id                               AS ProjectId,
                    p.Title                            AS ProjectTitle,
                    p.ProjectNumber,
                    COALESCE(mt.Title, '')              AS MilestoneTitle,
                    u.FirstName || ' ' || u.LastName   AS SubmittedBy,
                    ts.SubmittedAt,
                    ts.MentorStatus
            FROM    TaskSubmissions        ts
            JOIN    Tasks                  t   ON ts.TaskId                  = t.Id
            JOIN    Projects               p   ON t.ProjectId                = p.Id
            JOIN    ProjectMilestones      pm  ON t.ProjectMilestoneId       = pm.Id
            JOIN    AcademicYearMilestones aym ON pm.AcademicYearMilestoneId = aym.Id
            JOIN    MilestoneTemplates     mt  ON aym.MilestoneTemplateId    = mt.Id
            JOIN    users                  u   ON ts.SubmittedByUserId       = u.Id
            JOIN    ProjectMentors         pmt ON pmt.ProjectId              = p.Id
            WHERE   (@IsAdminOrStaff = 1 OR pmt.UserId = @MentorId)
              AND   ts.MentorStatus = 'Pending'
            ORDER   BY ts.SubmittedAt ASC";

        var rows = await _db.GetRecordsAsync<MentorPendingSubmissionDto>(
            sql, new { MentorId = authUserId, IsAdminOrStaff = isAdminOrStaff ? 1 : 0 });

        return Ok(rows ?? Enumerable.Empty<MentorPendingSubmissionDto>());
    }

    // ── GET /api/mentor/submissions/{id}/context ─────────────────────────────
    // Full context for a single submission: task info, files, and all round history.
    // Verifies the submission belongs to a project the mentor is assigned to.
    [HttpGet("submissions/{id:int}/context")]
    public async Task<IActionResult> GetSubmissionContext(int id, int authUserId)
    {
        // ── 1. Fetch submission + task context ────────────────────────────────
        const string subSql = @"
            SELECT  ts.Id                              AS SubmissionId,
                    ts.TaskId,
                    t.Title                            AS TaskTitle,
                    t.Description                      AS TaskDescription,
                    COALESCE(mt.Title, '')             AS MilestoneTitle,
                    u.FirstName || ' ' || u.LastName   AS SubmittedBy,
                    ts.SubmittedAt,
                    ts.Notes,
                    ts.MentorStatus,
                    ts.MentorFeedback,
                    t.ProjectId
            FROM    TaskSubmissions        ts
            JOIN    Tasks                  t   ON ts.TaskId                  = t.Id
            JOIN    users                  u   ON ts.SubmittedByUserId       = u.Id
            LEFT JOIN ProjectMilestones    pm  ON t.ProjectMilestoneId       = pm.Id
            LEFT JOIN AcademicYearMilestones aym ON pm.AcademicYearMilestoneId = aym.Id
            LEFT JOIN MilestoneTemplates   mt  ON aym.MilestoneTemplateId    = mt.Id
            WHERE   ts.Id = @SubmissionId";

        var subRow = (await _db.GetRecordsAsync<SubmissionContextRow>(
            subSql, new { SubmissionId = id }))?.FirstOrDefault();

        if (subRow is null) return NotFound();

        // ── 2. Verify mentor is assigned to the project ───────────────────────
        var roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        bool isAdminOrStaff = roleClaim == Roles.Admin || roleClaim == Roles.Staff;

        if (!isAdminOrStaff)
        {
            var access = (await _db.GetRecordsAsync<int>(
                "SELECT COUNT(1) FROM ProjectMentors WHERE ProjectId = @ProjectId AND UserId = @UserId",
                new { ProjectId = subRow.ProjectId, UserId = authUserId }))?.FirstOrDefault() ?? 0;
            if (access == 0) return Forbid();
        }

        // ── 3. Files for this submission ──────────────────────────────────────
        const string filesSql = @"
            SELECT  Id, TaskSubmissionId, OriginalFileName, StoredFileName,
                    ContentType, SizeBytes, UploadedAt
            FROM    TaskSubmissionFiles
            WHERE   TaskSubmissionId = @SubmissionId
            ORDER   BY Id";

        var files = ((await _db.GetRecordsAsync<TaskSubmissionFileDto>(
            filesSql, new { SubmissionId = id })) ?? Enumerable.Empty<TaskSubmissionFileDto>())
            .ToList();

        // ── 4. Full round history for the task ────────────────────────────────
        const string historySql = @"
            SELECT  ts.Id         AS SubmissionId,
                    ts.SubmittedAt,
                    ts.Notes,
                    ts.Status,
                    ts.ReviewerFeedback,
                    ts.MentorStatus,
                    ts.MentorFeedback,
                    ts.MentorReviewedAt,
                    ts.CourseSubmittedAt,
                    COUNT(f.Id)   AS FileCount
            FROM    TaskSubmissions      ts
            LEFT JOIN TaskSubmissionFiles f ON f.TaskSubmissionId = ts.Id
            WHERE   ts.TaskId = @TaskId
            GROUP   BY ts.Id
            ORDER   BY ts.SubmittedAt ASC";

        var historyRows = ((await _db.GetRecordsAsync<SubmissionHistoryRow>(
            historySql, new { TaskId = subRow.TaskId })) ?? Enumerable.Empty<SubmissionHistoryRow>())
            .ToList();

        // ── 5. Files for every round ───────────────────────────────────────────
        const string allFilesSql = @"
            SELECT  f.Id, f.TaskSubmissionId, f.OriginalFileName, f.StoredFileName,
                    f.ContentType, f.SizeBytes, f.UploadedAt
            FROM    TaskSubmissionFiles f
            JOIN    TaskSubmissions     ts ON f.TaskSubmissionId = ts.Id
            WHERE   ts.TaskId = @TaskId
            ORDER   BY f.TaskSubmissionId ASC, f.Id ASC";

        var allFilesRaw = ((await _db.GetRecordsAsync<TaskSubmissionFileDto>(
            allFilesSql, new { TaskId = subRow.TaskId })) ?? Enumerable.Empty<TaskSubmissionFileDto>())
            .ToList();

        var filesByRound = allFilesRaw
            .GroupBy(f => f.TaskSubmissionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var history = historyRows
            .Select((r, idx) => new MentorSubmissionRoundDto
            {
                SubmissionId      = r.SubmissionId,
                RoundNumber       = idx + 1,  // oldest first → round 1, 2, 3 …
                SubmittedAt       = r.SubmittedAt,
                Notes             = r.Notes,
                Status            = r.Status,
                ReviewerFeedback  = r.ReviewerFeedback,
                MentorStatus      = r.MentorStatus,
                MentorFeedback    = r.MentorFeedback,
                MentorReviewedAt  = r.MentorReviewedAt,
                CourseSubmittedAt = r.CourseSubmittedAt,
                FileCount         = r.FileCount,
                Files             = filesByRound.GetValueOrDefault(r.SubmissionId) ?? new(),
            }).ToList();

        return Ok(new MentorSubmissionContextDto
        {
            TaskId          = subRow.TaskId,
            TaskTitle       = subRow.TaskTitle,
            TaskDescription = subRow.TaskDescription,
            MilestoneTitle  = subRow.MilestoneTitle,
            SubmissionId    = subRow.SubmissionId,
            SubmittedBy     = subRow.SubmittedBy,
            SubmittedAt     = subRow.SubmittedAt,
            Notes           = subRow.Notes,
            MentorStatus    = subRow.MentorStatus,
            MentorFeedback  = subRow.MentorFeedback,
            Files           = files,
            History         = history,
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string NormalizeTaskStatus(string? raw) => raw switch
    {
        "Completed" => "Done",
        _           => raw ?? "Open",
    };

    // ── Private Dapper row classes ────────────────────────────────────────────
    // Must be classes with public properties — Dapper maps by property name.
    // Do NOT use positional records here; DbRepository silently returns null
    // on any Dapper mapping failure, which causes downstream NullReferenceExceptions.

    private sealed class MentorProjectOverviewRow
    {
        public int       Id                      { get; set; }
        public int       ProjectNumber           { get; set; }
        public string    Title                   { get; set; } = "";
        public string    Status                  { get; set; } = "";
        public string?   HealthStatus            { get; set; }
        public string    ProjectType             { get; set; } = "";
        public int       TeamId                  { get; set; }
        public string    TeamName                { get; set; } = "";
        public string    StudentNames            { get; set; } = "";
        public int       StudentCount            { get; set; }
        public string?   CurrentMilestoneTitle   { get; set; }
        public string?   CurrentMilestoneStatus  { get; set; }
        public DateTime? CurrentMilestoneDueDate { get; set; }
        public int       TotalMilestones         { get; set; }
        public int       CompletedMilestones     { get; set; }
        public int       TotalTasks              { get; set; }
        public int       OpenTasks               { get; set; }
        public int       InProgressTasks         { get; set; }
        public int       CompletedTasks          { get; set; }
        public int       OverdueTasks            { get; set; }
        public int       PendingMentorReview     { get; set; }
    }

    private sealed class MentorProjectHeaderRow
    {
        public int     Id           { get; set; }
        public int     ProjectNumber { get; set; }
        public string  Title        { get; set; } = "";
        public string  Status       { get; set; } = "";
        public string? HealthStatus { get; set; }
        public string  ProjectType  { get; set; } = "";
        public string? Description  { get; set; }
        public string? Organization { get; set; }
        public string  TeamName     { get; set; } = "";
    }

    private sealed class MentorMilestoneRow
    {
        public int       ProjectMilestoneId { get; set; }
        public string    Title              { get; set; } = "";
        public int       OrderIndex         { get; set; }
        public string    Status             { get; set; } = "";
        public DateTime? DueDate            { get; set; }
        public int       TotalTasks         { get; set; }
        public int       CompletedTasks     { get; set; }
    }

    private sealed class MentorTaskRow
    {
        public int       Id                     { get; set; }
        public string    Title                  { get; set; } = "";
        public string    Status                 { get; set; } = "";
        public DateTime? DueDate                { get; set; }
        public int?      ProjectMilestoneId     { get; set; }
        public bool      IsSubmission           { get; set; }
        public string    AssignedToName         { get; set; } = "";
        public string?   LatestSubmissionStatus { get; set; }
        public string?   LatestMentorStatus     { get; set; }
    }

    private sealed class SubmissionContextRow
    {
        public int      SubmissionId     { get; set; }
        public int      TaskId           { get; set; }
        public string   TaskTitle        { get; set; } = "";
        public string?  TaskDescription  { get; set; }
        public string   MilestoneTitle   { get; set; } = "";
        public string   SubmittedBy      { get; set; } = "";
        public DateTime SubmittedAt      { get; set; }
        public string?  Notes            { get; set; }
        public string   MentorStatus     { get; set; } = "Pending";
        public string?  MentorFeedback   { get; set; }
        public int      ProjectId        { get; set; }
    }

    private sealed class SubmissionHistoryRow
    {
        public int       SubmissionId      { get; set; }
        public DateTime  SubmittedAt       { get; set; }
        public string?   Notes             { get; set; }
        public string    Status            { get; set; } = "Submitted";
        public string?   ReviewerFeedback  { get; set; }
        public string    MentorStatus      { get; set; } = "Pending";
        public string?   MentorFeedback    { get; set; }
        public DateTime? MentorReviewedAt  { get; set; }
        public DateTime? CourseSubmittedAt { get; set; }
        public int       FileCount         { get; set; }
    }
}
