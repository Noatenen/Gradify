using System;
using System.Collections.Generic;
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
    /// <summary>Feedback from reviewer. The client must only render this when IsFeedbackPublished = true
    /// (or, for legacy NeedsRevision flows, when Status = "NeedsRevision"). Drafts are still wired through
    /// for backward compatibility but should never be exposed without the gate.</summary>
    public string?   ReviewerFeedback { get; set; }
    /// <summary>One of LecturerReviewStatuses.* — the lecturer review queue state for this submission.</summary>
    public string?   ReviewStatus        { get; set; }
    /// <summary>True ⇒ lecturer feedback (text + files) has been published to the student.</summary>
    public bool      IsFeedbackPublished { get; set; }
    public DateTime? FeedbackPublishedAt { get; set; }
    /// <summary>Mentor decision: "Pending" | "Approved" | "Returned".</summary>
    public string    MentorStatus       { get; set; } = "Pending";
    /// <summary>Feedback from mentor when MentorStatus = "Returned".</summary>
    public string?   MentorFeedback     { get; set; }
    public DateTime? MentorReviewedAt   { get; set; }
    /// <summary>When the student formally forwarded this submission to course staff. Null if not yet forwarded.</summary>
    public DateTime? CourseSubmittedAt  { get; set; }
    /// <summary>Drive link of this submission (post-2026-05-19). Null on legacy file-based rows.</summary>
    public string?   DriveUrl           { get; set; }
    public List<TaskSubmissionFileDto> Files { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Request models
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Payload for PATCH /api/projects/tasks/{taskId}/progress (student only).</summary>
public class UpdateTaskProgressRequest
{
    /// <summary>
    /// "Open" | "InProgress" — the only statuses a student may set manually,
    /// and only before the task has its first mentor submission. All later
    /// statuses (SubmittedToMentor, ReturnedForRevision, RevisionSubmitted,
    /// ApprovedForSubmission, Done, Moodle-confirmed) are system/mentor-owned;
    /// the server rejects attempts to set them here.
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

// ─────────────────────────────────────────────────────────────────────────────
//  Personal task DTOs  —  student-private reminder/checklist items
//  Stored in PersonalTasks table; scoped to a single user.
//  Not linked to project milestones or the shared Tasks table.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One personal reminder task visible only to the owning student.</summary>
public class PersonalTaskDto
{
    public int       Id          { get; set; }
    public string    Title       { get; set; } = "";
    public string?   Description { get; set; }
    public DateTime? DueDate     { get; set; }
    public bool      IsDone      { get; set; }
    public DateTime  CreatedAt   { get; set; }

    /// <summary>
    /// Optional project context. Null means "ללא שיוך".
    ///
    /// <para><b>Only ever populated when the caller still mentors that
    /// project.</b> The read query joins through ProjectMentors, so a task whose
    /// project the mentor has since lost access to comes back with this null and
    /// the titles below null — the row keeps working and simply shows no
    /// association. That is a deliberate one-way degradation: ownership of the
    /// task is never affected, and no project or team name can leak through a
    /// stale reference.</para>
    /// </summary>
    public int?      ProjectId    { get; set; }

    /// <summary>Resolved server-side alongside ProjectId, never sent by the
    /// client. Null whenever ProjectId is null.</summary>
    public string?   ProjectTitle { get; set; }
    public string?   TeamName     { get; set; }

    /// <summary>
    /// Optional work block on <see cref="DueDate"/>, as Israel wall-clock
    /// "HH:mm". Null means the item is date-only and belongs in the calendar's
    /// ללא שעה band.
    ///
    /// <para><b>Not a due datetime.</b> DueDate answers "which day is this
    /// for"; these answer "when on that day do I plan to sit with it". The two
    /// never merge — a task can be due Thursday with no hour attached, which is
    /// what every row created before this pair existed still looks like.</para>
    ///
    /// <para>Both are set together or both are null. The server refuses a
    /// half-set pair and refuses an end that is not after the start, rather
    /// than inventing the missing half.</para>
    /// </summary>
    public string?   StartTime   { get; set; }

    /// <summary>End of the optional work block. See <see cref="StartTime"/>.</summary>
    public string?   EndTime     { get; set; }
}

/// <summary>Payload for POST /api/projects/personal-tasks.</summary>
public class CreatePersonalTaskRequest
{
    public string    Title       { get; set; } = "";
    public string?   Description { get; set; }
    public DateTime? DueDate     { get; set; }

    /// <summary>Optional project to associate. Null = "ללא שיוך". The server
    /// re-checks this against the caller's own mentor assignments and refuses
    /// anything else — it is a request, never a grant.</summary>
    public int?      ProjectId   { get; set; }

    /// <summary>
    /// Optional work block on <see cref="DueDate"/>, as Israel wall-clock
    /// "HH:mm". Null means the item is date-only and belongs in the calendar's
    /// ללא שעה band.
    ///
    /// <para><b>Not a due datetime.</b> DueDate answers "which day is this
    /// for"; these answer "when on that day do I plan to sit with it". The two
    /// never merge — a task can be due Thursday with no hour attached, which is
    /// what every row created before this pair existed still looks like.</para>
    ///
    /// <para>Both are set together or both are null. The server refuses a
    /// half-set pair and refuses an end that is not after the start, rather
    /// than inventing the missing half.</para>
    /// </summary>
    public string?   StartTime   { get; set; }

    /// <summary>End of the optional work block. See <see cref="StartTime"/>.</summary>
    public string?   EndTime     { get; set; }
}

/// <summary>
/// Payload for PUT /api/projects/personal-tasks/{id}.
///
/// <para>Carries the editable fields only. IsDone is absent on purpose — it has
/// its own toggle endpoint, and letting a save also flip completion would give
/// two ways to change one bit. UserId is absent because nothing may reassign a
/// task to a different owner.</para>
/// </summary>
public class UpdatePersonalTaskRequest
{
    public string    Title       { get; set; } = "";
    public string?   Description { get; set; }
    public DateTime? DueDate     { get; set; }

    /// <summary>Optional project to associate, re-validated server-side exactly
    /// as on create. Sending null clears the association back to "ללא שיוך".</summary>
    public int?      ProjectId   { get; set; }

    /// <summary>
    /// Optional work block on <see cref="DueDate"/>, as Israel wall-clock
    /// "HH:mm". Null means the item is date-only and belongs in the calendar's
    /// ללא שעה band.
    ///
    /// <para><b>Not a due datetime.</b> DueDate answers "which day is this
    /// for"; these answer "when on that day do I plan to sit with it". The two
    /// never merge — a task can be due Thursday with no hour attached, which is
    /// what every row created before this pair existed still looks like.</para>
    ///
    /// <para>Both are set together or both are null. The server refuses a
    /// half-set pair and refuses an end that is not after the start, rather
    /// than inventing the missing half.</para>
    /// </summary>
    public string?   StartTime   { get; set; }

    /// <summary>End of the optional work block. See <see cref="StartTime"/>.</summary>
    public string?   EndTime     { get; set; }
}
