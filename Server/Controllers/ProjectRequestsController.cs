using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  ProjectRequestsController — /api/project-requests
//
//  Unified requests module with full thread/history support.
//
//  Priority is NOT accepted from students — it defaults to 'Normal' on
//  creation and is changed later by Admin / Staff via POST /{id}/handle.
//
//  Thread model:  every admin action creates one or more ProjectRequestEvents
//  rows so the full handling history is visible to both sides.
//
//  Status lifecycle: New → InProgress | NeedsInfo → Resolved | Closed
//
//  Authorization:
//    • GET all / GET by id        — Admin, Staff
//    • GET assignable-users       — Admin, Staff
//    • GET my                     — all authenticated roles
//    • POST (create)              — all authenticated roles
//    • POST /{id}/handle          — Admin, Staff
//    • PATCH /{id}   (legacy)     — Admin, Staff
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/project-requests")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize]
public class ProjectRequestsController : ControllerBase
{
    private readonly DbRepository _db;
    private readonly FilesManage  _filesManage;

    private const string Container = "request-attachments";

    // ── Request-level attachments (images only) ───────────────────────────
    private static readonly HashSet<string> AllowedImageTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/jpg", "image/png", "image/webp"
        };

    private const int  MaxAttachments = 5;
    private const long MaxImageBytes  = 5 * 1_048_576;

    // ── Event-level attachments (images + PDF + docx) ─────────────────────
    private static readonly HashSet<string> AllowedEventAttachmentMimes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/jpg", "image/png", "image/webp",
            "application/pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        };

    private static readonly HashSet<string> AllowedEventAttachmentExts =
        new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp", "pdf", "docx" };

    private const int  MaxEventAttachments     = 3;
    private const long MaxEventAttachmentBytes = 5 * 1_048_576;

    public ProjectRequestsController(DbRepository db, FilesManage filesManage)
    {
        _db          = db;
        _filesManage = filesManage;
    }

    // ── GET /api/project-requests/assignable-users ────────────────────────
    //
    // Returns Admin, Staff, and Mentor users for the assignee dropdown.
    [HttpGet("assignable-users")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> GetAssignableUsers(int authUserId)
    {
        const string sql = @"
            SELECT  u.Id,
                    u.FirstName || ' ' || u.LastName AS Name,
                    ur.Role
            FROM    users u
            JOIN    UserRoles ur ON ur.UserId = u.Id
            WHERE   ur.Role IN ('Admin','Staff','Mentor')
              AND   u.IsActive = 1
            ORDER   BY u.FirstName, u.LastName";

        var users = await _db.GetRecordsAsync<AssignableUserDto>(sql);
        return Ok(users ?? Enumerable.Empty<AssignableUserDto>());
    }

    // ── GET /api/project-requests/extension-targets ─────────────────────
    //
    // Returns the pickable targets (submission tasks + project milestones) for
    // the calling student's project. Used by the student's extension-request
    // creation modal. Strictly scoped: only the caller's own active team.
    // Only IsSubmission=1 tasks are surfaced because non-submission tasks have
    // no due date that students need an extension for.
    [HttpGet("extension-targets")]
    public async Task<IActionResult> GetExtensionTargets(int authUserId)
    {
        // Resolve the student's project via active team membership
        const string projectSql = @"
            SELECT  p.Id
            FROM    Projects    p
            JOIN    Teams       t  ON t.Id  = p.TeamId
            JOIN    TeamMembers tm ON tm.TeamId = t.Id
            WHERE   tm.UserId   = @UserId
              AND   tm.IsActive = 1
            LIMIT   1";

        var projectId = (await _db.GetRecordsAsync<int>(
            projectSql, new { UserId = authUserId }))?.FirstOrDefault();
        if (projectId is null || projectId == 0)
            return Ok(Array.Empty<ExtensionTargetDto>());

        // Submission tasks for this project — eligibility filter:
        //   - Must have a due date
        //   - Status must be a "still editable" state (Open / InProgress).
        //     Done / Completed / SubmittedToMentor are excluded.
        //   - Not closed (ClosedAt IS NULL)
        //   - No existing submission attached (any submission = "submitted",
        //     so the task is no longer in a state where extending the deadline
        //     makes sense for this team).
        const string tasksSql = @"
            SELECT  'Task'                       AS Kind,
                    t.Id                         AS Id,
                    t.Title                      AS Title,
                    COALESCE(mt.Title, '')       AS MilestoneTitle,
                    t.DueDate                    AS CurrentDueDate
            FROM    Tasks                    t
            LEFT JOIN ProjectMilestones      pm  ON pm.Id  = t.ProjectMilestoneId
            LEFT JOIN AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            LEFT JOIN MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            WHERE   t.ProjectId    = @ProjectId
              AND   t.IsSubmission = 1
              AND   t.DueDate      IS NOT NULL
              AND   t.ClosedAt     IS NULL
              AND   t.Status NOT IN ('Done', 'Completed', 'SubmittedToMentor')
              AND   NOT EXISTS (
                        SELECT 1 FROM TaskSubmissions s
                        WHERE  s.TaskId = t.Id
                    )
            ORDER   BY t.DueDate, t.Id";

        var tasks = (await _db.GetRecordsAsync<ExtensionTargetDto>(
            tasksSql, new { ProjectId = projectId }))?.ToList() ?? new();

        // Milestones for this project (per-team, project-scoped) — eligibility:
        //   - Must have a due date
        //   - Not Completed
        //   - Not finalized (CompletedAt IS NULL)
        const string milestonesSql = @"
            SELECT  'Milestone'                  AS Kind,
                    pm.Id                        AS Id,
                    COALESCE(mt.Title, '')       AS Title,
                    NULL                         AS MilestoneTitle,
                    pm.DueDate                   AS CurrentDueDate
            FROM    ProjectMilestones        pm
            JOIN    AcademicYearMilestones   aym ON aym.Id = pm.AcademicYearMilestoneId
            JOIN    MilestoneTemplates       mt  ON mt.Id  = aym.MilestoneTemplateId
            WHERE   pm.ProjectId = @ProjectId
              AND   pm.DueDate     IS NOT NULL
              AND   pm.CompletedAt IS NULL
              AND   pm.Status      <> 'Completed'
            ORDER   BY pm.DueDate, pm.Id";

        var milestones = (await _db.GetRecordsAsync<ExtensionTargetDto>(
            milestonesSql, new { ProjectId = projectId }))?.ToList() ?? new();

        // Tasks first, milestones second — keeps the dropdown stable.
        var combined = tasks.Concat(milestones).ToList();
        return Ok(combined);
    }

    // ── GET /api/project-requests/my ─────────────────────────────────────
    //
    // Returns the authenticated student's own requests with events, newest-first.
    [HttpGet("my")]
    public async Task<IActionResult> GetMy(int authUserId)
    {
        const string sql = @"
            SELECT  r.Id,
                    r.RequestType,
                    r.Title,
                    r.Status,
                    r.Priority,
                    r.CreatedAt,
                    r.UpdatedAt,
                    r.ResolutionNotes,
                    COUNT(a.Id) AS AttachmentCount
            FROM    ProjectRequests r
            JOIN    Projects    p  ON p.Id      = r.ProjectId
            JOIN    Teams       t  ON t.Id      = p.TeamId
            JOIN    TeamMembers tm ON tm.TeamId  = t.Id
            LEFT JOIN ProjectRequestAttachments a ON a.RequestId = r.Id
            WHERE   tm.UserId   = @UserId
              AND   tm.IsActive = 1
            GROUP   BY r.Id
            ORDER   BY r.CreatedAt DESC";

        var rows = (await _db.GetRecordsAsync<StudentOwnRequestDto>(sql, new { UserId = authUserId }))
                   ?.ToList() ?? new();

        if (rows.Count > 0)
        {
            var ids = string.Join(",", rows.Select(r => r.Id));
            var evtSql = $@"
                SELECT  e.Id, e.RequestId, e.UserId,
                        COALESCE(u.FirstName || ' ' || u.LastName, '[משתמש לא ידוע]') AS UserName,
                        COALESCE((SELECT ur.Role FROM UserRoles ur WHERE ur.UserId = e.UserId LIMIT 1), '') AS UserRole,
                        e.EventType, e.Content, e.OldValue, e.NewValue, e.CreatedAt
                FROM    ProjectRequestEvents e
                LEFT JOIN users u ON u.Id = e.UserId
                WHERE   e.RequestId IN ({ids})
                ORDER   BY e.CreatedAt ASC, e.Id ASC";

            var events = (await _db.GetRecordsAsync<ProjectRequestEventDto>(evtSql))
                         ?.ToList() ?? new();

            // Load event-level attachments for all retrieved events
            if (events.Count > 0)
            {
                var evtIds     = string.Join(",", events.Select(e => e.Id));
                var evtAttSql  = $@"
                    SELECT Id, EventId, OriginalFileName, StoredFileName, ContentType, SizeBytes, UploadedAt
                    FROM   ProjectRequestEventAttachments
                    WHERE  EventId IN ({evtIds})
                    ORDER  BY Id";
                var evtAtts    = (await _db.GetRecordsAsync<ProjectRequestEventAttachmentDto>(evtAttSql))?.ToList() ?? new();
                var evtAttMap  = evtAtts.GroupBy(a => a.EventId).ToDictionary(g => g.Key, g => g.ToList());
                foreach (var evt in events)
                    evt.Attachments = evtAttMap.TryGetValue(evt.Id, out var atts) ? atts : new();
            }

            var lookup = events.GroupBy(e => e.RequestId)
                               .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var row in rows)
                row.Events = lookup.TryGetValue(row.Id, out var ev) ? ev : new();
        }

        return Ok(rows);
    }

    // ── GET /api/project-requests ─────────────────────────────────────────
    //
    // Role scoping (server-side):
    //   Admin / Staff → all requests across the platform.
    //   Mentor        → only requests for projects this mentor is assigned to.
    //                   (filter via ProjectMentors.UserId = me)
    [HttpGet]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Mentor)]
    public async Task<IActionResult> GetAll(int authUserId)
    {
        // Mentor scope: limit to projects the caller mentors. Admin/Staff are
        // unscoped. Implemented with a parameterised WHERE clause so Dapper
        // safely handles the value (no string concatenation).
        bool isMentorOnly = User.IsInRole(Roles.Mentor)
                            && !User.IsInRole(Roles.Admin)
                            && !User.IsInRole(Roles.Staff);

        string whereClause = isMentorOnly
            ? "WHERE r.ProjectId IN (SELECT ProjectId FROM ProjectMentors WHERE UserId = @UserId)"
            : "";

        // Team-context fields (TeamName, TrackName, StudentNames, MentorName)
        // are joined as scalar/CSV subqueries so the list still loads in a single
        // round-trip — no N+1 — and the client can render the hover tooltip
        // without additional API calls.
        string sql = $@"
            SELECT
                r.Id,
                r.RequestType,
                r.Title,
                p.Id                             AS ProjectId,
                p.ProjectNumber,
                p.Title                          AS ProjectTitle,
                r.CreatedByUserId,
                u.FirstName || ' ' || u.LastName AS CreatedByName,
                r.CreatedAt,
                r.UpdatedAt,
                r.Status,
                r.Priority,
                a.FirstName || ' ' || a.LastName AS AssignedToName,
                COUNT(att.Id)                    AS AttachmentCount,

                t.TeamName                       AS TeamName,
                pt.Name                          AS TrackName,
                -- Each record is FullName<#>Email; records are joined by ||
                -- so names and emails stay strictly paired regardless of
                -- GROUP_CONCAT ordering, with two trivial split() calls.
                (SELECT GROUP_CONCAT(
                            su.FirstName || ' ' || su.LastName
                            || '<#>' || COALESCE(su.Email, ''),
                            '||')
                 FROM   TeamMembers stm
                 JOIN   users       su ON su.Id = stm.UserId
                 WHERE  stm.TeamId   = t.Id
                   AND  stm.IsActive = 1)        AS StudentDetailsCsv,
                (SELECT GROUP_CONCAT(
                            mu.FirstName || ' ' || mu.LastName
                            || '<#>' || COALESCE(mu.Email, ''),
                            '||')
                 FROM   ProjectMentors pm
                 JOIN   users          mu ON mu.Id = pm.UserId
                 WHERE  pm.ProjectId = p.Id)     AS MentorDetailsCsv
            FROM   ProjectRequests r
            JOIN   Projects  p ON p.Id = r.ProjectId
            LEFT JOIN Teams        t  ON t.Id  = p.TeamId
            LEFT JOIN ProjectTypes pt ON pt.Id = p.ProjectTypeId
            JOIN   users     u ON u.Id = r.CreatedByUserId
            LEFT JOIN users  a ON a.Id = r.AssignedToUserId
            LEFT JOIN ProjectRequestAttachments att ON att.RequestId = r.Id
            {whereClause}
            GROUP  BY r.Id
            ORDER  BY r.CreatedAt DESC";

        var rows = (await _db.GetRecordsAsync<ProjectRequestRowWithTeam>(
                        sql, new { UserId = authUserId }))?.ToList()
                   ?? new List<ProjectRequestRowWithTeam>();

        var result = rows.Select(MapToRowDto).ToList();
        return Ok(result);
    }

    // Splits the GROUP_CONCAT CSV columns into the public DTO's list/string
    // fields. '||' is the separator used in the SQL above so it survives names
    // that contain commas.
    private static ProjectRequestRowDto MapToRowDto(ProjectRequestRowWithTeam r)
    {
        var (studentNames, studentEmails) = SplitNameEmailPairs(r.StudentDetailsCsv);
        var (mentorNames,  mentorEmails)  = SplitNameEmailPairs(r.MentorDetailsCsv);

        return new ProjectRequestRowDto
        {
            Id              = r.Id,
            RequestType     = r.RequestType,
            Title           = r.Title,
            ProjectId       = r.ProjectId,
            ProjectNumber   = r.ProjectNumber,
            ProjectTitle    = r.ProjectTitle,
            CreatedByUserId = r.CreatedByUserId,
            CreatedByName   = r.CreatedByName,
            CreatedAt       = r.CreatedAt,
            UpdatedAt       = r.UpdatedAt,
            Status          = r.Status,
            Priority        = r.Priority,
            AssignedToName  = r.AssignedToName,
            AttachmentCount = r.AttachmentCount,
            TeamName        = string.IsNullOrWhiteSpace(r.TeamName)  ? null : r.TeamName,
            TrackName       = string.IsNullOrWhiteSpace(r.TrackName) ? null : r.TrackName,
            StudentNames    = studentNames,
            StudentEmails   = studentEmails,
            MentorNames     = mentorNames,
            MentorEmails    = mentorEmails,
        };
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

    // ── GET /api/project-requests/{id} ────────────────────────────────────
    //
    // Role scoping (server-side):
    //   Admin / Staff → any request.
    //   Mentor        → only requests for projects the mentor is assigned to.
    [HttpGet("{id:int}")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Mentor)]
    public async Task<IActionResult> GetById(int id, int authUserId)
    {
        const string sql = @"
            SELECT
                r.Id,
                r.RequestType,
                r.Title,
                r.Description,
                p.Id                             AS ProjectId,
                p.ProjectNumber,
                p.Title                          AS ProjectTitle,
                r.CreatedByUserId,
                u.FirstName || ' ' || u.LastName AS CreatedByName,
                r.CreatedAt,
                r.UpdatedAt,
                r.Status,
                r.Priority,
                r.ResolutionNotes,
                r.AssignedToUserId,
                a.FirstName || ' ' || a.LastName AS AssignedToName,
                COUNT(att.Id)                    AS AttachmentCount
            FROM   ProjectRequests r
            JOIN   Projects  p ON p.Id = r.ProjectId
            JOIN   users     u ON u.Id = r.CreatedByUserId
            LEFT JOIN users  a ON a.Id = r.AssignedToUserId
            LEFT JOIN ProjectRequestAttachments att ON att.RequestId = r.Id
            WHERE  r.Id = @Id
            GROUP  BY r.Id";

        var row = (await _db.GetRecordsAsync<ProjectRequestDetailDto>(sql, new { Id = id }))
                  ?.FirstOrDefault();

        if (row is null) return NotFound("הבקשה לא נמצאה");

        // Mentor scope: a mentor can only view requests for projects they mentor.
        if (User.IsInRole(Roles.Mentor)
            && !User.IsInRole(Roles.Admin)
            && !User.IsInRole(Roles.Staff))
        {
            const string mentorOfSql = @"
                SELECT 1 FROM ProjectMentors
                WHERE  ProjectId = @ProjectId AND UserId = @UserId
                LIMIT  1";
            var ok = (await _db.GetRecordsAsync<int>(
                mentorOfSql, new { ProjectId = row.ProjectId, UserId = authUserId }))
                ?.FirstOrDefault();
            if (ok != 1) return NotFound("הבקשה לא נמצאה");
        }

        // Attachments
        const string attachSql = @"
            SELECT  Id, RequestId, OriginalFileName, StoredFileName,
                    ContentType, SizeBytes, UploadedAt
            FROM    ProjectRequestAttachments
            WHERE   RequestId = @RequestId
            ORDER   BY Id";

        row.Attachments = (await _db.GetRecordsAsync<ProjectRequestAttachmentDto>(
            attachSql, new { RequestId = id }))?.ToList() ?? new();

        // Events thread
        const string evtSql = @"
            SELECT  e.Id, e.RequestId, e.UserId,
                    COALESCE(u.FirstName || ' ' || u.LastName, '[משתמש לא ידוע]') AS UserName,
                    COALESCE((SELECT ur.Role FROM UserRoles ur WHERE ur.UserId = e.UserId LIMIT 1), '') AS UserRole,
                    e.EventType, e.Content, e.OldValue, e.NewValue, e.CreatedAt
            FROM    ProjectRequestEvents e
            LEFT JOIN users u ON u.Id = e.UserId
            WHERE   e.RequestId = @RequestId
            ORDER   BY e.CreatedAt ASC, e.Id ASC";

        row.Events = (await _db.GetRecordsAsync<ProjectRequestEventDto>(
            evtSql, new { RequestId = id }))?.ToList() ?? new();

        // Load event-level attachments for all events in this thread
        if (row.Events.Count > 0)
        {
            var evtIds    = string.Join(",", row.Events.Select(e => e.Id));
            var evtAttSql = $@"
                SELECT Id, EventId, OriginalFileName, StoredFileName, ContentType, SizeBytes, UploadedAt
                FROM   ProjectRequestEventAttachments
                WHERE  EventId IN ({evtIds})
                ORDER  BY Id";
            var evtAtts   = (await _db.GetRecordsAsync<ProjectRequestEventAttachmentDto>(evtAttSql))?.ToList() ?? new();
            var evtAttMap = evtAtts.GroupBy(a => a.EventId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var evt in row.Events)
                evt.Attachments = evtAttMap.TryGetValue(evt.Id, out var atts) ? atts : new();
        }

        // Extension side-row — populated only for Extension type requests.
        if (row.RequestType == RequestTypes.Extension)
        {
            const string extSql = @"
                SELECT  e.Id,
                        e.RequestId,
                        e.TaskId,
                        t.Title                                      AS TaskTitle,
                        e.ProjectMilestoneId,
                        COALESCE(mt.Title, '')                       AS MilestoneTitle,
                        e.CurrentDueDate,
                        e.RequestedDueDate,
                        e.Reason,
                        e.MentorDecision,
                        e.MentorDecidedAt,
                        md.FirstName || ' ' || md.LastName           AS MentorDecidedByName,
                        e.MentorNotes,
                        e.LecturerDecision,
                        e.LecturerDecidedAt,
                        ld.FirstName || ' ' || ld.LastName           AS LecturerDecidedByName,
                        e.LecturerNotes,
                        e.FinalDecision,
                        e.ApprovedDueDate
                FROM    ProjectRequestExtensions e
                LEFT JOIN Tasks                    t   ON t.Id   = e.TaskId
                LEFT JOIN ProjectMilestones        pm  ON pm.Id  = e.ProjectMilestoneId
                LEFT JOIN AcademicYearMilestones   aym ON aym.Id = pm.AcademicYearMilestoneId
                LEFT JOIN MilestoneTemplates       mt  ON mt.Id  = aym.MilestoneTemplateId
                LEFT JOIN users                    md  ON md.Id  = e.MentorDecidedByUserId
                LEFT JOIN users                    ld  ON ld.Id  = e.LecturerDecidedByUserId
                WHERE   e.RequestId = @RequestId
                LIMIT   1";

            row.Extension = (await _db.GetRecordsAsync<ExtensionRequestInfoDto>(
                extSql, new { RequestId = id }))?.FirstOrDefault();
        }

        return Ok(row);
    }

    // ── GET /api/project-requests/{id}/debug-events ──────────────────────
    //
    // Debug endpoint: returns ALL raw event rows for a request without any
    // JOIN filtering, so missing events (e.g. dropped by a bad JOIN) become
    // visible.  Restricted to Admin / Staff.
    [HttpGet("{id:int}/debug-events")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> DebugEvents(int id, int authUserId)
    {
        // Verify the request exists first
        var exists = (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM ProjectRequests WHERE Id = @Id", new { Id = id }))
            .FirstOrDefault();
        if (exists == 0) return NotFound("הבקשה לא נמצאה");

        // Raw event rows — LEFT JOIN so we see everything even with orphaned UserId
        const string evtSql = @"
            SELECT  e.Id,
                    e.RequestId,
                    e.UserId,
                    COALESCE(u.FirstName || ' ' || u.LastName, '[לא נמצא: UserId=' || e.UserId || ']') AS UserName,
                    COALESCE((SELECT ur.Role FROM UserRoles ur WHERE ur.UserId = e.UserId LIMIT 1), '') AS UserRole,
                    e.EventType,
                    e.Content,
                    e.OldValue,
                    e.NewValue,
                    e.CreatedAt
            FROM    ProjectRequestEvents e
            LEFT JOIN users u ON u.Id = e.UserId
            WHERE   e.RequestId = @RequestId
            ORDER   BY e.CreatedAt ASC, e.Id ASC";

        var events = (await _db.GetRecordsAsync<ProjectRequestEventDto>(
            evtSql, new { RequestId = id }))?.ToList() ?? new();

        return Ok(new
        {
            RequestId  = id,
            EventCount = events.Count,
            Events     = events.Select(e => new
            {
                e.Id,
                e.UserId,
                e.UserName,
                e.UserRole,
                e.EventType,
                e.Content,
                e.OldValue,
                e.NewValue,
                e.CreatedAt,
            }),
        });
    }

    // ── POST /api/project-requests ────────────────────────────────────────
    //
    // Creates a new request. Priority always defaults to 'Normal'.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequestRequest req, int authUserId)
    {
        if (req.ProjectId <= 0)
            return BadRequest("יש לספק מזהה פרויקט תקין");

        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("כותרת הבקשה היא שדה חובה");

        if (!RequestTypes.All.Contains(req.RequestType))
            return BadRequest("סוג בקשה לא תקין");

        if (req.Attachments.Count > MaxAttachments)
            return BadRequest($"ניתן לצרף עד {MaxAttachments} תמונות לבקשה");

        for (int i = 0; i < req.Attachments.Count; i++)
        {
            var att = req.Attachments[i];
            if (string.IsNullOrWhiteSpace(att.OriginalFileName))
                return BadRequest($"קובץ {i + 1}: חסר שם קובץ");
            if (string.IsNullOrWhiteSpace(att.FileBase64))
                return BadRequest($"קובץ {i + 1}: חסר תוכן קובץ");
            if (att.SizeBytes > MaxImageBytes)
                return BadRequest($"קובץ \"{att.OriginalFileName}\" חורג מהגודל המקסימלי (5 MB)");
            if (!AllowedImageTypes.Contains(att.ContentType ?? ""))
            {
                string ext = Path.GetExtension(att.OriginalFileName).TrimStart('.').ToLowerInvariant();
                if (ext is not ("jpg" or "jpeg" or "png" or "webp"))
                    return BadRequest($"סוג הקובץ \"{ext}\" אינו נתמך. נתמכים: jpg, png, webp");
            }
        }

        const string checkSql = "SELECT COUNT(1) FROM Projects WHERE Id = @Id";
        var count = (await _db.GetRecordsAsync<int>(checkSql, new { Id = req.ProjectId }))
                    .FirstOrDefault();
        if (count == 0) return NotFound("הפרויקט לא נמצא");

        // ── Extension-specific validation (when RequestType = Extension) ──
        // The student's modal sends TargetTaskId xor TargetMilestoneId plus
        // RequestedDueDate. General "אחר / כללי" extension requests are still
        // allowed (no target → no override later) — in that case the date and
        // target may be omitted.
        DateTime? extCurrentDueDate = null;
        if (req.RequestType == RequestTypes.Extension)
        {
            if (req.TargetTaskId.HasValue && req.TargetMilestoneId.HasValue)
                return BadRequest("ניתן לבחור משימה או אבן דרך — לא שניהם");

            if (req.TargetTaskId.HasValue)
            {
                // Load the task with the same eligibility signals the picker uses.
                // We deliberately fetch the raw status fields (instead of relying on
                // the picker's filtered view) so we can produce the right Hebrew
                // error message for each ineligibility reason.
                const string taskSql = @"
                    SELECT  t.DueDate           AS DueDate,
                            t.Status            AS Status,
                            t.ClosedAt          AS ClosedAt,
                            t.IsSubmission      AS IsSubmission,
                            (SELECT COUNT(*) FROM TaskSubmissions s WHERE s.TaskId = t.Id)
                                                AS SubmissionCount
                    FROM    Tasks t
                    WHERE   t.Id = @TaskId AND t.ProjectId = @ProjectId
                    LIMIT   1";
                var taskRow = (await _db.GetRecordsAsync<TaskEligibilityRow>(
                    taskSql, new { TaskId = req.TargetTaskId, ProjectId = req.ProjectId }))?.FirstOrDefault();

                if (taskRow is null)
                    return BadRequest("המשימה אינה שייכת לפרויקט שלך");
                if (taskRow.SubmissionCount > 0)
                    return BadRequest("לא ניתן לבקש דחייה עבור משימה שכבר הוגשה");
                if (taskRow.Status == "Done"
                    || taskRow.Status == "Completed"
                    || taskRow.Status == "SubmittedToMentor"
                    || taskRow.ClosedAt is not null
                    || taskRow.DueDate is null
                    || taskRow.IsSubmission == 0)
                    return BadRequest("הפריט שנבחר אינו זמין לבקשת דחייה");

                extCurrentDueDate = taskRow.DueDate;
            }
            else if (req.TargetMilestoneId.HasValue)
            {
                // Same approach for milestones — fetch raw fields, then map to a
                // specific error message per reason.
                const string msSql = @"
                    SELECT  pm.DueDate     AS DueDate,
                            pm.Status      AS Status,
                            pm.CompletedAt AS CompletedAt
                    FROM    ProjectMilestones pm
                    WHERE   pm.Id = @MsId AND pm.ProjectId = @ProjectId
                    LIMIT   1";
                var msRow = (await _db.GetRecordsAsync<MilestoneEligibilityRow>(
                    msSql, new { MsId = req.TargetMilestoneId, ProjectId = req.ProjectId }))?.FirstOrDefault();

                if (msRow is null)
                    return BadRequest("אבן הדרך אינה שייכת לפרויקט שלך");
                if (msRow.Status == "Completed" || msRow.CompletedAt is not null)
                    return BadRequest("לא ניתן לבקש דחייה עבור אבן דרך שהושלמה או נסגרה");
                if (msRow.DueDate is null)
                    return BadRequest("הפריט שנבחר אינו זמין לבקשת דחייה");

                extCurrentDueDate = msRow.DueDate;
            }

            // RequestedDueDate is required when a target is selected.
            bool hasTarget = req.TargetTaskId.HasValue || req.TargetMilestoneId.HasValue;
            if (hasTarget && req.RequestedDueDate is null)
                return BadRequest("יש לבחור תאריך מבוקש חדש");

            // Sanity: the new date must be later than the current due date.
            if (hasTarget && extCurrentDueDate.HasValue && req.RequestedDueDate is not null
                && req.RequestedDueDate.Value.Date <= extCurrentDueDate.Value.Date)
                return BadRequest("התאריך המבוקש חייב להיות אחרי תאריך ההגשה הנוכחי");
        }

        const string insertSql = @"
            INSERT INTO ProjectRequests
                (ProjectId, CreatedByUserId, RequestType, Title, Description, Status, Priority)
            VALUES
                (@ProjectId, @UserId, @RequestType, @Title, @Description, 'New', 'Normal')";

        int newId = await _db.InsertReturnIdAsync(insertSql, new
        {
            ProjectId   = req.ProjectId,
            UserId      = authUserId,
            RequestType = req.RequestType,
            Title       = req.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
        });

        if (newId == 0) return StatusCode(500, "שגיאה ביצירת הבקשה");

        // ── Extension side-row (only for Extension type) ──────────────────
        if (req.RequestType == RequestTypes.Extension)
        {
            // RequestedDueDate is required by the table even for general
            // ("אחר / כללי") requests. When the student didn't supply one,
            // store the description's submission moment as a placeholder.
            DateTime requestedDate = req.RequestedDueDate ?? DateTime.Today;

            const string extInsertSql = @"
                INSERT INTO ProjectRequestExtensions
                    (RequestId, TaskId, ProjectMilestoneId,
                     CurrentDueDate, RequestedDueDate, Reason,
                     MentorDecision, LecturerDecision, FinalDecision)
                VALUES
                    (@RequestId, @TaskId, @MilestoneId,
                     @CurrentDueDate, @RequestedDueDate, @Reason,
                     'Pending', 'NotRequired', 'Pending')";

            await _db.SaveDataAsync(extInsertSql, new
            {
                RequestId        = newId,
                TaskId           = req.TargetTaskId,
                MilestoneId      = req.TargetMilestoneId,
                CurrentDueDate   = extCurrentDueDate?.ToString("yyyy-MM-dd"),
                RequestedDueDate = requestedDate.ToString("yyyy-MM-dd"),
                Reason           = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            });
        }

        const string insertAttachSql = @"
            INSERT INTO ProjectRequestAttachments
                (RequestId, OriginalFileName, StoredFileName, ContentType, SizeBytes, UploadedAt)
            VALUES
                (@RequestId, @OriginalFileName, @StoredFileName, @ContentType, @SizeBytes, datetime('now'))";

        foreach (var att in req.Attachments)
        {
            string storedFileName;
            try
            {
                storedFileName = await _filesManage.SaveRawFile(
                    att.FileBase64, att.OriginalFileName, Container);
            }
            catch { continue; }

            await _db.SaveDataAsync(insertAttachSql, new
            {
                RequestId        = newId,
                OriginalFileName = att.OriginalFileName,
                StoredFileName   = storedFileName,
                ContentType      = string.IsNullOrWhiteSpace(att.ContentType)
                                       ? "application/octet-stream"
                                       : att.ContentType,
                SizeBytes        = att.SizeBytes,
            });
        }

        return Ok(new { id = newId });
    }

    // ── POST /api/project-requests/{id}/handle ────────────────────────────
    //
    // Atomic handling action: updates status / priority / assignee and records
    // events for every detected change.  At least one change or a comment
    // must be present — empty no-op calls are rejected.
    [HttpPost("{id:int}/handle")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Handle(
        int id, [FromBody] HandleProjectRequestRequest req, int authUserId)
    {
        // ── Validate inputs ──────────────────────────────────────────────
        if (!RequestStatuses.All.Contains(req.NewStatus))
            return BadRequest("סטטוס לא תקין");

        string newPriority = string.IsNullOrWhiteSpace(req.NewPriority)
            ? RequestPriorities.Normal
            : req.NewPriority;

        if (!RequestPriorities.All.Contains(newPriority))
            return BadRequest("עדיפות לא תקינה");

        // ── Load current state ────────────────────────────────────────────
        const string currSql = @"
            SELECT  r.Id,
                    r.Title,
                    r.ProjectId,
                    r.Status,
                    r.Priority,
                    r.AssignedToUserId,
                    CASE WHEN r.AssignedToUserId IS NULL THEN NULL
                         ELSE (SELECT u.FirstName || ' ' || u.LastName
                               FROM   users u
                               WHERE  u.Id = r.AssignedToUserId)
                    END AS AssignedToName
            FROM    ProjectRequests r
            WHERE   r.Id = @Id";

        var curr = (await _db.GetRecordsAsync<CurrentRequestRow>(currSql, new { Id = id }))
                   ?.FirstOrDefault();

        if (curr is null) return NotFound("הבקשה לא נמצאה");

        bool hasComment  = !string.IsNullOrWhiteSpace(req.Comment);
        bool statusChg   = curr.Status   != req.NewStatus;
        bool priorityChg = curr.Priority != newPriority;
        bool assigneeChg = curr.AssignedToUserId != req.AssignedToUserId;

        if (!hasComment && !statusChg && !priorityChg && !assigneeChg)
            return BadRequest("לא בוצע שינוי — יש לבחור פעולה או להוסיף תגובה");

        // ── Resolve new assignee name ─────────────────────────────────────
        string? newAssigneeName = null;
        if (req.AssignedToUserId.HasValue)
        {
            var nameSql = "SELECT FirstName || ' ' || LastName FROM users WHERE Id = @Id";
            newAssigneeName = (await _db.GetRecordsAsync<string>(
                nameSql, new { Id = req.AssignedToUserId.Value }))?.FirstOrDefault();
            if (newAssigneeName is null) return BadRequest("המשתמש המשויך לא נמצא");
        }

        // ── Update request row ────────────────────────────────────────────
        const string updateSql = @"
            UPDATE ProjectRequests
            SET    Status          = @Status,
                   Priority        = @Priority,
                   AssignedToUserId = @AssignedToUserId,
                   UpdatedAt       = datetime('now')
            WHERE  Id = @Id";

        await _db.SaveDataAsync(updateSql, new
        {
            Status           = req.NewStatus,
            Priority         = newPriority,
            AssignedToUserId = req.AssignedToUserId,
            Id               = id,
        });

        // ── Record events ─────────────────────────────────────────────────
        const string insertEvtSql = @"
            INSERT INTO ProjectRequestEvents
                (RequestId, UserId, EventType, Content, OldValue, NewValue, CreatedAt)
            VALUES
                (@RequestId, @UserId, @EventType, @Content, @OldValue, @NewValue, datetime('now'))";

        if (statusChg)
            await _db.SaveDataAsync(insertEvtSql, new
            {
                RequestId = id,
                UserId    = authUserId,
                EventType = RequestEventTypes.StatusChange,
                Content   = (string?)null,
                OldValue  = RequestStatuses.Label(curr.Status),
                NewValue  = RequestStatuses.Label(req.NewStatus),
            });

        if (priorityChg)
            await _db.SaveDataAsync(insertEvtSql, new
            {
                RequestId = id,
                UserId    = authUserId,
                EventType = RequestEventTypes.PriorityChange,
                Content   = (string?)null,
                OldValue  = RequestPriorities.Label(curr.Priority),
                NewValue  = RequestPriorities.Label(newPriority),
            });

        if (assigneeChg)
            await _db.SaveDataAsync(insertEvtSql, new
            {
                RequestId = id,
                UserId    = authUserId,
                EventType = RequestEventTypes.AssigneeChange,
                Content   = (string?)null,
                OldValue  = curr.AssignedToName,
                NewValue  = newAssigneeName,
            });

        if (hasComment)
        {
            // Validate event-level attachments (if any)
            if (req.Attachments.Count > MaxEventAttachments)
                return BadRequest($"ניתן לצרף עד {MaxEventAttachments} קבצים לתגובה");

            foreach (var att in req.Attachments)
            {
                if (att.SizeBytes > MaxEventAttachmentBytes)
                    return BadRequest($"קובץ \"{att.OriginalFileName}\" חורג מהגודל המקסימלי (5 MB)");
                string ext = Path.GetExtension(att.OriginalFileName ?? "").TrimStart('.').ToLowerInvariant();
                if (!AllowedEventAttachmentMimes.Contains(att.ContentType ?? "") && !AllowedEventAttachmentExts.Contains(ext))
                    return BadRequest($"סוג הקובץ \"{ext}\" אינו נתמך בתגובות");
            }

            int commentEventId = await _db.InsertReturnIdAsync(insertEvtSql, new
            {
                RequestId = id,
                UserId    = authUserId,
                EventType = RequestEventTypes.Comment,
                Content   = req.Comment!.Trim(),
                OldValue  = (string?)null,
                NewValue  = (string?)null,
            });

            if (commentEventId > 0)
                await SaveEventAttachmentsAsync(req.Attachments, commentEventId, id);
        }

        // ── Notify team members when request is returned for more info ────
        if (req.NewStatus == RequestStatuses.NeedsInfo)
        {
            try
            {
                const string membersSql = @"
                    SELECT tm.UserId
                    FROM   ProjectRequests r
                    JOIN   Projects        p  ON p.Id      = r.ProjectId
                    JOIN   Teams           t  ON t.Id      = p.TeamId
                    JOIN   TeamMembers     tm ON tm.TeamId = t.Id
                    WHERE  r.Id        = @RequestId
                      AND  tm.IsActive = 1";

                var studentIds = (await _db.GetRecordsAsync<int>(
                    membersSql, new { RequestId = id }))?.ToList() ?? new();

                await NotificationHelper.CreateForUsersAsync(
                    _db, studentIds,
                    title:             "הבקשה שלך הוחזרה עם הערות",
                    message:           $"הבקשה \"{curr.Title}\" הוחזרה אליך עם הערות. נא לעיין ולהשיב.",
                    type:              "RequestReturned",
                    relatedEntityType: "ProjectRequest",
                    relatedEntityId:   id);
            }
            catch { /* notifications are best-effort */ }
        }

        return Ok();
    }

    // ── POST /api/project-requests/{id}/reply ────────────────────────────
    //
    // Student reply: appends a Comment event to the request thread.
    // Validates that the caller is a team member of the request's project.
    // If the request is in NeedsInfo status, it automatically transitions
    // back to InProgress so the staff side knows a response arrived.
    [HttpPost("{id:int}/reply")]
    public async Task<IActionResult> Reply(
        int id, [FromBody] StudentReplyRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.Comment))
            return BadRequest("תגובה לא יכולה להיות ריקה");

        if (req.Comment.Trim().Length > 2000)
            return BadRequest("תגובה ארוכה מדי (מקסימום 2000 תווים)");

        // Validate event-level attachments
        if (req.Attachments.Count > MaxEventAttachments)
            return BadRequest($"ניתן לצרף עד {MaxEventAttachments} קבצים לתגובה");

        foreach (var att in req.Attachments)
        {
            if (att.SizeBytes > MaxEventAttachmentBytes)
                return BadRequest($"קובץ \"{att.OriginalFileName}\" חורג מהגודל המקסימלי (5 MB)");
            string ext = Path.GetExtension(att.OriginalFileName ?? "").TrimStart('.').ToLowerInvariant();
            if (!AllowedEventAttachmentMimes.Contains(att.ContentType ?? "") && !AllowedEventAttachmentExts.Contains(ext))
                return BadRequest($"סוג הקובץ \"{ext}\" אינו נתמך בתגובות");
        }

        // ── Verify caller belongs to the request's project ───────────────
        const string ownerSql = @"
            SELECT  r.Id, r.Status
            FROM    ProjectRequests r
            JOIN    Projects    p  ON p.Id      = r.ProjectId
            JOIN    Teams       t  ON t.Id      = p.TeamId
            JOIN    TeamMembers tm ON tm.TeamId  = t.Id
            WHERE   r.Id        = @RequestId
              AND   tm.UserId   = @UserId
              AND   tm.IsActive = 1";

        var row = (await _db.GetRecordsAsync<RequestStatusRow>(
            ownerSql, new { RequestId = id, UserId = authUserId }))
            ?.FirstOrDefault();

        if (row is null)
            return NotFound("הבקשה לא נמצאה או שאינך חלק מהפרויקט");

        // Do not allow replies on closed/resolved requests
        if (row.Status is RequestStatuses.Resolved or RequestStatuses.Closed)
            return BadRequest("לא ניתן להוסיף תגובה לבקשה שנסגרה");

        // ── Insert comment event ─────────────────────────────────────────
        const string insertEvtSql = @"
            INSERT INTO ProjectRequestEvents
                (RequestId, UserId, EventType, Content, OldValue, NewValue, CreatedAt)
            VALUES
                (@RequestId, @UserId, @EventType, @Content, NULL, NULL, datetime('now'))";

        int commentEventId = await _db.InsertReturnIdAsync(insertEvtSql, new
        {
            RequestId = id,
            UserId    = authUserId,
            EventType = RequestEventTypes.Comment,
            Content   = req.Comment.Trim(),
        });

        // Save any event-level attachments for this comment
        if (commentEventId > 0)
            await SaveEventAttachmentsAsync(req.Attachments, commentEventId, id);

        // ── Transition NeedsInfo → WaitingForStaff and record status event ─
        bool wasNeedsInfo = row.Status == RequestStatuses.NeedsInfo;
        if (wasNeedsInfo)
        {
            await _db.SaveDataAsync(
                @"UPDATE ProjectRequests
                  SET Status = 'WaitingForStaff', UpdatedAt = datetime('now')
                  WHERE Id = @Id",
                new { Id = id });

            const string insertStatusEvt = @"
                INSERT INTO ProjectRequestEvents
                    (RequestId, UserId, EventType, Content, OldValue, NewValue, CreatedAt)
                VALUES
                    (@RequestId, @UserId, @EventType, NULL, @OldValue, @NewValue, datetime('now'))";

            await _db.SaveDataAsync(insertStatusEvt, new
            {
                RequestId = id,
                UserId    = authUserId,
                EventType = RequestEventTypes.StatusChange,
                OldValue  = RequestStatuses.Label(RequestStatuses.NeedsInfo),
                NewValue  = RequestStatuses.Label(RequestStatuses.WaitingForStaff),
            });
        }
        else
        {
            await _db.SaveDataAsync(
                "UPDATE ProjectRequests SET UpdatedAt = datetime('now') WHERE Id = @Id",
                new { Id = id });
        }

        return Ok(new { transitionedToWaitingForStaff = wasNeedsInfo });
    }

    // ── PATCH /api/project-requests/{id} (legacy) ─────────────────────────
    //
    // Kept for backward compatibility. New code should use POST /{id}/handle.
    [HttpPatch("{id:int}")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Update(
        int id, [FromBody] UpdateProjectRequestRequest req, int authUserId)
    {
        if (!RequestStatuses.All.Contains(req.Status))
            return BadRequest("סטטוס לא תקין");

        string priority = string.IsNullOrWhiteSpace(req.Priority)
            ? RequestPriorities.Normal
            : req.Priority;

        if (!RequestPriorities.All.Contains(priority))
            return BadRequest("עדיפות לא תקינה");

        const string sql = @"
            UPDATE ProjectRequests
            SET    Status          = @Status,
                   Priority        = @Priority,
                   ResolutionNotes = @ResolutionNotes,
                   UpdatedAt       = datetime('now')
            WHERE  Id = @Id";

        int affected = await _db.SaveDataAsync(sql, new
        {
            Status          = req.Status,
            Priority        = priority,
            ResolutionNotes = string.IsNullOrWhiteSpace(req.ResolutionNotes)
                              ? null : req.ResolutionNotes.Trim(),
            Id              = id,
        });

        if (affected == 0) return NotFound("הבקשה לא נמצאה");
        return Ok();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task SaveEventAttachmentsAsync(
        List<RequestAttachmentUploadRequest> attachments, int eventId, int requestId)
    {
        if (attachments.Count == 0) return;

        const string sql = @"
            INSERT INTO ProjectRequestEventAttachments
                (EventId, RequestId, OriginalFileName, StoredFileName, ContentType, SizeBytes, UploadedAt)
            VALUES
                (@EventId, @RequestId, @OriginalFileName, @StoredFileName, @ContentType, @SizeBytes, datetime('now'))";

        foreach (var att in attachments)
        {
            string storedFileName;
            try
            {
                storedFileName = await _filesManage.SaveRawFile(att.FileBase64, att.OriginalFileName, Container);
            }
            catch { continue; }

            await _db.SaveDataAsync(sql, new
            {
                EventId          = eventId,
                RequestId        = requestId,
                OriginalFileName = att.OriginalFileName,
                StoredFileName   = storedFileName,
                ContentType      = string.IsNullOrWhiteSpace(att.ContentType)
                                       ? "application/octet-stream"
                                       : att.ContentType,
                SizeBytes        = att.SizeBytes,
            });
        }
    }

    // ── POST /api/project-requests/{id}/extension/decision ───────────────────
    //
    // Two-stage extension-request decision flow:
    //   Stage = Mentor   (caller must mentor this project, OR be Admin/Staff)
    //     Decision = Approved   → write team-specific override + close request
    //     Decision = Rejected   → close request, no override
    //     Decision = Escalated  → status becomes InProgress; Lecturer stage opens
    //
    //   Stage = Lecturer (caller must be Admin/Staff; MentorDecision must be
    //                     Escalated)
    //     Decision = Approved   → write team-specific override + close request
    //     Decision = Rejected   → close request, no override
    //
    // Approved decisions that target a Task or Milestone REQUIRE ApprovedDueDate.
    // General requests (no target) need no date.
    [HttpPost("{id:int}/extension/decision")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Mentor)]
    public async Task<IActionResult> ExtensionDecision(
        int id, [FromBody] ExtensionDecisionRequest req, int authUserId)
    {
        if (req is null) return BadRequest("גוף בקשה ריק");

        // Validate Stage / Decision
        var stage    = (req.Stage    ?? "").Trim();
        var decision = (req.Decision ?? "").Trim();
        if (stage != "Mentor" && stage != "Lecturer")
            return BadRequest("שלב החלטה לא תקין");
        if (decision != ExtensionDecisionStatuses.Approved
            && decision != ExtensionDecisionStatuses.Rejected
            && decision != ExtensionDecisionStatuses.Escalated)
            return BadRequest("החלטה לא תקינה");
        if (stage == "Lecturer" && decision == ExtensionDecisionStatuses.Escalated)
            return BadRequest("לא ניתן להעביר בקשה הלאה משלב המרצה");

        // Load the request + extension row
        const string loadSql = @"
            SELECT  r.Id, r.ProjectId, r.RequestType, r.Status,
                    p.TeamId,
                    e.Id                  AS ExtId,
                    e.TaskId, e.ProjectMilestoneId,
                    e.MentorDecision, e.LecturerDecision, e.FinalDecision,
                    e.RequestedDueDate, e.CurrentDueDate
            FROM    ProjectRequests r
            JOIN    Projects        p ON p.Id = r.ProjectId
            LEFT JOIN ProjectRequestExtensions e ON e.RequestId = r.Id
            WHERE   r.Id = @Id
            LIMIT   1";

        var ctx = (await _db.GetRecordsAsync<ExtensionDecisionLoadRow>(
                       loadSql, new { Id = id }))?.FirstOrDefault();
        if (ctx is null) return NotFound("הבקשה לא נמצאה");
        if (ctx.RequestType != RequestTypes.Extension)
            return BadRequest("ההחלטה תקפה רק לבקשות דחייה");
        if (ctx.ExtId is null)
            return StatusCode(500, "נתוני הבקשה אינם תקינים");
        if (ctx.FinalDecision != ExtensionDecisionStatuses.Pending)
            return BadRequest("ההחלטה כבר נסגרה");

        // ── Authorization for the chosen stage ───────────────────────────
        bool isAdminOrStaff = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Staff);

        if (stage == "Mentor")
        {
            if (!isAdminOrStaff)
            {
                const string mentorOfSql = @"
                    SELECT 1 FROM ProjectMentors
                    WHERE  ProjectId = @ProjectId AND UserId = @UserId LIMIT 1";
                var ok = (await _db.GetRecordsAsync<int>(
                    mentorOfSql, new { ProjectId = ctx.ProjectId, UserId = authUserId }))
                    ?.FirstOrDefault();
                if (ok != 1) return Forbid();
            }
            if (ctx.MentorDecision != ExtensionDecisionStatuses.Pending)
                return BadRequest("המנחה כבר החליט בבקשה זו");
        }
        else // Lecturer stage
        {
            if (!isAdminOrStaff) return Forbid();
            if (ctx.MentorDecision != ExtensionDecisionStatuses.Escalated)
                return BadRequest("הבקשה לא הועברה לבדיקת מרצה");
            if (ctx.LecturerDecision != ExtensionDecisionStatuses.Pending)
                return BadRequest("המרצה כבר החליט בבקשה זו");
        }

        // ── Date validation when approving with a target ─────────────────
        bool hasTaskTarget      = ctx.TaskId.HasValue;
        bool hasMilestoneTarget = ctx.ProjectMilestoneId.HasValue;
        bool hasAnyTarget       = hasTaskTarget || hasMilestoneTarget;

        if (decision == ExtensionDecisionStatuses.Approved && hasAnyTarget)
        {
            if (req.ApprovedDueDate is null)
                return BadRequest("חובה להזין תאריך מאושר");
            if (req.ApprovedDueDate.Value.Date < DateTime.Today)
                return BadRequest("תאריך מאושר לא יכול להיות בעבר");
        }

        // ── Apply: extension side-row + ProjectRequests + override ───────
        DateTime? approvedDate = (decision == ExtensionDecisionStatuses.Approved && hasAnyTarget)
            ? req.ApprovedDueDate : null;

        if (stage == "Mentor")
        {
            string finalDecision = decision switch
            {
                ExtensionDecisionStatuses.Approved  => ExtensionDecisionStatuses.Approved,
                ExtensionDecisionStatuses.Rejected  => ExtensionDecisionStatuses.Rejected,
                _                                    => ExtensionDecisionStatuses.Pending, // Escalated
            };
            string lecturerDecision = decision == ExtensionDecisionStatuses.Escalated
                ? ExtensionDecisionStatuses.Pending
                : ExtensionDecisionStatuses.NotRequired;

            const string updExt = @"
                UPDATE ProjectRequestExtensions
                SET    MentorDecision        = @MentorDecision,
                       MentorDecidedByUserId = @MentorBy,
                       MentorDecidedAt       = datetime('now'),
                       MentorNotes           = @Notes,
                       LecturerDecision      = @LecturerDecision,
                       FinalDecision         = @FinalDecision,
                       ApprovedDueDate       = COALESCE(@ApprovedDueDate, ApprovedDueDate)
                WHERE  RequestId = @Id";
            await _db.SaveDataAsync(updExt, new
            {
                MentorDecision   = decision,
                MentorBy         = authUserId,
                Notes            = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes!.Trim(),
                LecturerDecision = lecturerDecision,
                FinalDecision    = finalDecision,
                ApprovedDueDate  = approvedDate?.ToString("yyyy-MM-dd"),
                Id               = id,
            });
        }
        else // Lecturer
        {
            string finalDecision = decision; // Approved or Rejected — both terminal at this stage
            const string updExt = @"
                UPDATE ProjectRequestExtensions
                SET    LecturerDecision        = @LecturerDecision,
                       LecturerDecidedByUserId = @LecturerBy,
                       LecturerDecidedAt       = datetime('now'),
                       LecturerNotes           = @Notes,
                       FinalDecision           = @FinalDecision,
                       ApprovedDueDate         = COALESCE(@ApprovedDueDate, ApprovedDueDate)
                WHERE  RequestId = @Id";
            await _db.SaveDataAsync(updExt, new
            {
                LecturerDecision = decision,
                LecturerBy       = authUserId,
                Notes            = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes!.Trim(),
                FinalDecision    = finalDecision,
                ApprovedDueDate  = approvedDate?.ToString("yyyy-MM-dd"),
                Id               = id,
            });
        }

        // ── Override write on Approved + has target + has team ───────────
        if (decision == ExtensionDecisionStatuses.Approved
            && hasAnyTarget
            && approvedDate.HasValue
            && ctx.TeamId.HasValue)
        {
            if (hasTaskTarget)
            {
                const string upsertTask = @"
                    INSERT INTO TeamTaskDueDateOverrides
                        (TeamId, TaskId, OriginalDueDate, OverrideDueDate,
                         SourceRequestId, ApprovedByUserId, CreatedAt, UpdatedAt)
                    VALUES
                        (@TeamId, @TaskId, @OriginalDueDate, @OverrideDueDate,
                         @SourceRequestId, @ApprovedBy, datetime('now'), datetime('now'))
                    ON CONFLICT(TeamId, TaskId) DO UPDATE SET
                        OriginalDueDate  = excluded.OriginalDueDate,
                        OverrideDueDate  = excluded.OverrideDueDate,
                        SourceRequestId  = excluded.SourceRequestId,
                        ApprovedByUserId = excluded.ApprovedByUserId,
                        UpdatedAt        = datetime('now')";
                await _db.SaveDataAsync(upsertTask, new
                {
                    TeamId          = ctx.TeamId,
                    TaskId          = ctx.TaskId,
                    OriginalDueDate = ctx.CurrentDueDate?.ToString("yyyy-MM-dd"),
                    OverrideDueDate = approvedDate.Value.ToString("yyyy-MM-dd"),
                    SourceRequestId = id,
                    ApprovedBy      = authUserId,
                });
            }
            else if (hasMilestoneTarget)
            {
                const string upsertMs = @"
                    INSERT INTO TeamMilestoneDueDateOverrides
                        (TeamId, ProjectMilestoneId, OriginalDueDate, OverrideDueDate,
                         SourceRequestId, ApprovedByUserId, CreatedAt, UpdatedAt)
                    VALUES
                        (@TeamId, @MilestoneId, @OriginalDueDate, @OverrideDueDate,
                         @SourceRequestId, @ApprovedBy, datetime('now'), datetime('now'))
                    ON CONFLICT(TeamId, ProjectMilestoneId) DO UPDATE SET
                        OriginalDueDate  = excluded.OriginalDueDate,
                        OverrideDueDate  = excluded.OverrideDueDate,
                        SourceRequestId  = excluded.SourceRequestId,
                        ApprovedByUserId = excluded.ApprovedByUserId,
                        UpdatedAt        = datetime('now')";
                await _db.SaveDataAsync(upsertMs, new
                {
                    TeamId          = ctx.TeamId,
                    MilestoneId     = ctx.ProjectMilestoneId,
                    OriginalDueDate = ctx.CurrentDueDate?.ToString("yyyy-MM-dd"),
                    OverrideDueDate = approvedDate.Value.ToString("yyyy-MM-dd"),
                    SourceRequestId = id,
                    ApprovedBy      = authUserId,
                });
            }
        }

        // ── ProjectRequests parent row + thread events ───────────────────
        string newStatus = decision == ExtensionDecisionStatuses.Escalated
            ? RequestStatuses.InProgress
            : RequestStatuses.Resolved;

        const string updParent = @"
            UPDATE ProjectRequests
            SET    Status    = @Status,
                   UpdatedAt = datetime('now')
            WHERE  Id = @Id";
        await _db.SaveDataAsync(updParent, new { Status = newStatus, Id = id });

        // Audit-log event (StatusChange) + optional Comment
        const string evtSql = @"
            INSERT INTO ProjectRequestEvents (RequestId, UserId, EventType, Content, OldValue, NewValue, CreatedAt)
            VALUES (@RequestId, @UserId, @EventType, @Content, @OldValue, @NewValue, datetime('now'))";

        await _db.SaveDataAsync(evtSql, new
        {
            RequestId = id,
            UserId    = authUserId,
            EventType = RequestEventTypes.StatusChange,
            Content   = (string?)null,
            OldValue  = ctx.Status,
            NewValue  = $"{stage}:{ExtensionDecisionStatuses.Label(decision)}",
        });
        if (!string.IsNullOrWhiteSpace(req.Notes))
        {
            await _db.SaveDataAsync(evtSql, new
            {
                RequestId = id,
                UserId    = authUserId,
                EventType = RequestEventTypes.Comment,
                Content   = req.Notes!.Trim(),
                OldValue  = (string?)null,
                NewValue  = (string?)null,
            });
        }

        // ── Notify the team — only this team's members ───────────────────
        try
        {
            const string membersSql = @"
                SELECT tm.UserId
                FROM   ProjectRequests r
                JOIN   Projects        p  ON p.Id      = r.ProjectId
                JOIN   Teams           t  ON t.Id      = p.TeamId
                JOIN   TeamMembers     tm ON tm.TeamId = t.Id
                WHERE  r.Id        = @RequestId
                  AND  tm.IsActive = 1";
            var studentIds = (await _db.GetRecordsAsync<int>(
                membersSql, new { RequestId = id }))?.ToList() ?? new();

            (string title, string message, string type) = decision switch
            {
                // Approved with a per-team milestone/task target → mention the new date.
                ExtensionDecisionStatuses.Approved when approvedDate.HasValue =>
                    ("הדחייה אושרה",
                     $"הדחייה אושרה. התאריך עודכן עבור אבן הדרך — תאריך חדש: {approvedDate.Value:dd/MM/yyyy}.",
                     "ExtensionApproved"),
                // Approved without a target ("אחר / כללי") — no override is written.
                ExtensionDecisionStatuses.Approved =>
                    ("הדחייה אושרה",
                     "הדחייה אושרה.",
                     "ExtensionApproved"),
                ExtensionDecisionStatuses.Rejected =>
                    ("בקשת הדחייה נדחתה",
                     "בקשת הדחייה נדחתה. ראו את התגובה בפרטי הבקשה.",
                     "ExtensionRejected"),
                ExtensionDecisionStatuses.Escalated =>
                    ("בקשת הדחייה הועברה לבדיקת מרצה",
                     "המנחה העביר את בקשת הדחייה לבדיקת מרצה.",
                     "ExtensionEscalated"),
                _ => ("עדכון בבקשת הדחייה", "מצב בקשת הדחייה התעדכן.", "ExtensionUpdated"),
            };

            if (studentIds.Count > 0)
            {
                await NotificationHelper.CreateForUsersAsync(
                    _db, studentIds,
                    title:             title,
                    message:           message,
                    type:              type,
                    relatedEntityType: "ProjectRequest",
                    relatedEntityId:   id);
            }
        }
        catch { /* notifications are best-effort */ }

        return Ok();
    }

    // Mirrors the columns the create-time eligibility check selects from Tasks.
    // Kept private so DTOs in Shared stay clean.
    private sealed class TaskEligibilityRow
    {
        public DateTime? DueDate         { get; set; }
        public string    Status          { get; set; } = "";
        public DateTime? ClosedAt        { get; set; }
        public int       IsSubmission    { get; set; }
        public int       SubmissionCount { get; set; }
    }

    private sealed class MilestoneEligibilityRow
    {
        public DateTime? DueDate     { get; set; }
        public string    Status      { get; set; } = "";
        public DateTime? CompletedAt { get; set; }
    }

    private sealed class ExtensionDecisionLoadRow
    {
        public int       Id                 { get; set; }
        public int       ProjectId          { get; set; }
        public string    RequestType        { get; set; } = "";
        public string    Status             { get; set; } = "";
        public int?      TeamId             { get; set; }
        public int?      ExtId              { get; set; }
        public int?      TaskId             { get; set; }
        public int?      ProjectMilestoneId { get; set; }
        public string    MentorDecision     { get; set; } = "";
        public string    LecturerDecision   { get; set; } = "";
        public string    FinalDecision      { get; set; } = "";
        public DateTime  RequestedDueDate   { get; set; }
        public DateTime? CurrentDueDate     { get; set; }
    }

    // ── Private row types ────────────────────────────────────────────────────

    // Mirrors ProjectRequestRowDto plus the raw CSV team-info columns produced
    // by the GROUP_CONCAT subqueries. Kept private so the public DTO stays
    // clean (lists, not CSV strings) on the wire.
    private sealed class ProjectRequestRowWithTeam
    {
        public int      Id               { get; set; }
        public string   RequestType      { get; set; } = "";
        public string   Title            { get; set; } = "";
        public int      ProjectId        { get; set; }
        public int      ProjectNumber    { get; set; }
        public string   ProjectTitle     { get; set; } = "";
        public int      CreatedByUserId  { get; set; }
        public string   CreatedByName    { get; set; } = "";
        public DateTime CreatedAt        { get; set; }
        public DateTime UpdatedAt        { get; set; }
        public string   Status           { get; set; } = RequestStatuses.New;
        public string   Priority         { get; set; } = RequestPriorities.Normal;
        public string?  AssignedToName   { get; set; }
        public int      AttachmentCount  { get; set; }

        public string?  TeamName          { get; set; }
        public string?  TrackName         { get; set; }
        public string?  StudentDetailsCsv { get; set; }
        public string?  MentorDetailsCsv  { get; set; }
    }

    private sealed class CurrentRequestRow
    {
        public int     Id               { get; set; }
        public string  Title            { get; set; } = "";
        public int     ProjectId        { get; set; }
        public string  Status           { get; set; } = "";
        public string  Priority         { get; set; } = "";
        public int?    AssignedToUserId { get; set; }
        public string? AssignedToName   { get; set; }
    }

    private sealed class RequestStatusRow
    {
        public int    Id     { get; set; }
        public string Status { get; set; } = "";
    }
}
