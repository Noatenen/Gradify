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
    [HttpGet]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> GetAll(int authUserId)
    {
        const string sql = @"
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
                COUNT(att.Id)                    AS AttachmentCount
            FROM   ProjectRequests r
            JOIN   Projects  p ON p.Id = r.ProjectId
            JOIN   users     u ON u.Id = r.CreatedByUserId
            LEFT JOIN users  a ON a.Id = r.AssignedToUserId
            LEFT JOIN ProjectRequestAttachments att ON att.RequestId = r.Id
            GROUP  BY r.Id
            ORDER  BY r.CreatedAt DESC";

        var rows = await _db.GetRecordsAsync<ProjectRequestRowDto>(sql);
        return Ok(rows ?? Enumerable.Empty<ProjectRequestRowDto>());
    }

    // ── GET /api/project-requests/{id} ────────────────────────────────────
    [HttpGet("{id:int}")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
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

    // ── Private row types ────────────────────────────────────────────────────

    private sealed class CurrentRequestRow
    {
        public int     Id               { get; set; }
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
