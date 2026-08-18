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
    private readonly DbRepository       _db;
    private readonly FilesManage        _filesManage;
    private readonly EmailHelper        _email;
    private readonly IHttpClientFactory _httpFactory;

    private const string Container      = "submissions";
    private static readonly HashSet<string> ValidStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "Submitted", "Reviewed", "NeedsRevision" };

    // Lecturer-feedback file upload policy. Reuses wwwroot/submissions/.
    private const int  MaxLecturerFilesPerCall = 5;
    private const int  MaxLecturerFileMb       = 10;
    private const long MaxLecturerFileBytes    = (long)MaxLecturerFileMb * 1_048_576;

    public TaskSubmissionsController(
        DbRepository db,
        FilesManage filesManage,
        EmailHelper email,
        IHttpClientFactory httpFactory)
    {
        _db          = db;
        _filesManage = filesManage;
        _email       = email;
        _httpFactory = httpFactory;
    }

    // ── GET /api/task-submissions/all ─────────────────────────────────────
    // DEPRECATED 2026-05-17.
    // Powered the retired lecturer-final-review queue. Returns 410 Gone so any
    // lingering client never displays stale data.
    [HttpGet("all")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public IActionResult GetAll_Deprecated() =>
        StatusCode(StatusCodes.Status410Gone, new
        {
            error = "lecturer_review_disabled",
            message = "תהליך הבדיקה הסופית הוסר. ההגשה הרשמית מתבצעת במודל."
        });

    // ── Legacy implementation kept below as a private no-op for reference. ─
    private async Task<IActionResult> GetAllLegacy(int authUserId)
    {
        // Visibility gate: a submission becomes visible to lecturers/admins
        // only after the mentor approves it. The legacy "course-submitted OR
        // already actioned" gate is preserved as a safety fallback for old
        // rows. AssignedMentorName is built via a correlated GROUP_CONCAT —
        // single round-trip, no N+1.
        // Lecturer-uploaded files are excluded from FileCount (that's the
        // student-uploaded count; lecturer files are counted separately in
        // the detail view).
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
                s.MentorStatus,
                s.MentorReviewedAt,
                s.CourseSubmittedAt,
                COALESCE(s.ReviewStatus, 'PendingReview')   AS ReviewStatus,
                COALESCE(s.IsFeedbackPublished, 0)          AS IsFeedbackPublished,
                s.FeedbackPublishedAt,
                ay.Id                                AS AcademicYearId,
                ay.Name                              AS AcademicYearName,
                SUM(CASE WHEN COALESCE(f.IsLecturerFeedback,0) = 0 THEN 1 ELSE 0 END) AS FileCount,
                (SELECT GROUP_CONCAT(mu.FirstName || ' ' || mu.LastName, ', ')
                 FROM   ProjectMentors pmm
                 JOIN   users          mu ON mu.Id = pmm.UserId
                 WHERE  pmm.ProjectId = p.Id)        AS AssignedMentorName
            FROM   TaskSubmissions        s
            JOIN   Tasks                  t   ON t.Id   = s.TaskId
            JOIN   users                  u   ON u.Id   = s.SubmittedByUserId
            JOIN   ProjectMilestones      pm  ON pm.Id  = t.ProjectMilestoneId
            JOIN   Projects               p   ON p.Id   = pm.ProjectId
            JOIN   AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            JOIN   MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            JOIN   AcademicYears          ay  ON ay.Id  = p.AcademicYearId
            LEFT JOIN TaskSubmissionFiles f   ON f.TaskSubmissionId = s.Id
            WHERE  s.MentorStatus = 'Approved'
              OR   s.CourseSubmittedAt IS NOT NULL
              OR   s.Status != 'Submitted'
            GROUP  BY s.Id
            ORDER  BY s.SubmittedAt DESC";

        var rows = await _db.GetRecordsAsync<LecturerSubmissionRowDto>(sql, new { });
        return Ok(rows ?? Enumerable.Empty<LecturerSubmissionRowDto>());
    }

    // ── POST /api/task-submissions/{id}/submit-to-course ──────────────────
    // DEPRECATED 2026-05-17. The student no longer forwards submissions to
    // course staff inside the system — they submit the final deliverable to
    // Moodle. Returns 410 Gone so old clients fail loudly rather than silently.
    [HttpPost("{id:int}/submit-to-course")]
    public IActionResult SubmitToCourse_Deprecated(int id) =>
        StatusCode(StatusCodes.Status410Gone, new
        {
            error = "course_submission_disabled",
            message = "ההגשה הרשמית מתבצעת במודל. אישור מנחה כבר התקבל."
        });

    // ── Legacy implementation kept below as a private no-op for reference. ─
    private async Task<IActionResult> SubmitToCourseLegacy(int id, int authUserId)
    {
        // Verify the submission belongs to the student's project
        const string checkSql = @"
            SELECT  s.Id, s.TaskId, s.MentorStatus, s.CourseSubmittedAt, t.ProjectId
            FROM    TaskSubmissions s
            JOIN    Tasks           t  ON s.TaskId = t.Id
            JOIN    Projects        p  ON t.ProjectId = p.Id
            JOIN    Teams           tm ON p.TeamId    = tm.Id
            JOIN    TeamMembers     m  ON tm.Id       = m.TeamId
            WHERE   s.Id         = @SubmissionId
              AND   m.UserId     = @UserId
              AND   m.IsActive   = 1";

        var row = (await _db.GetRecordsAsync<CourseSumbitCheckRow>(
            checkSql, new { SubmissionId = id, UserId = authUserId }))
            ?.FirstOrDefault();

        if (row is null) return NotFound("ההגשה לא נמצאה או שאין לך הרשאה");

        if (row.MentorStatus != "Approved")
            return BadRequest("ניתן להגיש לצוות הקורס רק לאחר אישור מנחה");

        // Mark the submission as forwarded to course staff
        int affected = await _db.SaveDataAsync(
            "UPDATE TaskSubmissions SET CourseSubmittedAt = datetime('now') WHERE Id = @Id",
            new { Id = id });

        if (affected == 0) return NotFound("ההגשה לא נמצאה");

        // Update task status to Done — the student has completed all steps
        await _db.SaveDataAsync(
            "UPDATE Tasks SET Status = 'Done' WHERE Id = @TaskId AND Status = 'ApprovedForSubmission'",
            new { row.TaskId });

        return Ok();
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
                    s.DriveUrl,
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
                    s.CreatedAt,
                    s.DriveUrl
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
    // Drive-link-only submission flow (since 2026-05-19). The student sends
    // a public Google Drive URL + optional notes; the server validates the
    // URL shape (host whitelist + HTTPS) and runs a lightweight reachability
    // probe (no body download) before persisting. Files are never uploaded
    // here — the historical TaskSubmissionFiles rows remain readable but
    // are not appended to.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSubmissionRequest req, int authUserId,
        CancellationToken cancellationToken)
    {
        // ── 1. Validate request structure ────────────────────────────────────
        if (req.TaskId <= 0)
            return BadRequest("יש לספק מזהה משימה תקין");

        // ── 2. Validate Drive URL format ─────────────────────────────────────
        var formatErr = GoogleDriveLinkValidator.ValidateFormat(req.DriveUrl);
        if (formatErr is not null) return BadRequest(formatErr);

        var driveUrl = req.DriveUrl!.Trim();

        // ── 3. Load task to confirm it's a submission task ───────────────────
        var task = await GetTaskPolicyAsync(req.TaskId);
        if (task is null)
            return NotFound("המשימה לא נמצאה");

        if (!task.IsSubmission)
            return BadRequest("משימה זו אינה מוגדרת כמשימת הגשה");

        // ── 4. Reachability probe — verifies the link is publicly shared ─────
        var reachErr = await GoogleDriveLinkValidator.IsReachableAsync(
            driveUrl, _httpFactory, cancellationToken);
        if (reachErr is not null) return BadRequest(reachErr);

        // ── 5. Create the submission record ──────────────────────────────────
        const string insertSubSql = @"
            INSERT INTO TaskSubmissions
                (TaskId, SubmittedByUserId, SubmittedAt, Notes, Status, DriveUrl)
            VALUES
                (@TaskId, @UserId, datetime('now'), @Notes, 'Submitted', @DriveUrl)";

        int submissionId = await _db.InsertReturnIdAsync(insertSubSql, new
        {
            TaskId   = req.TaskId,
            UserId   = authUserId,
            Notes    = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            DriveUrl = driveUrl,
        });

        if (submissionId == 0)
            return StatusCode(500, "שגיאה ביצירת רשומת ההגשה");

        // ── 6. Files are no longer stored. Any incoming Files[] payload is
        //       silently ignored — DTO field is marked [Obsolete] and the
        //       legacy file-save loop has been removed.

        // ── 7. Sync Tasks.Status to reflect the new submission ───────────────
        // A resubmission after a return moves the task to RevisionSubmitted;
        // a first submission moves it to SubmittedToMentor.
        await _db.SaveDataAsync(@"
            UPDATE Tasks
            SET    Status = CASE WHEN Status = 'ReturnedForRevision'
                                 THEN 'RevisionSubmitted'
                                 ELSE 'SubmittedToMentor'
                            END
            WHERE  Id = @TaskId", new { TaskId = req.TaskId });

        // ── 8. Notify mentor(s) that a submission arrived ────────────────────
        try
        {
            const string contextSql = @"
                SELECT t.Title AS TaskTitle, tm.TeamName
                FROM   Tasks    t
                JOIN   Projects p  ON t.ProjectId = p.Id
                JOIN   Teams    tm ON p.TeamId    = tm.Id
                WHERE  t.Id = @TaskId
                LIMIT 1";

            var ctx = (await _db.GetRecordsAsync<SubmissionNotifyRow>(
                contextSql, new { TaskId = req.TaskId }))?.FirstOrDefault();

            if (ctx is not null)
            {
                const string mentorsSql = @"
                    SELECT pmt.UserId
                    FROM   ProjectMentors pmt
                    JOIN   Tasks          t   ON t.ProjectId = pmt.ProjectId
                    WHERE  t.Id = @TaskId";

                var mentorIds = (await _db.GetRecordsAsync<int>(
                    mentorsSql, new { TaskId = req.TaskId }))?.ToList() ?? new();

                string title   = "הגשה חדשה התקבלה";
                string message = $"צוות {ctx.TeamName} הגיש לבדיקה את המשימה {ctx.TaskTitle}";

                // Dispatcher, not NotificationHelper, and NotificationTypes
                // .SubmissionSubmitted rather than the ad-hoc string
                // "SubmissionReceived".
                //
                // WHAT WAS BROKEN. "SubmissionReceived" is not a member of
                // NotificationTypes, so it matched no row in
                // UserNotificationPreferences and never reached the dispatcher's
                // email path: a real new submission produced an in-app row only.
                // Meanwhile the mentor's settings screen DOES show a "הגשה חדשה"
                // toggle — bound to SubmissionSubmitted, which until now was
                // written solely by the lecturer's manual "remind mentor"
                // action. The mentor's own new-submission email preference
                // therefore controlled nothing.
                //
                // Using the canonical type makes that toggle real and routes the
                // notification through the same preference check every other
                // notification already uses. The in-app row is still always
                // written first, so nothing that worked before stops working.
                await NotificationDispatcher.DispatchAsync(
                    _db, _email, mentorIds, title, message,
                    type: NotificationTypes.SubmissionSubmitted,
                    relatedEntityType: "TaskSubmission",
                    relatedEntityId: req.TaskId);  // TaskId so mentor page can deep-link
            }
        }
        catch { /* notifications are best-effort — never block the main flow */ }

        // ── 9. Auto-complete all student sub-tasks for this task + team ─────
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

    // ── POST /api/task-submissions/{id}/mark-moodle-submitted ─────────────
    // Manual student confirmation that the official submission was made in
    // Moodle. Gated server-side on:
    //   • caller is on the team that owns the parent project,
    //   • MentorStatus = 'Approved' on this row (CTA only shows then).
    // Idempotent — re-clicks are a no-op and preserve the original
    // timestamp/user. Bumps Tasks.Status to 'Done' so the task drops off
    // the "pending submission" list.
    [HttpPost("{id:int}/mark-moodle-submitted")]
    public async Task<IActionResult> MarkMoodleSubmitted(int id, int authUserId)
    {
        var row = (await _db.GetRecordsAsync<MoodleMarkGateRow>(@"
            SELECT  s.Id, s.TaskId, s.MentorStatus,
                    s.MoodleSubmittedAt, s.MoodleSubmittedByUserId, s.CourseSubmittedAt
            FROM    TaskSubmissions s
            JOIN    Tasks       t  ON t.Id   = s.TaskId
            JOIN    Projects    p  ON p.Id   = t.ProjectId
            JOIN    Teams       tm ON tm.Id  = p.TeamId
            JOIN    TeamMembers m  ON m.TeamId = tm.Id
            WHERE   s.Id        = @SubmissionId
              AND   m.UserId    = @UserId
              AND   m.IsActive  = 1
            LIMIT 1",
            new { SubmissionId = id, UserId = authUserId }))?.FirstOrDefault();

        if (row is null)
            return NotFound("ההגשה לא נמצאה או שאין לך הרשאה");

        if (!string.Equals(row.MentorStatus, "Approved", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status409Conflict,
                "ניתן לסמן הגשה במודל רק לאחר אישור מנחה");

        // Idempotent: if already marked (new column or legacy CourseSubmittedAt),
        // we don't overwrite the original audit trail.
        bool alreadyMarked = !string.IsNullOrWhiteSpace(row.MoodleSubmittedAt)
                          || !string.IsNullOrWhiteSpace(row.CourseSubmittedAt);

        if (!alreadyMarked)
        {
            await _db.SaveDataAsync(@"
                UPDATE TaskSubmissions
                SET    MoodleSubmittedAt       = datetime('now'),
                       MoodleSubmittedByUserId = @UserId
                WHERE  Id = @Id
                  AND  MoodleSubmittedAt IS NULL", // second-tier idempotency guard via WHERE
                new { Id = id, UserId = authUserId });

            // The task is done as far as the student flow is concerned —
            // mentor approval has happened AND the student has confirmed the
            // external submission. Stop counting it as pending.
            await _db.SaveDataAsync(
                "UPDATE Tasks SET Status = 'Done' WHERE Id = @TaskId",
                new { TaskId = row.TaskId });
        }

        // Re-read so the response carries the canonical persisted values.
        var fresh = (await _db.GetRecordsAsync<MoodleMarkResultRow>(@"
            SELECT  COALESCE(s.MoodleSubmittedAt, s.CourseSubmittedAt) AS MoodleSubmittedAt,
                    CASE
                        WHEN u.Id IS NULL THEN ''
                        ELSE TRIM(COALESCE(u.FirstName,'') || ' ' || COALESCE(u.LastName,''))
                    END                                               AS MoodleSubmittedByName
            FROM    TaskSubmissions s
            LEFT JOIN users u ON u.Id = s.MoodleSubmittedByUserId
            WHERE   s.Id = @Id LIMIT 1",
            new { Id = id }))?.FirstOrDefault();

        return Ok(new
        {
            submissionId        = id,
            wasAlreadyMarked    = alreadyMarked,
            moodleSubmittedAt   = fresh?.MoodleSubmittedAt,
            moodleSubmittedBy   = fresh?.MoodleSubmittedByName ?? "",
        });
    }

    private sealed class MoodleMarkGateRow
    {
        public int     Id                       { get; set; }
        public int     TaskId                   { get; set; }
        public string  MentorStatus             { get; set; } = "";
        public string? MoodleSubmittedAt        { get; set; }
        public int?    MoodleSubmittedByUserId  { get; set; }
        public string? CourseSubmittedAt        { get; set; }
    }

    private sealed class MoodleMarkResultRow
    {
        public DateTime? MoodleSubmittedAt     { get; set; }
        public string    MoodleSubmittedByName { get; set; } = "";
    }

    // ── POST /api/task-submissions/validate-drive-link ────────────────────
    // Dry-run validator used by the student submission modal to live-check
    // a Drive URL *before* submission. No DB mutation. Mirrors the same
    // ValidateFormat + IsReachableAsync the Create / Update endpoints run,
    // so the submit button can be reliably gated on "Ok = true".
    [HttpPost("validate-drive-link")]
    public async Task<IActionResult> ValidateDriveLink(
        [FromBody] ValidateDriveLinkRequest req, int authUserId,
        CancellationToken cancellationToken)
    {
        if (req is null) return BadRequest("גוף בקשה ריק");

        var formatErr = GoogleDriveLinkValidator.ValidateFormat(req.DriveUrl);
        if (formatErr is not null)
            return Ok(new ValidateDriveLinkResponse { Ok = false, Error = formatErr });

        var reachErr = await GoogleDriveLinkValidator.IsReachableAsync(
            req.DriveUrl!.Trim(), _httpFactory, cancellationToken);
        if (reachErr is not null)
            return Ok(new ValidateDriveLinkResponse { Ok = false, Error = reachErr });

        return Ok(new ValidateDriveLinkResponse { Ok = true });
    }

    // ── PATCH /api/task-submissions/{id} ──────────────────────────────────
    // Student-side edit of *own* pending submission. Allowed only when:
    //   • the caller is on the team that owns the parent project,
    //   • MentorStatus = 'Pending' on this row,
    //   • this row is the LATEST submission for its task (no newer rows).
    // Once a mentor reviews (Approved / Returned), the row is frozen here
    // and revisions must go through the standard resubmit flow.
    [HttpPatch("{id:int}")]
    public async Task<IActionResult> UpdateMine(
        int id, [FromBody] UpdateMySubmissionRequest req, int authUserId,
        CancellationToken cancellationToken)
    {
        if (req is null) return BadRequest("גוף בקשה ריק");

        var formatErr = GoogleDriveLinkValidator.ValidateFormat(req.DriveUrl);
        if (formatErr is not null) return BadRequest(formatErr);

        var gate = await LoadOwnPendingLatestAsync(id, authUserId);
        if (gate.Result is not null) return gate.Result;

        var driveUrl = req.DriveUrl!.Trim();
        var reachErr = await GoogleDriveLinkValidator.IsReachableAsync(
            driveUrl, _httpFactory, cancellationToken);
        if (reachErr is not null) return BadRequest(reachErr);

        await _db.SaveDataAsync(@"
            UPDATE TaskSubmissions
            SET    DriveUrl = @DriveUrl,
                   Notes    = @Notes
            WHERE  Id = @Id",
            new
            {
                Id       = id,
                DriveUrl = driveUrl,
                Notes    = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            });

        return Ok();
    }

    // ── DELETE /api/task-submissions/{id} ──────────────────────────────────
    // Student-side delete of *own* pending submission. Same gate as PATCH.
    // After removing the row we recompute Tasks.Status from surviving
    // submissions so the task UI reflects the rollback:
    //   • 0 surviving rows                 → Status = 'Open'
    //   • newest survivor.MentorStatus='Returned' → Status = 'ReturnedForRevision'
    //   • else (defensive)                 → Status = 'SubmittedToMentor'
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteMine(int id, int authUserId)
    {
        var gate = await LoadOwnPendingLatestAsync(id, authUserId);
        if (gate.Result is not null) return gate.Result;
        int taskId = gate.TaskId;

        // Hard delete — the row is the latest *and* unreviewed, so there's
        // no history value in keeping it. Associated TaskSubmissionFiles
        // (none for Drive-link rows; legacy uploads can't end up here
        // because legacy rows were created pre-cutover) cascade via the
        // existing FK ON DELETE CASCADE.
        int affected = await _db.SaveDataAsync(
            "DELETE FROM TaskSubmissions WHERE Id = @Id",
            new { Id = id });
        if (affected == 0) return NotFound("ההגשה לא נמצאה");

        // Recompute Task.Status from surviving submissions.
        var survivor = (await _db.GetRecordsAsync<SurvivorRow>(@"
            SELECT  Id, MentorStatus
            FROM    TaskSubmissions
            WHERE   TaskId = @TaskId
            ORDER   BY Id DESC
            LIMIT   1", new { TaskId = taskId }))?.FirstOrDefault();

        string newStatus = survivor is null
            ? "Open"
            : survivor.MentorStatus == "Returned"
                ? "ReturnedForRevision"
                : "SubmittedToMentor";

        await _db.SaveDataAsync(
            "UPDATE Tasks SET Status = @S WHERE Id = @Id",
            new { Id = taskId, S = newStatus });

        return Ok(new { taskId, taskStatus = newStatus });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers for student-side edit / delete
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates that the calling student owns the submission (via team
    /// membership), that the submission is still Pending, and that it is the
    /// latest row for its task. Returns the task id on success; otherwise an
    /// already-formed error response in <c>Result</c>.
    /// </summary>
    private async Task<(IActionResult? Result, int TaskId)> LoadOwnPendingLatestAsync(
        int submissionId, int authUserId)
    {
        var row = (await _db.GetRecordsAsync<OwnSubmissionGateRow>(@"
            SELECT  s.Id, s.TaskId, s.MentorStatus,
                    (SELECT MAX(s2.Id) FROM TaskSubmissions s2 WHERE s2.TaskId = s.TaskId) AS LatestId
            FROM    TaskSubmissions s
            JOIN    Tasks       t  ON t.Id   = s.TaskId
            JOIN    Projects    p  ON p.Id   = t.ProjectId
            JOIN    Teams       tm ON tm.Id  = p.TeamId
            JOIN    TeamMembers m  ON m.TeamId = tm.Id
            WHERE   s.Id        = @SubmissionId
              AND   m.UserId    = @UserId
              AND   m.IsActive  = 1
            LIMIT 1",
            new { SubmissionId = submissionId, UserId = authUserId }))?.FirstOrDefault();

        if (row is null)
            return (NotFound("ההגשה לא נמצאה או שאין לך הרשאה"), 0);

        if (!string.Equals(row.MentorStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            return (StatusCode(StatusCodes.Status409Conflict,
                "ההגשה כבר נבדקה ע״י המנחה ולא ניתנת לעריכה או מחיקה"), 0);

        if (row.LatestId != submissionId)
            return (StatusCode(StatusCodes.Status409Conflict,
                "ניתן לערוך/למחוק רק את ההגשה האחרונה"), 0);

        return (null, row.TaskId);
    }

    private sealed class OwnSubmissionGateRow
    {
        public int    Id           { get; set; }
        public int    TaskId       { get; set; }
        public string MentorStatus { get; set; } = "";
        public int    LatestId     { get; set; }
    }

    private sealed class SurvivorRow
    {
        public int    Id           { get; set; }
        public string MentorStatus { get; set; } = "";
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
    // Also syncs Tasks.Status so the student task list reflects the decision.
    [HttpPatch("{id:int}/mentor-review")]
    [Authorize(Roles = Roles.Mentor + "," + Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> MentorReview(
        int id, [FromBody] MentorReviewRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.MentorStatus) ||
            (req.MentorStatus != "Approved" && req.MentorStatus != "Returned"))
            return BadRequest("ערך לא תקין. ערכים חוקיים: Approved, Returned");

        // Returning a submission for correction MUST include a non-empty
        // mentor note — otherwise the student receives a "fix this" signal
        // with zero context. Whitespace-only text is also rejected.
        if (req.MentorStatus == "Returned" && string.IsNullOrWhiteSpace(req.MentorFeedback))
            return BadRequest("יש להזין הערת מנחה לפני החזרת ההגשה לתיקון");

        // Resolve the TaskId before updating so we can sync Tasks.Status
        var subRow = (await _db.GetRecordsAsync<SubmissionTaskRow>(
            "SELECT TaskId FROM TaskSubmissions WHERE Id = @Id", new { Id = id }))
            ?.FirstOrDefault();

        if (subRow is null) return NotFound("ההגשה לא נמצאה");

        // ── Ownership gate ───────────────────────────────────────────────────
        //
        // The [Authorize] attribute above proves the caller holds the Mentor
        // role — it proves NOTHING about which projects are theirs. Without the
        // check below, any mentor in the programme could approve or return any
        // submission in the database, including for teams they have never met.
        // Approving is not a soft action: it sets Tasks.Status to
        // 'ApprovedForSubmission' and unlocks the student's Moodle confirmation
        // step, and returning notifies the whole team.
        //
        // This is the same rule, resolved the same way, that
        // ProjectRequestsController.SubmitMentorRecommendation already applies
        // to the mentor's other decision endpoint ("never trust the role
        // alone"). Stated here so the two mentor write paths agree.
        //
        // Admin/Staff are intentionally unscoped, exactly as before: they hold
        // the lecturer-override endpoint on this same controller and are the
        // programme-wide authority. Their behaviour is unchanged.
        bool isMentorOnly = User.IsInRole(Roles.Mentor)
                            && !User.IsInRole(Roles.Admin)
                            && !User.IsInRole(Roles.Staff);

        if (isMentorOnly)
        {
            // Joined from the submission rather than taken from the request, so
            // the caller cannot nominate the project they are checked against.
            const string ownershipSql = @"
                SELECT 1
                FROM   TaskSubmissions ts
                JOIN   Tasks           t   ON t.Id  = ts.TaskId
                JOIN   ProjectMentors  pm  ON pm.ProjectId = t.ProjectId
                WHERE  ts.Id     = @Id
                  AND  pm.UserId = @UserId
                LIMIT  1";

            var owns = (await _db.GetRecordsAsync<int>(
                ownershipSql, new { Id = id, UserId = authUserId }))?.FirstOrDefault();

            if (owns != 1) return Forbid();
        }

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

        // Notify relevant users of the review decision
        try
        {
            const string contextSql = @"
                SELECT t.Title AS TaskTitle
                FROM   TaskSubmissions ts
                JOIN   Tasks           t  ON ts.TaskId = t.Id
                WHERE  ts.Id = @Id
                LIMIT  1";

            var taskTitle = (await _db.GetRecordsAsync<TitleRow>(
                contextSql, new { Id = id }))?.FirstOrDefault()?.TaskTitle ?? "";

            if (req.MentorStatus == "Returned" && !string.IsNullOrEmpty(taskTitle))
            {
                // Notify all active team members that their submission was returned
                const string membersSql = @"
                    SELECT m.UserId
                    FROM   TaskSubmissions ts
                    JOIN   Tasks           t  ON ts.TaskId   = t.Id
                    JOIN   Projects        p  ON t.ProjectId = p.Id
                    JOIN   Teams           tm ON p.TeamId    = tm.Id
                    JOIN   TeamMembers     m  ON tm.Id       = m.TeamId
                    WHERE  ts.Id       = @Id
                      AND  m.IsActive  = 1";

                var studentIds = (await _db.GetRecordsAsync<int>(
                    membersSql, new { Id = id }))?.ToList() ?? new();

                await NotificationHelper.CreateForUsersAsync(
                    _db, studentIds,
                    title:             "ההגשה הוחזרה לתיקון",
                    message:           $"המשימה {taskTitle} הוחזרה לתיקונים על ידי המנחה",
                    type:              "SubmissionReturned",
                    relatedEntityType: "TaskSubmission",
                    relatedEntityId:   subRow.TaskId);  // TaskId so student submissions page can deep-link
            }
            // The "new final submission to review" notification to admin/staff
            // was retired on 2026-05-17 along with the lecturer-final-review
            // flow. Mentor approval no longer escalates automatically; admins
            // see status through the project dashboard / explicit escalation.
        }
        catch { /* notifications are best-effort */ }

        // Sync Tasks.Status to match the mentor's decision so the student
        // task list and detail modal always reflect the true review state.
        string newTaskStatus = req.MentorStatus == "Approved"
            ? "ApprovedForSubmission"
            : "ReturnedForRevision";

        await _db.SaveDataAsync(
            "UPDATE Tasks SET Status = @Status WHERE Id = @TaskId",
            new { Status = newTaskStatus, subRow.TaskId });

        return Ok();
    }

    // ── GET /api/task-submissions/{id}/lecturer-detail ────────────────────
    //
    // Full detail for the lecturer review drawer: identity, mentor side,
    // lecturer review fields, and attached files in one round-trip.
    // Visibility is gated on MentorStatus = 'Approved' so drafts never leak.
    [HttpGet("{id:int}/lecturer-detail")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> GetLecturerDetail(int id, int authUserId)
    {
        const string sql = @"
            SELECT  s.Id,
                    s.TaskId,
                    t.Title                              AS TaskTitle,
                    p.Id                                 AS ProjectId,
                    p.ProjectNumber,
                    p.Title                              AS ProjectTitle,
                    tm.TeamName                          AS TeamName,
                    mt.Title                             AS MilestoneTitle,
                    s.SubmittedByUserId,
                    u.FirstName || ' ' || u.LastName     AS SubmittedByName,
                    s.SubmittedAt,
                    s.Notes,
                    s.MentorStatus,
                    s.MentorFeedback,
                    s.MentorReviewedAt,
                    COALESCE(s.MoodleSubmittedAt, s.CourseSubmittedAt) AS CourseSubmittedAt,
                    s.Status,
                    COALESCE(s.ReviewStatus, 'PendingReview') AS ReviewStatus,
                    s.ReviewerFeedback,
                    COALESCE(s.IsFeedbackPublished, 0)   AS IsFeedbackPublished,
                    s.FeedbackPublishedAt,
                    rb.FirstName || ' ' || rb.LastName   AS ReviewedByName,
                    (SELECT GROUP_CONCAT(mu.FirstName || ' ' || mu.LastName, ', ')
                     FROM   ProjectMentors pmm
                     JOIN   users          mu ON mu.Id = pmm.UserId
                     WHERE  pmm.ProjectId = p.Id)        AS MentorName
            FROM    TaskSubmissions       s
            JOIN    Tasks                 t   ON t.Id   = s.TaskId
            JOIN    users                 u   ON u.Id   = s.SubmittedByUserId
            JOIN    ProjectMilestones     pm  ON pm.Id  = t.ProjectMilestoneId
            JOIN    Projects              p   ON p.Id   = pm.ProjectId
            LEFT JOIN Teams               tm  ON tm.Id  = p.TeamId
            JOIN    AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            JOIN    MilestoneTemplates    mt  ON mt.Id  = aym.MilestoneTemplateId
            LEFT JOIN users               rb  ON rb.Id  = s.ReviewedByUserId
            WHERE   s.Id = @Id
              AND   s.MentorStatus = 'Approved'
            LIMIT   1";

        var row = (await _db.GetRecordsAsync<LecturerSubmissionDetailDto>(
                      sql, new { Id = id }))?.FirstOrDefault();

        if (row is null)
            return NotFound("ההגשה לא נמצאה או שטרם אושרה על ידי המנחה");

        const string filesSql = @"
            SELECT  Id, TaskSubmissionId, OriginalFileName, StoredFileName,
                    ContentType, SizeBytes, UploadedAt,
                    COALESCE(IsLecturerFeedback, 0) AS IsLecturerFeedback,
                    FilePublishedAt
            FROM    TaskSubmissionFiles
            WHERE   TaskSubmissionId = @SubId
            ORDER   BY Id";
        row.Files = (await _db.GetRecordsAsync<TaskSubmissionFileDto>(
                        filesSql, new { SubId = id }))?.ToList() ?? new();

        // ── Full submission history for this task (newest first) ─────────────
        const string historySql = @"
            SELECT  s.Id,
                    s.SubmittedAt,
                    s.Notes,
                    s.Status,
                    s.ReviewerFeedback,
                    s.MentorStatus,
                    s.MentorFeedback,
                    s.MentorReviewedAt,
                    COALESCE(s.MoodleSubmittedAt, s.CourseSubmittedAt) AS CourseSubmittedAt,
                    COALESCE(s.ReviewStatus, 'PendingReview') AS ReviewStatus,
                    COALESCE(s.IsFeedbackPublished, 0)        AS IsFeedbackPublished,
                    s.FeedbackPublishedAt
            FROM    TaskSubmissions s
            WHERE   s.TaskId = @TaskId
            ORDER   BY s.Id DESC";

        var history = (await _db.GetRecordsAsync<SubmissionHistoryItemDto>(
                          historySql, new { row.TaskId }))?.ToList() ?? new();

        if (history.Count > 0)
        {
            const string historyFilesSql = @"
                SELECT  f.Id, f.TaskSubmissionId, f.OriginalFileName, f.StoredFileName,
                        f.ContentType, f.SizeBytes, f.UploadedAt,
                        COALESCE(f.IsLecturerFeedback, 0) AS IsLecturerFeedback,
                        f.FilePublishedAt
                FROM    TaskSubmissionFiles f
                WHERE   f.TaskSubmissionId IN
                    (SELECT s2.Id FROM TaskSubmissions s2 WHERE s2.TaskId = @TaskId)
                ORDER   BY f.TaskSubmissionId DESC, f.Id";

            var historyFiles = (await _db.GetRecordsAsync<TaskSubmissionFileDto>(
                                   historyFilesSql, new { row.TaskId }))?.ToList() ?? new();

            var filesBySub = historyFiles
                .GroupBy(f => f.TaskSubmissionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var h in history)
                h.Files = filesBySub.GetValueOrDefault(h.Id) ?? new();
        }

        row.SubmissionHistory = history;

        return Ok(row);
    }

    // ── PATCH /api/task-submissions/{id}/lecturer-review ──────────────────
    // DEPRECATED 2026-05-17. The lecturer-final-review flow has been retired
    // — final deliverables go through Moodle now. Returns 410 Gone.
    [HttpPatch("{id:int}/lecturer-review")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public IActionResult SaveLecturerReview_Deprecated(int id) =>
        StatusCode(StatusCodes.Status410Gone, new
        {
            error = "lecturer_review_disabled",
            message = "תהליך הבדיקה הסופית הוסר."
        });

    // ── Legacy implementation kept below as a private no-op for reference. ─
    private async Task<IActionResult> SaveLecturerReviewLegacy(
        int id, [FromBody] SaveLecturerReviewRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.ReviewStatus) ||
            !LecturerReviewStatuses.All.Contains(req.ReviewStatus))
            return BadRequest("סטטוס בדיקה לא תקין");

        // Verify the submission exists and is mentor-approved
        const string checkSql = @"
            SELECT MentorStatus FROM TaskSubmissions WHERE Id = @Id LIMIT 1";
        var mentorStatus = (await _db.GetRecordsAsync<string>(
            checkSql, new { Id = id }))?.FirstOrDefault();

        if (mentorStatus is null) return NotFound("ההגשה לא נמצאה");
        if (mentorStatus != "Approved")
            return BadRequest("ניתן לבדוק רק הגשות שאושרו על ידי המנחה");

        const string updateSql = @"
            UPDATE TaskSubmissions
            SET    ReviewStatus     = @ReviewStatus,
                   ReviewerFeedback = @ReviewerFeedback,
                   ReviewedByUserId = @AuthUserId
            WHERE  Id = @Id";

        await _db.SaveDataAsync(updateSql, new
        {
            req.ReviewStatus,
            ReviewerFeedback = string.IsNullOrWhiteSpace(req.ReviewerFeedback)
                ? null : req.ReviewerFeedback.Trim(),
            AuthUserId = authUserId,
            Id         = id,
        });

        return Ok();
    }

    // ── POST /api/task-submissions/{id}/publish-feedback ──────────────────
    // DEPRECATED 2026-05-17. Lecturer feedback is no longer published inside
    // our system; mentor feedback is the only review surface students see.
    [HttpPost("{id:int}/publish-feedback")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public IActionResult PublishFeedback_Deprecated(int id) =>
        StatusCode(StatusCodes.Status410Gone, new
        {
            error = "lecturer_review_disabled",
            message = "תהליך הבדיקה הסופית הוסר."
        });

    // ── Legacy implementation kept below as a private no-op for reference. ─
    private async Task<IActionResult> PublishFeedbackLegacy(int id, int authUserId)
    {
        const string loadSql = @"
            SELECT s.Id, s.TaskId, s.MentorStatus,
                   COALESCE(s.ReviewStatus, 'PendingReview')   AS ReviewStatus,
                   s.ReviewerFeedback,
                   COALESCE(s.IsFeedbackPublished, 0)          AS IsFeedbackPublished,
                   t.Title    AS TaskTitle,
                   p.Id       AS ProjectId
            FROM   TaskSubmissions s
            JOIN   Tasks    t ON s.TaskId    = t.Id
            JOIN   Projects p ON t.ProjectId = p.Id
            WHERE  s.Id = @Id
            LIMIT  1";

        var row = (await _db.GetRecordsAsync<PublishLoadRow>(
                      loadSql, new { Id = id }))?.FirstOrDefault();

        if (row is null) return NotFound("ההגשה לא נמצאה");
        if (row.MentorStatus != "Approved")
            return BadRequest("ניתן לפרסם משוב רק לאחר אישור מנחה");
        if (string.IsNullOrWhiteSpace(row.ReviewerFeedback))
            return BadRequest("לא ניתן לפרסם משוב ריק. כתבי משוב לפני הפרסום.");

        const string updateSql = @"
            UPDATE TaskSubmissions
            SET    IsFeedbackPublished = 1,
                   FeedbackPublishedAt = datetime('now'),
                   ReviewedByUserId    = @AuthUserId,
                   ReviewStatus        = CASE
                       WHEN COALESCE(ReviewStatus,'PendingReview') = 'PendingReview'
                            THEN 'FeedbackReturned'
                       ELSE ReviewStatus
                   END
            WHERE  Id = @Id";
        await _db.SaveDataAsync(updateSql, new { Id = id, AuthUserId = authUserId });

        // Stamp any pending lecturer files so they become visible to students.
        await _db.SaveDataAsync(@"
            UPDATE TaskSubmissionFiles
            SET    FilePublishedAt = datetime('now')
            WHERE  TaskSubmissionId               = @Id
              AND  COALESCE(IsLecturerFeedback,0) = 1
              AND  FilePublishedAt IS NULL", new { Id = id });

        // Notify all active team members. Best-effort.
        try
        {
            const string teamSql = @"
                SELECT m.UserId
                FROM   Projects   p
                JOIN   Teams      tm ON tm.Id = p.TeamId
                JOIN   TeamMembers m  ON m.TeamId = tm.Id
                WHERE  p.Id      = @ProjectId
                  AND  m.IsActive = 1";
            var memberIds = (await _db.GetRecordsAsync<int>(
                                teamSql, new { row.ProjectId }))?.ToList() ?? new();

            if (memberIds.Count > 0)
            {
                // Lecturer feedback published → SubmissionFeedbackReceived
                // routed through the dispatcher so per-user email prefs apply.
                await NotificationDispatcher.DispatchAsync(
                    _db, _email, memberIds,
                    title:             "התקבל משוב מרצה חדש",
                    message:           $"התקבל משוב מרצה חדש עבור {row.TaskTitle}",
                    type:              NotificationTypes.SubmissionFeedbackReceived,
                    relatedEntityType: "TaskSubmission",
                    relatedEntityId:   row.TaskId);
            }
        }
        catch { /* notifications are best-effort */ }

        return Ok();
    }

    // ── POST /api/task-submissions/{id}/lecturer-files ────────────────────
    //
    // Uploads one or more lecturer-feedback files. Reuses wwwroot/submissions/
    // and TaskSubmissionFiles, flagged with IsLecturerFeedback = 1.
    // Newly-uploaded files have FilePublishedAt = NULL until republish.
    // Allowed only on mentor-approved submissions; admin/staff only.
    [HttpPost("{id:int}/lecturer-files")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> UploadLecturerFiles(
        int id, [FromBody] UploadLecturerFilesRequest req, int authUserId)
    {
        if (req.Files.Count == 0)
            return BadRequest("יש לבחור לפחות קובץ אחד להעלאה");
        if (req.Files.Count > MaxLecturerFilesPerCall)
            return BadRequest("ניתן להעלות עד " + MaxLecturerFilesPerCall + " קבצים בפעם אחת");

        const string checkSql = "SELECT MentorStatus FROM TaskSubmissions WHERE Id = @Id LIMIT 1";
        var mentorStatus = (await _db.GetRecordsAsync<string>(checkSql, new { Id = id }))?.FirstOrDefault();
        if (mentorStatus is null) return NotFound("ההגשה לא נמצאה");
        if (mentorStatus != "Approved")
            return BadRequest("ניתן להעלות קבצי משוב רק להגשות שאושרו על ידי המנחה");

        for (int i = 0; i < req.Files.Count; i++)
        {
            var f       = req.Files[i];
            int fileNo  = i + 1;
            string name = f.OriginalFileName ?? "";
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("קובץ " + fileNo + ": חסר שם קובץ");
            if (string.IsNullOrWhiteSpace(f.FileBase64))
                return BadRequest("קובץ " + fileNo + ": חסר תוכן קובץ");
            if (f.SizeBytes > MaxLecturerFileBytes)
                return BadRequest("קובץ \"" + name + "\" חורג מהגודל המקסימלי ("
                                  + MaxLecturerFileMb + " MB)");
        }

        // Skip duplicates: an existing lecturer-file with the same OriginalFileName
        // is a no-op. Caller can replace by deleting first.
        const string existingSql = @"
            SELECT LOWER(OriginalFileName)
            FROM   TaskSubmissionFiles
            WHERE  TaskSubmissionId = @SubId AND COALESCE(IsLecturerFeedback,0) = 1";
        var existing = (await _db.GetRecordsAsync<string>(existingSql, new { SubId = id }))
            ?.ToHashSet() ?? new HashSet<string>();

        const string insertFileSql = @"
            INSERT INTO TaskSubmissionFiles
                (TaskSubmissionId, OriginalFileName, StoredFileName, ContentType, SizeBytes,
                 UploadedAt, IsLecturerFeedback, FilePublishedAt)
            VALUES
                (@SubId, @OriginalFileName, @StoredFileName, @ContentType, @SizeBytes,
                 datetime('now'), 1, NULL)";

        int saved = 0;
        foreach (var f in req.Files)
        {
            if (existing.Contains(f.OriginalFileName.ToLowerInvariant())) continue;

            string storedFileName;
            try
            {
                storedFileName = await _filesManage.SaveRawFile(f.FileBase64, f.OriginalFileName, Container);
            }
            catch { continue; }

            await _db.SaveDataAsync(insertFileSql, new
            {
                SubId            = id,
                OriginalFileName = f.OriginalFileName,
                StoredFileName   = storedFileName,
                ContentType      = string.IsNullOrWhiteSpace(f.ContentType)
                                       ? "application/octet-stream"
                                       : f.ContentType,
                SizeBytes        = f.SizeBytes,
            });
            saved++;
        }

        return Ok(new { saved });
    }

    // ── DELETE /api/task-submissions/{id}/lecturer-files/{fileId} ─────────
    //
    // Removes a single lecturer-feedback file at any time. After publish,
    // students lose access to the file immediately. Lecturers can re-upload
    // and re-publish to roll forward. Admin/staff only.
    [HttpDelete("{id:int}/lecturer-files/{fileId:int}")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> DeleteLecturerFile(
        int id, int fileId, int authUserId,
        [FromServices] IWebHostEnvironment env)
    {
        const string loadSql = @"
            SELECT  f.Id                              AS FileId,
                    f.StoredFileName,
                    COALESCE(f.IsLecturerFeedback, 0) AS IsLecturerFeedback
            FROM    TaskSubmissionFiles f
            WHERE   f.Id               = @FileId
              AND   f.TaskSubmissionId = @SubId
            LIMIT   1";

        var row = (await _db.GetRecordsAsync<DeleteLecturerFileRow>(
                      loadSql, new { SubId = id, FileId = fileId }))?.FirstOrDefault();
        if (row is null) return NotFound("הקובץ לא נמצא");
        if (row.IsLecturerFeedback != 1)
            return BadRequest("ניתן למחוק רק קבצי משוב מרצה דרך נקודת קצה זו");

        await _db.SaveDataAsync(
            "DELETE FROM TaskSubmissionFiles WHERE Id = @FileId", new { FileId = fileId });

        try
        {
            string path = Path.Combine(env.WebRootPath, Container, row.StoredFileName);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch { /* non-fatal */ }

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
                    f.ContentType,
                    COALESCE(f.IsLecturerFeedback, 0)  AS IsLecturerFeedback,
                    f.FilePublishedAt                  AS FilePublishedAt,
                    COALESCE(s.IsFeedbackPublished, 0) AS IsFeedbackPublished
            FROM    TaskSubmissionFiles f
            JOIN    TaskSubmissions     s ON s.Id = f.TaskSubmissionId
            WHERE   f.Id               = @FileId
              AND   f.TaskSubmissionId = @SubId";

        var row = (await _db.GetRecordsAsync<FileDownloadRow>(
                      sql, new { FileId = fileId, SubId = id }))
                  ?.FirstOrDefault();

        if (row is null) return NotFound("הקובץ לא נמצא");

        // Gate lecturer-feedback files for students. Visible only when the
        // parent feedback is published AND this specific file has been
        // stamped with FilePublishedAt (handles republish-after-edit cleanly).
        if (row.IsLecturerFeedback == 1 && User.IsInRole(Roles.Student))
        {
            if (row.IsFeedbackPublished != 1 || row.FilePublishedAt is null)
                return NotFound("הקובץ לא זמין");
        }

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

    // ── GET /api/task-submissions/pending-mentor-approvals ────────────────────
    //
    // Lecturer/admin supervision inbox — surfaces submissions waiting on a
    // mentor approval. Joins project/team/mentor/milestone context so the
    // table can render without N+1 round-trips. Days-waiting is computed
    // server-side from SubmittedAt so the client doesn't have to.
    //
    // Older rows that were force-approved before this feature shipped are
    // implicitly excluded — MentorStatus = 'Pending' is the gate.
    [HttpGet("pending-mentor-approvals")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> GetPendingMentorApprovals(int authUserId)
    {
        const string sql = @"
            SELECT
                s.Id                                  AS SubmissionId,
                s.TaskId,
                t.Title                               AS TaskTitle,
                p.Id                                  AS ProjectId,
                p.ProjectNumber,
                p.Title                               AS ProjectTitle,
                tm.TeamName                           AS TeamName,
                mt.Title                              AS MilestoneTitle,
                s.SubmittedAt,
                CAST((julianday('now') - julianday(s.SubmittedAt)) AS INTEGER) AS DaysWaiting,
                s.MentorStatus,
                s.LastMentorReminderAt,
                (SELECT GROUP_CONCAT(mu.FirstName || ' ' || mu.LastName, ', ')
                 FROM   ProjectMentors pmm
                 JOIN   users          mu ON mu.Id = pmm.UserId
                 WHERE  pmm.ProjectId = p.Id)         AS MentorName
            FROM   TaskSubmissions        s
            JOIN   Tasks                  t   ON t.Id   = s.TaskId
            JOIN   ProjectMilestones      pm  ON pm.Id  = t.ProjectMilestoneId
            JOIN   Projects               p   ON p.Id   = pm.ProjectId
            LEFT JOIN Teams               tm  ON tm.Id  = p.TeamId
            JOIN   AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            JOIN   MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            WHERE  s.MentorStatus = 'Pending'
            ORDER  BY s.SubmittedAt ASC";

        var rows = await _db.GetRecordsAsync<PendingMentorApprovalRowDto>(sql, new { });
        return Ok(rows ?? Enumerable.Empty<PendingMentorApprovalRowDto>());
    }

    // ── POST /api/task-submissions/{id}/remind-mentor ─────────────────────────
    //
    // Sends an in-app + email reminder to the project's mentors that a
    // submission is still pending review. Email respects per-user notification
    // preferences via NotificationDispatcher. The student is intentionally
    // never notified.
    //
    // 24h cooldown: refuses to fire again within 24 hours of LastMentorReminderAt.
    [HttpPost("{id:int}/remind-mentor")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> RemindMentor(int id, int authUserId)
    {
        // Load enough context to validate and to build a useful message.
        const string loadSql = @"
            SELECT  s.Id, s.MentorStatus, s.LastMentorReminderAt,
                    s.TaskId, t.Title AS TaskTitle,
                    p.Id  AS ProjectId, p.Title AS ProjectTitle, p.ProjectNumber
            FROM    TaskSubmissions s
            JOIN    Tasks    t ON t.Id = s.TaskId
            JOIN    ProjectMilestones pm ON pm.Id = t.ProjectMilestoneId
            JOIN    Projects p ON p.Id = pm.ProjectId
            WHERE   s.Id = @Id
            LIMIT   1";
        var ctx = (await _db.GetRecordsAsync<RemindMentorContext>(
            loadSql, new { Id = id }))?.FirstOrDefault();
        if (ctx is null) return NotFound("ההגשה לא נמצאה");
        if (ctx.MentorStatus != "Pending")
            return BadRequest("ההגשה כבר טופלה על ידי המנחה");

        // Cooldown — block back-to-back reminders within 24h.
        if (ctx.LastMentorReminderAt is DateTime last
            && DateTime.UtcNow - last.ToUniversalTime() < TimeSpan.FromHours(24))
            return BadRequest("נשלחה תזכורת לאחרונה. ניתן לשלוח שוב לאחר 24 שעות.");

        // Resolve the mentors of this project — the audience for the reminder.
        const string mentorsSql = @"
            SELECT pm.UserId
            FROM   ProjectMentors pm
            JOIN   users          u  ON u.Id = pm.UserId
            WHERE  pm.ProjectId = @ProjectId
              AND  u.IsActive    = 1";
        var mentorIds = (await _db.GetRecordsAsync<int>(
            mentorsSql, new { ctx.ProjectId }))?.ToList() ?? new();
        if (mentorIds.Count == 0)
            return BadRequest("לא נמצאו מנחים לפרויקט זה");

        // Stamp the reminder timestamp first so cooldown holds even if the
        // notification dispatch is slow.
        await _db.SaveDataAsync(@"
            UPDATE TaskSubmissions
            SET    LastMentorReminderAt        = datetime('now'),
                   LastMentorReminderByUserId  = @UserId
            WHERE  Id = @Id",
            new { UserId = authUserId, Id = id });

        try
        {
            await NotificationDispatcher.DispatchAsync(
                _db, _email, mentorIds,
                title:             "תזכורת: הגשה ממתינה לאישורך",
                message:           $"ההגשה למשימה \"{ctx.TaskTitle}\" בפרויקט \"{ctx.ProjectTitle}\" עדיין ממתינה לאישור מנחה.",
                type:              NotificationTypes.SubmissionSubmitted,
                relatedEntityType: "TaskSubmission",
                relatedEntityId:   id);
        }
        catch { /* notifications are best-effort */ }

        return Ok();
    }

    // ── PUT /api/task-submissions/{id}/lecturer-override-approve ──────────────
    //
    // Lecturer/admin approves a submission without waiting for the mentor.
    // The reason is REQUIRED and stays internal — students never see it.
    // Mirrors the regular MentorReview="Approved" effect (sets MentorStatus,
    // syncs Tasks.Status) and additionally records the override audit fields.
    //
    // Notifies the team that the task was approved; the message intentionally
    // does NOT include OverrideReason. ApprovalSource is recorded so the
    // mentor / admin staff can later see this was lecturer-driven.
    [HttpPut("{id:int}/lecturer-override-approve")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> LecturerOverrideApprove(
        int id, [FromBody] LecturerOverrideApproveRequest req, int authUserId)
    {
        if (req is null) return BadRequest("גוף בקשה ריק");
        var reason = (req.OverrideReason ?? "").Trim();
        if (reason.Length == 0)        return BadRequest("חובה להזין סיבה לעקיפה");
        if (reason.Length > 2000)      return BadRequest("הסיבה ארוכה מדי");

        const string loadSql = @"
            SELECT  s.Id, s.MentorStatus, s.TaskId,
                    t.Title  AS TaskTitle
            FROM    TaskSubmissions s
            JOIN    Tasks t ON t.Id = s.TaskId
            WHERE   s.Id = @Id
            LIMIT   1";
        var ctx = (await _db.GetRecordsAsync<OverrideContext>(
            loadSql, new { Id = id }))?.FirstOrDefault();
        if (ctx is null) return NotFound("ההגשה לא נמצאה");
        if (ctx.MentorStatus == "Approved")
            return BadRequest("ההגשה כבר אושרה");

        // Persist override + mirror the mentor-approved effect so existing
        // pipelines (course submission gate, lecturer review surface) keep
        // treating the submission as approved.
        await _db.SaveDataAsync(@"
            UPDATE TaskSubmissions
            SET    MentorStatus             = 'Approved',
                   MentorReviewedAt         = datetime('now'),
                   ApprovalSource           = @ApprovalSource,
                   OverrideReason           = @Reason,
                   OverrideApprovedByUserId = @UserId,
                   OverrideApprovedAt       = datetime('now')
            WHERE  Id = @Id",
            new
            {
                ApprovalSource = ApprovalSources.LecturerOverride,
                Reason         = reason,
                UserId         = authUserId,
                Id             = id,
            });

        // Sync Tasks.Status so the student task list reflects approval.
        await _db.SaveDataAsync(
            "UPDATE Tasks SET Status = 'ApprovedForSubmission' WHERE Id = @TaskId",
            new { ctx.TaskId });

        // Notify the student team — but never include the override reason.
        try
        {
            const string membersSql = @"
                SELECT m.UserId
                FROM   TaskSubmissions ts
                JOIN   Tasks           t  ON ts.TaskId   = t.Id
                JOIN   Projects        p  ON t.ProjectId = p.Id
                JOIN   Teams           tm ON p.TeamId    = tm.Id
                JOIN   TeamMembers     m  ON tm.Id       = m.TeamId
                WHERE  ts.Id      = @Id
                  AND  m.IsActive = 1";
            var studentIds = (await _db.GetRecordsAsync<int>(
                membersSql, new { Id = id }))?.ToList() ?? new();

            if (studentIds.Count > 0)
            {
                await NotificationDispatcher.DispatchAsync(
                    _db, _email, studentIds,
                    title:             "ההגשה אושרה",
                    message:           $"ההגשה למשימה \"{ctx.TaskTitle}\" אושרה.",
                    type:              NotificationTypes.SubmissionFeedbackReceived,
                    relatedEntityType: "TaskSubmission",
                    relatedEntityId:   ctx.TaskId);
            }
        }
        catch { /* notifications are best-effort */ }

        return Ok();
    }

    // ── Private Dapper row types ─────────────────────────────────────────────

    private sealed class RemindMentorContext
    {
        public int       Id                   { get; set; }
        public string    MentorStatus         { get; set; } = "";
        public DateTime? LastMentorReminderAt { get; set; }
        public int       TaskId               { get; set; }
        public string    TaskTitle            { get; set; } = "";
        public int       ProjectId            { get; set; }
        public string    ProjectTitle         { get; set; } = "";
        public int       ProjectNumber        { get; set; }
    }

    private sealed class OverrideContext
    {
        public int    Id           { get; set; }
        public string MentorStatus { get; set; } = "";
        public int    TaskId       { get; set; }
        public string TaskTitle    { get; set; } = "";
    }

    private sealed class SubmissionTaskRow { public int TaskId { get; set; } }

    private sealed class SubmissionNotifyRow
    {
        public string TaskTitle { get; set; } = "";
        public string TeamName  { get; set; } = "";
    }

    private sealed class TitleRow { public string TaskTitle { get; set; } = ""; }

    private sealed class CourseSumbitCheckRow
    {
        public int      Id                 { get; set; }
        public int      TaskId             { get; set; }
        public string   MentorStatus       { get; set; } = "";
        public string?  CourseSubmittedAt  { get; set; }
        public int      ProjectId          { get; set; }
    }

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
        public string    OriginalFileName    { get; set; } = "";
        public string    StoredFileName      { get; set; } = "";
        public string    ContentType         { get; set; } = "";
        public int       IsLecturerFeedback  { get; set; }
        public DateTime? FilePublishedAt     { get; set; }
        public int       IsFeedbackPublished { get; set; }
    }

    private sealed class DeleteLecturerFileRow
    {
        public int    FileId             { get; set; }
        public string StoredFileName     { get; set; } = "";
        public int    IsLecturerFeedback { get; set; }
    }

    private sealed class PublishLoadRow
    {
        public int     Id                  { get; set; }
        public int     TaskId              { get; set; }
        public string  MentorStatus        { get; set; } = "";
        public string? ReviewStatus        { get; set; }
        public string? ReviewerFeedback    { get; set; }
        public int     IsFeedbackPublished { get; set; }
        public string  TaskTitle           { get; set; } = "";
        public int     ProjectId           { get; set; }
    }
}