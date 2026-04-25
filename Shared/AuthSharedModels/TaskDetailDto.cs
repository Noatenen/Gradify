namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Task Detail DTOs  —  GET /api/projects/tasks/{taskId}/detail
//
//  Returned by the student task-detail endpoint.
//  Contains the full task record + the complete submission history for that task.
//
//  Status separation (by design):
//    TaskDetailDto.Status          = student progress  ("Open"|"InProgress"|"Done")
//    SubmissionHistoryItemDto.Status       = reviewer decision  ("Submitted"|"Reviewed"|"NeedsRevision")
//    SubmissionHistoryItemDto.MentorStatus = mentor decision    ("Pending"|"Approved"|"Returned")
//
//  Students MAY update TaskDetailDto.Status via
//    PATCH /api/projects/tasks/{taskId}/progress
//
//  Students MAY NOT update submission Status or MentorStatus directly.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Full task detail returned to a student — task info + full submission history.</summary>
public class TaskDetailDto
{
    // ── Task identity ─────────────────────────────────────────────────────────
    public int       Id               { get; set; }
    public string    Title            { get; set; } = "";
    public string?   Description      { get; set; }
    public string    MilestoneTitle   { get; set; } = "";
    /// <summary>When the task was created / opened for this project. Maps to Tasks.CreatedAt.</summary>
    public DateTime? OpenDate         { get; set; }
    public DateTime? DueDate          { get; set; }
    /// <summary>Student-controlled progress: "Open" | "InProgress" | "Done".</summary>
    public string    Status           { get; set; } = "Open";
    /// <summary>"Personal" | "System" | "Mentor"</summary>
    public string    TaskType         { get; set; } = "";
    public bool      IsSubmission     { get; set; }
    public string?   SubmissionInstructions { get; set; }
    public int?      MaxFilesCount    { get; set; }
    public int?      MaxFileSizeMb    { get; set; }
    public string?   AllowedFileTypes { get; set; }

    // ── Latest submission state (direct server-computed — always populated) ──
    /// <summary>Mentor decision on the latest submission. Null if never submitted.</summary>
    public string?   LatestMentorStatus     { get; set; }
    /// <summary>Id of the latest TaskSubmissions row. Null if never submitted.</summary>
    public int?      LatestSubmissionId      { get; set; }
    /// <summary>When the student forwarded the latest submission to course staff.</summary>
    public DateTime? LatestCourseSubmittedAt { get; set; }

    // ── Submission history (oldest first) ────────────────────────────────────
    public List<SubmissionHistoryItemDto> Submissions { get; set; } = new();
}

/// <summary>
/// One entry in the submission history for a task.
/// Contains the approval status from both the reviewer (admin/staff/lecturer)
/// and the mentor, plus any feedback text.
/// </summary>
public class SubmissionHistoryItemDto
{
    public int       Id               { get; set; }
    public DateTime  SubmittedAt      { get; set; }
    /// <summary>Notes the student added at submission time.</summary>
    public string?   Notes            { get; set; }
    /// <summary>Reviewer (admin/staff) decision: "Submitted" | "Reviewed" | "NeedsRevision".</summary>
    public string    Status           { get; set; } = "Submitted";
    /// <summary>Feedback from reviewer when Status = "NeedsRevision".</summary>
    public string?   ReviewerFeedback { get; set; }
    /// <summary>Mentor decision: "Pending" | "Approved" | "Returned".</summary>
    public string    MentorStatus       { get; set; } = "Pending";
    /// <summary>Feedback from mentor when MentorStatus = "Returned".</summary>
    public string?   MentorFeedback     { get; set; }
    public DateTime? MentorReviewedAt   { get; set; }
    /// <summary>When the student formally forwarded this submission to course staff. Null if not yet forwarded.</summary>
    public DateTime? CourseSubmittedAt  { get; set; }
    public List<TaskSubmissionFileDto> Files { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Request models
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Payload for PATCH /api/projects/tasks/{taskId}/progress (student only).</summary>
public class UpdateTaskProgressRequest
{
    /// <summary>
    /// "Open" | "InProgress" | "SubmittedToMentor" | "ReturnedForRevision" |
    /// "RevisionSubmitted" | "ApprovedForSubmission" | "Done"
    /// </summary>
    public string Status { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────────────────────
//  Student sub-task DTOs  —  student-only internal checklist items
//  Stored in StudentSubTasks table; scoped to a team + parent system task.
//  Not visible to mentors or lecturers.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One internal sub-task visible only to the student team.</summary>
public class StudentSubTaskDto
{
    public int       Id            { get; set; }
    public int       TaskId        { get; set; }
    public string    Title         { get; set; } = "";
    public bool      IsDone        { get; set; }
    /// <summary>"Open" | "InProgress" | "Done"</summary>
    public string    Status        { get; set; } = "Open";
    public DateTime? DueDate       { get; set; }
    public string?   Notes         { get; set; }
    public DateTime  CreatedAt     { get; set; }
    public string    CreatedByName { get; set; } = "";
}

/// <summary>Payload for POST /api/projects/tasks/{taskId}/subtasks.</summary>
public class CreateSubTaskRequest
{
    public string    Title   { get; set; } = "";
    /// <summary>"Open" | "InProgress" | "Done"</summary>
    public string    Status  { get; set; } = "Open";
    public DateTime? DueDate { get; set; }
    public string?   Notes   { get; set; }
}

/// <summary>Payload for PATCH /api/projects/subtasks/{id} (full update, student only).</summary>
public class UpdateSubTaskRequest
{
    public string    Title   { get; set; } = "";
    /// <summary>"Open" | "InProgress" | "Done"</summary>
    public string    Status  { get; set; } = "Open";
    public DateTime? DueDate { get; set; }
    public string?   Notes   { get; set; }
}

/// <summary>Payload for PATCH /api/task-submissions/{id}/mentor-review (mentor only).</summary>
public class MentorReviewRequest
{
    /// <summary>"Approved" | "Returned"</summary>
    public string  MentorStatus    { get; set; } = "";
    public string? MentorFeedback  { get; set; }
}
