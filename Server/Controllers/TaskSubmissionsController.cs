using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  TaskSubmissionsController — /api/task-submissions
//
//  Manages submission records and their attached files for operational tasks
//  where Tasks.IsSubmission = true.
//
//  Design notes:
//    • Submission policy is read from the Tasks row snapshot
//      (MaxFilesCount / MaxFileSizeMb / AllowedFileTypes), NOT from
//      TaskTemplates. This keeps the domain clean at runtime.
//    • Files are stored in wwwroot/submissions/ using the same GUID-filename
//      pattern as ResourceFiles.
//    • Status lifecycle: Submitted → Reviewed | NeedsRevision
//
//  Authorization:
//    • GET (list / detail) — Admin, Staff, Mentor
//    • POST (create)       — all authenticated roles (students submit)
//    • PATCH (status)      — Admin, Staff only
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/task-submissions")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize]
public class TaskSubmissionsController : ControllerBase
{
    private readonly DbRepository    _db;
    private readonly FilesManage     _filesManage;

    private const string Container      = "submissions";
    private static readonly HashSet<string> ValidStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "Submitted", "Reviewed", "NeedsRevision" };

    public TaskSubmissionsController(DbRepository db, FilesManage filesManage)
    {
        _db          = db;
        _filesManage = filesManage;
    }

    // ── GET /api/task-submissions/all ─────────────────────────────────────
    //
    // Returns all submissions across every project, enriched with project /
    // team / milestone context. Used by the lecturer "הגשות" monitoring page.
    // Ordered newest-first.
    [HttpGet("all")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> GetAll(int authUserId)
    {
        const string sql = @"
            SELECT
                s.Id                                 AS SubmissionId,
                s.TaskId,
                t.Title                              AS TaskTitle,
                p.Id                                 AS ProjectId,
                p.ProjectNumber,
                p.Title                              AS ProjectTitle,
                mt.Title                             AS MilestoneTitle,
                s.SubmittedByUserId,
                u.FirstName || ' ' || u.LastName     AS SubmittedByName,
                s.SubmittedAt,
                s.Notes,
                s.Status,
                ay.Id                                AS AcademicYearId,
                ay.Name                              AS AcademicYearName,
                COUNT(f.Id)                          AS FileCount
            FROM   TaskSubmissions        s
            JOIN   Tasks                  t   ON t.Id   = s.TaskId
            JOIN   users                  u   ON u.Id   = s.SubmittedByUserId
            JOIN   ProjectMilestones      pm  ON pm.Id  = t.ProjectMilestoneId
            JOIN   Projects               p   ON p.Id   = pm.ProjectId
            JOIN   AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            JOIN   MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            JOIN   AcademicYears          ay  ON ay.Id  = p.AcademicYearId
            LEFT JOIN TaskSubmissionFiles f   ON f.TaskSubmissionId = s.Id
            GROUP  BY s.Id
            ORDER  BY s.SubmittedAt DESC";

        var rows = await _db.GetRecordsAsync<LecturerSubmissionRowDto>(sql, new { });
        return Ok(rows ?? Enumerable.Empty<LecturerSubmissionRowDto>());
    }

    // ── GET /api/task-submissions?taskId=N ─────────────────────────────────
    //
    // Returns all submissions for a given task as summary rows (no embedded files).
    // Ordered newest-first.
    [HttpGet]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Mentor)]
    public async Task<IActionResult> GetByTask([FromQuery] int taskId, int authUserId)
    {
        if (taskId <= 0) return BadRequest("יש לספק מזהה משימה תקין");

        // Verify the task exists
        var task = await GetTaskPolicyAsync(taskId);
        if (task is null) return NotFound("המשימה לא נמצאה");

        const string sql = @"
            SELECT  s.Id,
                    s.TaskId,
                    t.Title                              AS TaskTitle,
                    s.SubmittedByUserId,
                    u.FirstName || ' ' || u.LastName     AS SubmittedByName,
                    s.SubmittedAt,
                    s.Notes,
                    s.Status,
                    COUNT(f.Id)                          AS FileCount
            FROM    TaskSubmissions      s
            JOIN    Tasks                t ON t.Id = s.TaskId
            JOIN    users                u ON u.Id = s.SubmittedByUserId
            LEFT JOIN TaskSubmissionFiles f ON f.TaskSubmissionId = s.Id
            WHERE   s.TaskId = @TaskId
            GROUP   BY s.Id
            ORDER   BY s.SubmittedAt DESC";

        var rows = await _db.GetRecordsAsync<TaskSubmissionSummaryDto>(sql, new { TaskId = taskId });
        return Ok(rows ?? Enumerable.Empty<TaskSubmissionSummaryDto>());
    }

    // ── GET /api/task-submissions/{id} ─────────────────────────────────────
    //
    // Returns the full submission record including all attached file metadata.
    [HttpGet("{id:int}")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Mentor + "," + Roles.Student)]
    public async Task<IActionResult> GetById(int id, int authUserId)
    {
        const string subSql = @"
            SELECT  s.Id,
                    s.TaskId,
                    t.Title                              AS TaskTitle,
                    s.SubmittedByUserId,
                    u.FirstName || ' ' || u.LastName     AS SubmittedByName,
                    s.SubmittedAt,
                    s.Notes,
                    s.Status,
                    s.CreatedAt
            FROM    TaskSubmissions s
            JOIN    Tasks           t ON t.Id = s.TaskId
            JOIN    users           u ON u.Id = s.SubmittedByUserId
            WHERE   s.Id = @Id";

        var sub = (await _db.GetRecordsAsync<TaskSubmissionDto>(subSql, new { Id = id }))
                  ?.FirstOrDefault();

        if (sub is null) return NotFound("ההגשה לא נמצאה");

        const string filesSql = @"
            SELECT  Id,
                    TaskSubmissionId,
                    OriginalFileName,
                    StoredFileName,
                    ContentType,
                    SizeBytes,
                    UploadedAt
            FROM    TaskSubmissionFiles
            WHERE   TaskSubmissionId = @SubmissionId
            ORDER   BY Id";

        var files = await _db.GetRecordsAsync<TaskSubmissionFileDto>(
            filesSql, new { SubmissionId = id });

        sub.Files = files?.ToList() ?? new();
        return Ok(sub);
    }

    // ── POST /api/task-submissions ─────────────────────────────────────────
    //
    // Creates a submission record + stores all attached files.
    //
    // Validates against the snapshot policy on the Tasks row:
    //   • task must exist and have IsSubmission = 1
    //   • file count  ≤ Tasks.MaxFilesCount
    //   • file size   ≤ Tasks.MaxFileSizeMb × 1 048 576 bytes
    //   • extension   ∈ Tasks.AllowedFileTypes (when defined)
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSubmissionRequest req, int authUserId)
    {
        // ── 1. Validate request structure ────────────────────────────────────
        if (req.TaskId <= 0)
            return BadRequest("יש לספק מזהה משימה תקין");

        if (req.Files.Count == 0)
            return BadRequest("יש לצרף לפחות קובץ אחד להגשה");

        // ── 2. Load task + snapshot policy ──────────────────────────────────
        var task = await GetTaskPolicyAsync(req.TaskId);
        if (task is null)
            return NotFound("המשימה לא נמצאה");

        if (!task.IsSubmission)
            return BadRequest("משימה זו אינה מוגדרת כמשימת הגשה");

        // ── 3. Validate file count ────────────────────────────────────────
        if (task.MaxFilesCount.HasValue && req.Files.Count > task.MaxFilesCount.Value)
            return BadRequest(
                $"ניתן לצרף עד {task.MaxFilesCount.Value} קבצים. נשלחו {req.Files.Count}.");

        // ── 4. Validate each file (size + extension) ─────────────────────
        long maxBytes = task.MaxFileSizeMb.HasValue
            ? (long)task.MaxFileSizeMb.Value * 1_048_576
            : long.MaxValue;

        HashSet<string>? allowedExtensions = null;
        if (!string.IsNullOrWhiteSpace(task.AllowedFileTypes))
        {
            allowedExtensions = new HashSet<string>(
                task.AllowedFileTypes.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim().TrimStart('.').ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);
        }

        for (int i = 0; i < req.Files.Count; i++)
        {
            var f = req.Files[i];

            if (string.IsNullOrWhiteSpace(f.OriginalFileName))
                return BadRequest($"קובץ {i + 1}: חסר שם קובץ");

            if (string.IsNullOrWhiteSpace(f.FileBase64))
                return BadRequest($"קובץ {i + 1}: חסר תוכן קובץ");

            if (f.SizeBytes > maxBytes)
                return BadRequest(
                    $"קובץ \"{f.OriginalFileName}\" חורג מגודל הקובץ המקסימלי ({task.MaxFileSizeMb} MB).");

            if (allowedExtensions is not null)
            {
                string ext = Path.GetExtension(f.OriginalFileName)
                                 .TrimStart('.')
                                 .ToLowerInvariant();
                if (!allowedExtensions.Contains(ext))
                    return BadRequest(
                        $"סוג הקובץ \"{ext}\" אינו מורשה. סוגים מותרים: {task.AllowedFileTypes}.");
            }
        }

        // ── 5. Create the submission record ──────────────────────────────
        const string insertSubSql = @"
            INSERT INTO TaskSubmissions
                (TaskId, SubmittedByUserId, SubmittedAt, Notes, Status)
            VALUES
                (@TaskId, @UserId, datetime('now'), @Notes, 'Submitted')";

        int submissionId = await _db.InsertReturnIdAsync(insertSubSql, new
        {
            TaskId = req.TaskId,
            UserId = authUserId,
            Notes  = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
        });

        if (submissionId == 0)
            return StatusCode(500, "שגיאה ביצירת רשומת ההגשה");

        // ── 6. Save files ─────────────────────────────────────────────────
        const string insertFileSql = @"
            INSERT INTO TaskSubmissionFiles
                (TaskSubmissionId, OriginalFileName, StoredFileName, ContentType, SizeBytes, UploadedAt)
            VALUES
                (@SubId, @OriginalFileName, @StoredFileName, @ContentType, @SizeBytes, datetime('now'))";

        foreach (var f in req.Files)
        {
            string storedFileName;
            try
            {
                storedFileName = await _filesManage.SaveRawFile(f.FileBase64, f.OriginalFileName, Container);
            }
            catch
            {
                // Best-effort: skip files that fail to save; submission record still created.
                // Callers can check FileCount vs expected.
                continue;
            }

            await _db.SaveDataAsync(insertFileSql, new
            {
                SubId            = submissionId,
                OriginalFileName = f.OriginalFileName,
                StoredFileName   = storedFileName,
                ContentType      = string.IsNullOrWhiteSpace(f.ContentType)
                                       ? "application/octet-stream"
                                       : f.ContentType,
                SizeBytes        = f.SizeBytes,
            });
        }

        // ── 7. Auto-complete all student sub-tasks for this task + team ─────
        // Marking sub-tasks done when a submission is created keeps the internal
        // team checklist in sync without requiring a separate student action.
        var teamIdRows = await _db.GetRecordsAsync<int>(@"
            SELECT t.Id
            FROM   Teams       t
            JOIN   TeamMembers tm ON t.Id = tm.TeamId
            WHERE  tm.UserId   = @UserId AND tm.IsActive = 1
            LIMIT  1", new { UserId = authUserId });
        int teamId = teamIdRows?.FirstOrDefault() ?? 0;
        if (teamId > 0)
        {
            await _db.SaveDataAsync(
                "UPDATE StudentSubTasks SET IsDone = 1 WHERE TaskId = @TaskId AND TeamId = @TeamId",
                new { TaskId = req.TaskId, TeamId = teamId });
        }

        return Ok(new { id = submissionId });
    }

    // ── PATCH /api/task-submissions/{id}/status ────────────────────────────
    //
    // Updates the reviewer (admin/staff/lecturer) status of a submission.
    // Valid values: "Submitted" | "Reviewed" | "NeedsRevision"
    // Optional ReviewerFeedback is stored when status = NeedsRevision.
    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> UpdateStatus(
        int id, [FromBody] UpdateSubmissionStatusRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.Status) || !ValidStatuses.Contains(req.Status))
            return BadRequest("סטטוס לא תקין. ערכים חוקיים: Submitted, Reviewed, NeedsRevision");

        const string sql = @"
            UPDATE TaskSubmissions
            SET    Status          = @Status,
                   ReviewerFeedback = @ReviewerFeedback
            WHERE  Id              = @Id";

        int affected = await _db.SaveDataAsync(sql, new
        {
            req.Status,
            ReviewerFeedback = string.IsNullOrWhiteSpace(req.ReviewerFeedback)
                ? null : req.ReviewerFeedback.Trim(),
            Id = id,
        });

        if (affected == 0) return NotFound("ההגשה לא נמצאה");
        return Ok();
    }

    // ── PATCH /api/task-submissions/{id}/mentor-review ─────────────────────
    //
    // Allows a mentor to approve or return a submission with feedback.
    // Valid MentorStatus values: "Approved" | "Returned"
    [HttpPatch("{id:int}/mentor-review")]
    [Authorize(Roles = Roles.Mentor + "," + Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> MentorReview(
        int id, [FromBody] MentorReviewRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.MentorStatus) ||
            (req.MentorStatus != "Approved" && req.MentorStatus != "Returned"))
            return BadRequest("ערך לא תקין. ערכים חוקיים: Approved, Returned");

        const string sql = @"
            UPDATE TaskSubmissions
            SET    MentorStatus     = @MentorStatus,
                   MentorFeedback   = @MentorFeedback,
                   MentorReviewedAt = datetime('now')
            WHERE  Id               = @Id";

        int affected = await _db.SaveDataAsync(sql, new
        {
            req.MentorStatus,
            MentorFeedback = string.IsNullOrWhiteSpace(req.MentorFeedback)
                ? null : req.MentorFeedback.Trim(),
            Id = id,
        });

        if (affected == 0) return NotFound("ההגשה לא נמצאה");
        return Ok();
    }

    // ── GET /api/task-submissions/{id}/files/{fileId}/download ────────────
    //
    // Streams a submission file back to the caller.
    // The file lives in wwwroot/submissions/<storedFileName>.
    [HttpGet("{id:int}/files/{fileId:int}/download")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff + "," + Roles.Mentor + "," + Roles.Student)]
    public async Task<IActionResult> DownloadFile(int id, int fileId, int authUserId,
        [FromServices] IWebHostEnvironment env)
    {
        const string sql = @"
            SELECT  f.OriginalFileName,
                    f.StoredFileName,
                    f.ContentType
            FROM    TaskSubmissionFiles f
            WHERE   f.Id               = @FileId
              AND   f.TaskSubmissionId = @SubId";

        var row = (await _db.GetRecordsAsync<FileDownloadRow>(
                      sql, new { FileId = fileId, SubId = id }))
                  ?.FirstOrDefault();

        if (row is null) return NotFound("הקובץ לא נמצא");

        string path = Path.Combine(env.WebRootPath, Container, row.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound("קובץ לא נמצא בדיסק");

        var bytes = await System.IO.File.ReadAllBytesAsync(path);
        return File(bytes, row.ContentType, row.OriginalFileName);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Loads the submission policy snapshot from the operational Tasks row.
    /// Returns null when the task does not exist.
    /// </summary>
    private async Task<TaskPolicyRow?> GetTaskPolicyAsync(int taskId)
    {
        const string sql = @"
            SELECT  Id,
                    IsSubmission,
                    MaxFilesCount,
                    MaxFileSizeMb,
                    AllowedFileTypes
            FROM    Tasks
            WHERE   Id = @Id";

        return (await _db.GetRecordsAsync<TaskPolicyRow>(sql, new { Id = taskId }))
               ?.FirstOrDefault();
    }

    // ── Private Dapper row types ─────────────────────────────────────────────

    private sealed class TaskPolicyRow
    {
        public int     Id               { get; set; }
        public bool    IsSubmission     { get; set; }
        public int?    MaxFilesCount    { get; set; }
        public int?    MaxFileSizeMb    { get; set; }
        public string? AllowedFileTypes { get; set; }
    }

    private sealed class FileDownloadRow
    {
        public string OriginalFileName { get; set; } = "";
        public string StoredFileName   { get; set; } = "";
        public string ContentType      { get; set; } = "";
    }
}