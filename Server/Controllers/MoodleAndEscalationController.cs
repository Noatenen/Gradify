using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  MoodleAndEscalationController
//
//  Two thin surfaces introduced 2026-05-17 alongside the retirement of the
//  lecturer-final-review flow:
//
//   1. PATCH /api/projects/{projectId}/moodle-status
//      Manual per-project Moodle tracking. Admin/Staff only. Sets one of:
//        "SubmittedToMoodle" | "NotSubmittedToMoodle" | "Unknown"  (or NULL).
//
//   2. POST  /api/task-submissions/{submissionId}/escalate
//      POST  /api/task-submissions/{submissionId}/unescalate
//      Minimal "needs lecturer attention" toggle on a single submission.
//      Mentors / Admin / Staff can flip; admins/staff get a one-shot
//      notification when raised.
//
//  Deliberately kept thin — no review queues, no state machines.
// ─────────────────────────────────────────────────────────────────────────────

[ApiController]
[ServiceFilter(typeof(AuthCheck))]
public class MoodleAndEscalationController : ControllerBase
{
    private readonly DbRepository _db;
    public MoodleAndEscalationController(DbRepository db) => _db = db;

    // ═══════════════════════════════════════════════════════════════════════
    //  Moodle status
    // ═══════════════════════════════════════════════════════════════════════

    // ── GET /api/projects/{projectId}/moodle-status ────────────────────────
    // Returns the current Moodle-tracking row for a project. Authenticated
    // users (any role) can read it — the dashboard surface needs it.
    [HttpGet("api/projects/{projectId:int}/moodle-status")]
    [Authorize]
    public async Task<IActionResult> GetMoodleStatus(int projectId, int authUserId)
    {
        const string sql = @"
            SELECT  p.Id                              AS ProjectId,
                    p.MoodleSubmissionStatus          AS Status,
                    p.MoodleSubmissionNotes           AS Notes,
                    p.MoodleSubmissionUpdatedAt       AS UpdatedAt,
                    p.MoodleSubmissionUpdatedByUserId AS UpdatedByUserId,
                    CASE
                        WHEN u.Id IS NULL THEN ''
                        ELSE TRIM(COALESCE(u.FirstName,'') || ' ' || COALESCE(u.LastName,''))
                    END                                AS UpdatedByName
            FROM    Projects p
            LEFT JOIN users u ON u.Id = p.MoodleSubmissionUpdatedByUserId
            WHERE   p.Id = @Id
            LIMIT 1";

        var row = (await _db.GetRecordsAsync<ProjectMoodleStatusDto>(
            sql, new { Id = projectId }))?.FirstOrDefault();

        if (row is null) return NotFound("הפרויקט לא נמצא");
        return Ok(row);
    }

    // ── PATCH /api/projects/{projectId}/moodle-status ──────────────────────
    [HttpPatch("api/projects/{projectId:int}/moodle-status")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> SaveMoodleStatus(int projectId, int authUserId,
        [FromBody] SaveProjectMoodleStatusRequest req)
    {
        if (req is null) return BadRequest("גוף בקשה ריק");

        // Empty string == null == clear back to "not tracked".
        string? normalized = string.IsNullOrWhiteSpace(req.Status) ? null : req.Status.Trim();
        if (normalized is not null && !MoodleSubmissionStatuses.All.Contains(normalized))
            return BadRequest("ערך סטטוס לא חוקי");

        var exists = (await _db.GetRecordsAsync<int>(
            "SELECT 1 FROM Projects WHERE Id = @Id LIMIT 1",
            new { Id = projectId }))?.Any() ?? false;
        if (!exists) return NotFound("הפרויקט לא נמצא");

        int affected = await _db.SaveDataAsync(@"
            UPDATE Projects
            SET    MoodleSubmissionStatus          = @Status,
                   MoodleSubmissionNotes           = @Notes,
                   MoodleSubmissionUpdatedAt       = datetime('now'),
                   MoodleSubmissionUpdatedByUserId = @UserId,
                   UpdatedAt                       = datetime('now')
            WHERE  Id = @Id",
            new
            {
                Id     = projectId,
                Status = normalized,
                Notes  = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes!.Trim(),
                UserId = authUserId,
            });

        if (affected == 0) return StatusCode(500, "שגיאה בשמירת הסטטוס");

        return await GetMoodleStatus(projectId, authUserId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Submission escalation — "needs lecturer attention"
    // ═══════════════════════════════════════════════════════════════════════

    // ── POST /api/task-submissions/{id}/escalate ───────────────────────────
    // Idempotent: re-flagging is a no-op. Notifies admin/staff once on first
    // raise (matched by the EscalatedToLecturer transition 0 → 1).
    [HttpPost("api/task-submissions/{id:int}/escalate")]
    [Authorize(Roles = Roles.Mentor + "," + Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Escalate(int id, int authUserId,
        [FromBody] EscalateSubmissionRequest? req)
    {
        var row = (await _db.GetRecordsAsync<EscalateLoadRow>(@"
            SELECT  s.Id, s.TaskId, s.EscalatedToLecturer,
                    t.Title  AS TaskTitle,
                    p.Id     AS ProjectId,
                    p.Title  AS ProjectTitle,
                    p.ProjectNumber
            FROM    TaskSubmissions s
            JOIN    Tasks            t ON t.Id = s.TaskId
            JOIN    Projects         p ON p.Id = t.ProjectId
            WHERE   s.Id = @Id LIMIT 1",
            new { Id = id }))?.FirstOrDefault();
        if (row is null) return NotFound("ההגשה לא נמצאה");

        if (row.EscalatedToLecturer == 1)
            return Ok(new { id, alreadyEscalated = true });

        string reason = (req?.Reason ?? "").Trim();
        if (reason.Length > 1000) reason = reason[..1000];

        await _db.SaveDataAsync(@"
            UPDATE TaskSubmissions
            SET    EscalatedToLecturer = 1,
                   EscalatedAt         = datetime('now'),
                   EscalatedByUserId   = @UserId,
                   EscalationReason    = @Reason
            WHERE  Id = @Id AND EscalatedToLecturer = 0",
            new { Id = id, UserId = authUserId, Reason = reason });

        // One-shot notification to admin/staff.
        try
        {
            var lecturerIds = (await _db.GetRecordsAsync<int>(@"
                SELECT u.Id FROM users u
                JOIN   UserRoles ur ON ur.UserId = u.Id
                WHERE  ur.Role IN ('Admin','Staff') AND u.IsActive = 1"))?.ToList() ?? new();

            if (lecturerIds.Count > 0)
            {
                await NotificationHelper.CreateForUsersAsync(
                    _db, lecturerIds,
                    title:             "הגשה סומנה כמקרה חריג",
                    message:           $"הגשה במשימה ״{row.TaskTitle}״ (פרויקט {row.ProjectNumber}) סומנה לטיפול מרצה.",
                    type:              "SubmissionEscalated",
                    relatedEntityType: "TaskSubmission",
                    relatedEntityId:   id);
            }
        }
        catch { /* notifications best-effort */ }

        return Ok(new { id, alreadyEscalated = false });
    }

    // ── POST /api/task-submissions/{id}/unescalate ─────────────────────────
    // Lecturer/Admin closes the escalation. No notification.
    [HttpPost("api/task-submissions/{id:int}/unescalate")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Unescalate(int id, int authUserId)
    {
        int affected = await _db.SaveDataAsync(@"
            UPDATE TaskSubmissions
            SET    EscalatedToLecturer = 0
            WHERE  Id = @Id AND EscalatedToLecturer = 1",
            new { Id = id });

        // 0 affected = it wasn't escalated; treat as a no-op success so the
        // UI doesn't show a scary error when the state was already resolved.
        return Ok(new { id, wasEscalated = affected > 0 });
    }

    private sealed class EscalateLoadRow
    {
        public int    Id                  { get; set; }
        public int    TaskId              { get; set; }
        public int    EscalatedToLecturer { get; set; }
        public string TaskTitle           { get; set; } = "";
        public int    ProjectId           { get; set; }
        public string ProjectTitle        { get; set; } = "";
        public int    ProjectNumber       { get; set; }
    }
}