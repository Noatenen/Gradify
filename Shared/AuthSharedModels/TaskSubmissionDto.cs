using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Task Submission DTOs  —  /api/task-submissions
//
//  A submission belongs to an operational Task (Tasks.IsSubmission = true).
//  Submission policy (MaxFilesCount / MaxFileSizeMb / AllowedFileTypes) is
//  validated against the snapshot columns on the Tasks table, NOT against
//  TaskTemplates. This keeps the domain clean at runtime.
//
//  Status lifecycle:
//    Submitted → Reviewed | NeedsRevision
//
//  File storage mirrors the ResourceFiles pattern:
//    wwwroot/submissions/<guid><ext>
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Full submission record including all attached files.
/// Returned by GET /api/task-submissions/{id}.
/// </summary>
public class TaskSubmissionDto
{
    public int      Id                { get; set; }
    public int      TaskId            { get; set; }
    public string   TaskTitle         { get; set; } = "";

    public int      SubmittedByUserId { get; set; }
    public string   SubmittedByName   { get; set; } = "";

    public DateTime SubmittedAt       { get; set; }
    public string?  Notes             { get; set; }

    /// <summary>"Submitted" | "Reviewed" | "NeedsRevision"</summary>
    public string   Status            { get; set; } = "Submitted";

    public DateTime CreatedAt         { get; set; }

    /// <summary>
    /// Public Google Drive link (drive.google.com / docs.google.com) that the
    /// student submitted. Null on legacy rows submitted before 2026-05-19 —
    /// those rows carry attachments in <see cref="Files"/> instead.
    /// </summary>
    public string?  DriveUrl          { get; set; }

    /// <summary>
    /// Historical uploaded files. New submissions never populate this list —
    /// the student flow is Drive-link-only since 2026-05-19. Kept for read
    /// access to pre-cutover submissions.
    /// </summary>
    public List<TaskSubmissionFileDto> Files { get; set; } = new();
}

/// <summary>
/// Slim submission row for list views — no embedded files.
/// Returned by GET /api/task-submissions?taskId=N.
/// </summary>
public class TaskSubmissionSummaryDto
{
    public int      Id                { get; set; }
    public int      TaskId            { get; set; }
    public string   TaskTitle         { get; set; } = "";
    public int      SubmittedByUserId { get; set; }
    public string   SubmittedByName   { get; set; } = "";
    public DateTime SubmittedAt       { get; set; }
    public string?  Notes             { get; set; }
    /// <summary>"Submitted" | "Reviewed" | "NeedsRevision"</summary>
    public string   Status            { get; set; } = "Submitted";
    /// <summary>Drive link on new (post-2026-05-19) submissions. Null on legacy rows.</summary>
    public string?  DriveUrl          { get; set; }
    /// <summary>Legacy: file count for pre-cutover submissions. Always 0 for Drive-link rows.</summary>
    public int      FileCount         { get; set; }
}

/// <summary>File metadata record attached to a submission.</summary>
public class TaskSubmissionFileDto
{
    public int      Id               { get; set; }
    public int      TaskSubmissionId { get; set; }
    public string   OriginalFileName { get; set; } = "";
    /// <summary>GUID-based stored filename under wwwroot/submissions/. Used to build the download URL.</summary>
    public string   StoredFileName   { get; set; } = "";
    public string   ContentType      { get; set; } = "";
    /// <summary>Raw size in bytes. Stored for display and policy re-check without disk reads.</summary>
    public long     SizeBytes        { get; set; }
    public DateTime UploadedAt       { get; set; }
    /// <summary>true ⇒ uploaded by lecturer/admin as feedback. false ⇒ student submission file.</summary>
    public bool     IsLecturerFeedback { get; set; }
    /// <summary>When this lecturer-feedback file became visible to students. NULL ⇒ draft.
    /// Always NULL for student-uploaded files (they are visible immediately).</summary>
    public DateTime? FilePublishedAt { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Request models
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Payload for POST /api/task-submissions.
/// As of 2026-05-19 the student submission flow accepts a Google Drive link
/// only — uploaded files are no longer stored on our server. The
/// <see cref="Files"/> property is retained for wire-compatibility but is
/// ignored server-side and must not be populated by new clients.
/// </summary>
public class CreateSubmissionRequest
{
    public int     TaskId    { get; set; }
    public string? Notes     { get; set; }

    /// <summary>
    /// Public Google Drive link (drive.google.com / docs.google.com).
    /// Required. The server validates format AND performs a lightweight
    /// reachability probe before persisting the submission. If the link is
    /// not shared the submission is rejected with a Hebrew error message.
    /// </summary>
    public string? DriveUrl  { get; set; }

    /// <summary>
    /// DEPRECATED 2026-05-19. Ignored by the server. New clients must send
    /// <see cref="DriveUrl"/> instead. The property remains on the wire so
    /// older clients that still post an empty list don't break.
    /// </summary>
    [Obsolete("File uploads removed from student submissions; use DriveUrl.")]
    public List<SubmissionFileRequest> Files { get; set; } = new();
}

/// <summary>
/// One file within a CreateSubmissionRequest.
/// </summary>
/// <remarks>
/// Deprecated 2026-05-19. Student submissions no longer accept files; the
/// DTO is kept only so deserialisation of legacy payloads doesn't fail.
/// </remarks>
[Obsolete("Student submissions no longer accept uploaded files. Use DriveUrl.")]
public class SubmissionFileRequest
{
    public string OriginalFileName { get; set; } = "";
    /// <summary>Base64-encoded file content — same encoding as ResourceFiles.</summary>
    public string FileBase64       { get; set; } = "";
    public string ContentType      { get; set; } = "";
    /// <summary>Raw size in bytes, sent by the client for fast pre-validation.</summary>
    public long   SizeBytes        { get; set; }
}

/// <summary>Payload for PATCH /api/task-submissions/{id}/status.</summary>
public class UpdateSubmissionStatusRequest
{
    /// <summary>"Submitted" | "Reviewed" | "NeedsRevision"</summary>
    public string  Status          { get; set; } = "";
    /// <summary>Optional feedback text — stored when Status = "NeedsRevision".</summary>
    public string? ReviewerFeedback { get; set; }
}

/// <summary>
/// Payload for PATCH /api/task-submissions/{id}. Students use this to edit
/// their own *latest* submission before the mentor reviews it — replace the
/// Drive link and/or update the notes. Server enforces ownership +
/// MentorStatus='Pending' + latest-row gating.
/// </summary>
public class UpdateMySubmissionRequest
{
    public string?  DriveUrl { get; set; }
    public string?  Notes    { get; set; }
}

/// <summary>Payload for POST /api/task-submissions/validate-drive-link.</summary>
public class ValidateDriveLinkRequest
{
    public string?  DriveUrl { get; set; }
}

/// <summary>Response for the live Drive-link validator. <c>Ok=true</c> means
/// the link passes both format and accessibility checks. <c>Error</c> is the
/// server-supplied Hebrew message to render inline when <c>Ok=false</c>.</summary>
public class ValidateDriveLinkResponse
{
    public bool     Ok     { get; set; }
    public string?  Error  { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Student-facing submission task view
//  Returned by GET /api/projects/my-submission-tasks and
//              GET /api/projects/my-submission-tasks/{taskId}
//
//  Contains both the operational task policy snapshot and the latest
//  submission state so the student UI can render the full card in one shot.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// An operational submission task with its current submission state.
/// Used by the /submissions page and the SubmissionModal.
/// </summary>
public class StudentSubmissionTaskDto
{
    public int      TaskId                  { get; set; }
    public string   TaskTitle               { get; set; } = "";
    public string?  Description             { get; set; }
    public string   MilestoneTitle          { get; set; } = "";
    public DateTime? DueDate                { get; set; }
    /// <summary>"Open" | "InProgress" | "Done"</summary>
    public string   TaskStatus              { get; set; } = "Open";
    public string?  SubmissionInstructions  { get; set; }
    public int?     MaxFilesCount           { get; set; }
    public int?     MaxFileSizeMb           { get; set; }
    public string?  AllowedFileTypes        { get; set; }

    // ── Latest submission state ──────────────────────────────────────────────
    /// <summary>Total number of submissions made for this task.</summary>
    public int      SubmissionCount         { get; set; }
    /// <summary>DB Id of the most recent TaskSubmissions row. Null if never submitted.</summary>
    public int?     LatestSubmissionId      { get; set; }
    /// <summary>"Submitted" | "Reviewed" | "NeedsRevision" — null if never submitted.</summary>
    public string?  LatestSubmissionStatus  { get; set; }
    /// <summary>"Pending" | "Approved" | "Returned" — null if never submitted.</summary>
    public string?   LatestMentorStatus      { get; set; }
    /// <summary>When the student forwarded the latest submission to course staff. Null if not yet forwarded.</summary>
    /// <remarks>
    /// Since 2026-05-19 the semantics are "student confirmed the official
    /// Moodle submission". The server fills this from
    /// <c>COALESCE(MoodleSubmittedAt, CourseSubmittedAt)</c> so legacy rows
    /// keep rendering as already-submitted.
    /// </remarks>
    public DateTime? LatestCourseSubmittedAt { get; set; }
    /// <summary>Display name of the student who clicked "הגשתי במודל". Empty
    /// when not yet marked, or for legacy rows that pre-date the audit
    /// column.</summary>
    public string   LatestMoodleSubmittedByName { get; set; } = "";
    public DateTime? LatestSubmittedAt       { get; set; }
    /// <summary>Drive link of the latest submission. Null on legacy file-based rows.</summary>
    public string?  LatestDriveUrl          { get; set; }

    // ── Lecturer published feedback (read-only on the student side) ──────────
    // The server fills these only when IsFeedbackPublished = 1 — drafts are
    // never serialized to students.
    public string?   LecturerFeedback              { get; set; }
    public DateTime? LecturerFeedbackPublishedAt   { get; set; }
    /// <summary>One of LecturerReviewStatuses.* — only for published reviews.</summary>
    public string?   LecturerReviewStatus          { get; set; }
    /// <summary>Lecturer-uploaded files visible to students (FilePublishedAt IS NOT NULL).
    /// Empty when there is no published feedback yet.</summary>
    public List<TaskSubmissionFileDto> LecturerFeedbackFiles { get; set; } = new();
}