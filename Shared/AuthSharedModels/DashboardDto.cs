using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ────────────────────────────────────────────────────────────────────────────
//  Dashboard DTO — single response object for GET /api/projects/my-dashboard
//
//  Designed to be UI-friendly:
//    • DB join complexity is resolved server-side
//    • No raw FK ids exposed to the client
//    • Status values are normalised strings, not DB enums
//    • All nullable fields are safe to render with null-checks only
// ────────────────────────────────────────────────────────────────────────────

/// <summary>Complete dashboard payload. Project is null when the user has no team/project yet.</summary>
public class DashboardDto
{
    /// <summary>True when the user is in at least one active team, even if the team has no project yet.</summary>
    public bool                     HasTeam      { get; set; }
    public ProjectInfoDto?          Project      { get; set; }
    public List<TeamMemberDto>      TeamMembers  { get; set; } = new();
    public List<ContactDto>         Mentors      { get; set; } = new();
    public List<MilestoneSummaryDto> Milestones  { get; set; } = new();
    public UpcomingDeadlineDto?     NextDeadline { get; set; }
    public List<OpenRequestDto>     OpenRequests { get; set; } = new();
    /// <summary>Tasks with no ProjectMilestoneId — not part of the academic
    /// milestone/submission process, created ad-hoc by a team member (e.g. an
    /// internal coordination task). Previously silently dropped by this
    /// endpoint (only milestone-linked tasks were ever assembled); surfaced
    /// explicitly so the Home Page can distinguish "משימות מערכת" from
    /// "משימות צוות" using a real field instead of guessing from title text.</summary>
    public List<TaskSummaryDto>     TeamTasks    { get; set; } = new();
}

// ── Project ──────────────────────────────────────────────────────────────────

/// <summary>Core project metadata shown in the summary card header.</summary>
public class ProjectInfoDto
{
    public int     Id            { get; set; }
    public int     ProjectNumber { get; set; }
    public string  Title        { get; set; } = "";
    public string  Description  { get; set; } = "";
    /// <summary>"InProgress", "Completed", "Paused", etc.</summary>
    public string  Status       { get; set; } = "";
    /// <summary>"OnTrack", "NeedsAttention", "AtRisk" — optional.</summary>
    public string? HealthStatus { get; set; }
    public string  ProjectType  { get; set; } = "";
    /// <summary>Academic year display name ("תשפ"ה" / "2025-2026"); empty
    /// when not assigned. Surfaced on the dashboard project card.</summary>
    public string  AcademicYear { get; set; } = "";
}

// ── People ───────────────────────────────────────────────────────────────────

/// <summary>Student member of the project team.</summary>
public class TeamMemberDto
{
    public int     UserId     { get; set; }
    public string  FullName   { get; set; } = "";
    public string? MemberRole { get; set; }
}

/// <summary>A person with contact details — used for mentors.</summary>
public class ContactDto
{
    public int    UserId   { get; set; }
    public string FullName { get; set; } = "";
    public string Email    { get; set; } = "";
    public string Phone    { get; set; } = "";
}

// ── Milestones & Tasks ───────────────────────────────────────────────────────

/// <summary>
/// Flattened milestone — merges MilestoneTemplates + AcademicYearMilestones + ProjectMilestones
/// into a single, UI-ready row. Tasks are pre-grouped inside.
/// </summary>
public class MilestoneSummaryDto
{
    public int     ProjectMilestoneId { get; set; }
    public string  Title              { get; set; } = "";
    public int     OrderIndex         { get; set; }
    /// <summary>"NotStarted" | "InProgress" | "Completed" | "Delayed"</summary>
    public string  Status             { get; set; } = "NotStarted";
    public DateTime? OpenDate         { get; set; }
    public DateTime? DueDate          { get; set; }
    public DateTime? CloseDate        { get; set; }
    public DateTime? CompletedAt      { get; set; }
    /// <summary>
    /// Server-derived: true when today is inside the milestone's effective
    /// visibility window (OpenDate/CloseDate, per AcademicYearMilestones).
    /// Same rule as the Tasks page's IsCurrentlyOpen — the single source of
    /// truth for "is this milestone active" so the dashboard's Active Tasks
    /// card and the Tasks tab always agree on which milestones to show.
    /// </summary>
    public bool      IsCurrentlyOpen  { get; set; }
    /// <summary>Tasks belonging to this milestone, ordered by DueDate.</summary>
    public List<TaskSummaryDto> Tasks { get; set; } = new();
}

/// <summary>Task row. "Overdue" state is computed client-side: DueDate &lt; today &amp;&amp; Status != "Done".</summary>
public class TaskSummaryDto
{
    public int     Id             { get; set; }
    public string  Title          { get; set; } = "";
    public string? Description    { get; set; }
    public string  Status         { get; set; } = "Open";
    public DateTime? DueDate      { get; set; }
    public string  AssignedToName { get; set; } = "";
    /// <summary>True ⇒ this task goes through the mentor-review / Moodle-confirmation
    /// pipeline (Tasks.IsSubmission). Used to decide how "fully complete" is determined —
    /// see ActiveTasksCard.IsTaskFullyComplete.</summary>
    public bool      IsSubmission           { get; set; }
    /// <summary>Reviewer decision on the latest submission. Null if never submitted.</summary>
    public string?   LatestSubmissionStatus { get; set; }
    /// <summary>Mentor decision on the latest submission. Null if never submitted.</summary>
    public string?   LatestMentorStatus     { get; set; }
    /// <summary>When the latest submission was created. Null if never submitted.</summary>
    public DateTime? LatestSubmittedAt      { get; set; }
    /// <summary>When the mentor reviewed the latest submission (TaskSubmissions.
    /// MentorReviewedAt). Null if never reviewed — used for the Home Page's
    /// "הוחזר לתיקון לפני X ימים" style relative-time status.</summary>
    public DateTime? LatestMentorReviewedAt { get; set; }
    /// <summary>True ⇒ the latest submission has been confirmed as submitted to Moodle
    /// (TaskSubmissions.MoodleSubmittedAt or legacy CourseSubmittedAt is set). False if
    /// never submitted or not yet confirmed.</summary>
    public bool      LatestMoodleConfirmed  { get; set; }

    /// <summary>PHASE 1 canonical urgency flag — computed by TaskUrgencyService
    /// (design/business-logic-consolidation-epic.md, Concept 4), not re-derived
    /// here. Mandatory, no submission yet, past the effective due date.</summary>
    public bool      IsOverdue       { get; set; }
    /// <summary>Canonical reason — see TaskAttentionReasons in this namespace.
    /// "None" when nothing about this task currently needs attention.</summary>
    public string    AttentionReason { get; set; } = TaskAttentionReasons.None;
    /// <summary>Canonical priority rank from TaskUrgencyService — lower is more
    /// urgent; int.MaxValue when AttentionReason is None.</summary>
    public int       AttentionRank   { get; set; } = int.MaxValue;
}

// ── Upcoming deadline ─────────────────────────────────────────────────────────

/// <summary>Derived server-side: the nearest incomplete milestone's deadline.</summary>
public class UpcomingDeadlineDto
{
    public int?     TaskId             { get; set; }
    public string   Title              { get; set; } = "";
    public DateTime DueDate            { get; set; }
    /// <summary>Mentor decision on the latest submission. "Returned" means action is required.</summary>
    public string?  LatestMentorStatus { get; set; }
}

// ── Open requests ─────────────────────────────────────────────────────────────

/// <summary>Active (non-closed) student request.</summary>
public class OpenRequestDto
{
    public int      Id          { get; set; }
    public string   Title       { get; set; } = "";
    public string   RequestType { get; set; } = "";
    public string   Status      { get; set; } = "";
    public DateTime OpenedAt    { get; set; }
}
