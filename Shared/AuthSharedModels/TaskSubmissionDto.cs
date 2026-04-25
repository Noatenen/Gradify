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
}

// ─────────────────────────────────────────────────────────────────────────────
//  Request models
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Payload for POST /api/task-submissions.
/// Contains the submission record and all files in one atomic request.
/// Files are base64-encoded following the same pattern as ResourceFiles uploads.
/// </summary>
public class CreateSubmissionRequest
{
    public int     TaskId { get; set; }
    public string? Notes  { get; set; }

    /// <summary>
    /// Files to attach. Must satisfy the task's snapshot policy:
    ///   Count ≤ Tasks.MaxFilesCount,
    ///   each SizeBytes ≤ Tasks.MaxFileSizeMb × 1 048 576,
    ///   extension in Tasks.AllowedFileTypes (when defined).
    /// </summary>
    public List<SubmissionFileRequest> Files { get; set; } = new();
}

/// <summary>One file within a CreateSubmissionRequest.</summary>
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
    public DateTime? LatestCourseSubmittedAt { get; set; }
    public DateTime? LatestSubmittedAt       { get; set; }
}