using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ────────────────────────────────────────────────────────────────────────────
//  Tasks page DTO  —  GET /api/projects/my-tasks
//
//  Design decisions:
//    • Status values are raw DB strings ("Open"|"InProgress"|"Done").
//    • MilestoneStatus is embedded on every TaskItemDto so the UI can
//      determine pending state without re-joining data client-side.
//    • ActiveGroups and CompletedGroups are pre-split server-side so
//      the page components are purely presentational.
//    • Priority is derived from IsMandatory (no separate DB column exists).
// ────────────────────────────────────────────────────────────────────────────

/// <summary>Full payload for the student tasks page.</summary>
public class TasksPageDto
{
    // ── Student / project identity ────────────────────────────────────────────
    public string StudentName   { get; set; } = "";
    public int    ProjectNumber { get; set; }
    public string ProjectTitle  { get; set; } = "";

    // ── Summary counts ────────────────────────────────────────────────────────
    /// <summary>
    /// Tasks with Status != "Done" whose parent milestone is InProgress or Delayed.
    /// These are actionable right now.
    /// </summary>
    public int ActiveCount { get; set; }

    /// <summary>
    /// Tasks that require student action: ReturnedForRevision, SubmittedToMentor, or RevisionSubmitted.
    /// These are grouped under "דורשות תשומת לב" on the tasks page.
    /// </summary>
    public int PendingCount { get; set; }

    /// <summary>Tasks with Status == "Done".</summary>
    public int CompletedCount { get; set; }

    // ── Task groups ───────────────────────────────────────────────────────────
    /// <summary>
    /// Milestones that contain at least one non-Done task.
    /// Each group's Tasks list contains only non-Done tasks.
    /// </summary>
    public List<TaskMilestoneGroupDto> ActiveGroups { get; set; } = new();

    /// <summary>
    /// Milestones that contain at least one Done task.
    /// Each group's Tasks list contains only Done tasks.
    /// </summary>
    public List<TaskMilestoneGroupDto> CompletedGroups { get; set; } = new();
}

/// <summary>A milestone with its associated tasks for display in an accordion group.</summary>
public class TaskMilestoneGroupDto
{
    public int       ProjectMilestoneId { get; set; }
    public string    MilestoneTitle     { get; set; } = "";
    public int       OrderIndex         { get; set; }
    /// <summary>"NotStarted" | "InProgress" | "Completed" | "Delayed".
    /// Progress indicator only — does NOT gate visibility. The client uses
    /// IsCurrentlyOpen for visibility decisions per the date-based rules.</summary>
    public string    MilestoneStatus    { get; set; } = "";
    public DateTime? OpenDate           { get; set; }
    /// <summary>Effective due date for the team (per-team override applied if present).</summary>
    public DateTime? DueDate            { get; set; }
    public DateTime? CloseDate          { get; set; }
    /// <summary>
    /// Server-derived. True when today is inside the milestone's effective
    /// visibility window:
    ///   (OpenDate IS NULL OR today &gt;= OpenDate)
    ///   AND (CloseDate IS NULL OR today &lt;= CloseDate).
    /// Independent of MilestoneStatus and unrelated to whether previous
    /// milestones have been completed.
    /// </summary>
    public bool      IsCurrentlyOpen    { get; set; }
    public int       DoneCount          { get; set; }
    public int       TotalCount         { get; set; }
    public List<TaskItemDto> Tasks      { get; set; } = new();
}

/// <summary>
/// A single task row, ready for display.
/// All display-logic inputs are pre-computed or embedded.
/// </summary>
public class TaskItemDto
{
    public int       Id              { get; set; }
    public string    Title           { get; set; } = "";
    /// <summary>"Open" | "InProgress" | "Done"</summary>
    public string    Status          { get; set; } = "";
    /// <summary>"Personal" | "System" | "Mentor"</summary>
    public string    TaskType        { get; set; } = "";
    /// <summary>True = high priority (IsMandatory in DB).</summary>
    public bool      IsMandatory     { get; set; }
    public DateTime? DueDate         { get; set; }
    /// <summary>ClosedAt from DB — when the task was marked done.</summary>
    public DateTime? CompletedAt     { get; set; }
    public string    AssignedToName  { get; set; } = "";
    /// <summary>True when this task requires a file submission.</summary>
    public bool      IsSubmission    { get; set; }
    /// <summary>
    /// Parent milestone status, embedded so the UI can determine whether
    /// this task is "pending" (NotStarted milestone) without extra lookups.
    /// </summary>
    public string    MilestoneStatus { get; set; } = "";

    /// <summary>
    /// Server-derived urgency flag.
    /// True when the parent milestone's effective due date (per-team override
    /// applied if present) has passed AND the task is mandatory AND still open
    /// (not Done / not Completed / not SubmittedToMentor / not closed / no
    /// submission yet). Computed in SQL so all student readers get consistent
    /// values and the project-health layer can reuse the same input.
    /// </summary>
    public bool      IsUrgent        { get; set; }

    // ── Latest submission state (populated only when IsSubmission = true) ────
    /// <summary>
    /// Reviewer (admin/staff) decision on the latest submission.
    /// "Submitted" | "Reviewed" | "NeedsRevision" — null if never submitted.
    /// </summary>
    public string?   LatestSubmissionStatus { get; set; }
    /// <summary>
    /// Mentor decision on the latest submission.
    /// "Pending" | "Approved" | "Returned" — null if never submitted.
    /// </summary>
    public string?   LatestMentorStatus     { get; set; }
    /// <summary>When the latest submission was created. Null if never submitted.</summary>
    public DateTime? LatestSubmittedAt      { get; set; }
}
