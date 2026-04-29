using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ── Overview (list of all mentor's projects) ──────────────────────────────

public class MentorProjectSummaryDto
{
    public int     Id            { get; set; }
    public int     ProjectNumber { get; set; }
    public string  Title         { get; set; } = "";
    public string  Status        { get; set; } = "";
    public string? HealthStatus  { get; set; }
    public string  ProjectType   { get; set; } = "";

    // Team
    public int    TeamId       { get; set; }
    public string TeamName     { get; set; } = "";
    public string StudentNames { get; set; } = "";
    public int    StudentCount { get; set; }

    // Current active milestone
    public string?   CurrentMilestoneTitle  { get; set; }
    public string?   CurrentMilestoneStatus { get; set; }
    public DateTime? CurrentMilestoneDueDate { get; set; }
    public int       MilestoneProgressPct   { get; set; }   // 0-100

    // Task counts
    public int TotalTasks          { get; set; }
    public int OpenTasks           { get; set; }
    public int InProgressTasks     { get; set; }
    public int CompletedTasks      { get; set; }
    public int OverdueTasks        { get; set; }
    public int PendingMentorReview { get; set; }
}

// ── Single project detail ─────────────────────────────────────────────────

public class MentorProjectDetailDto
{
    // Identity
    public int     Id            { get; set; }
    public int     ProjectNumber { get; set; }
    public string  Title         { get; set; } = "";
    public string  Status        { get; set; } = "";
    public string? HealthStatus  { get; set; }
    public string  ProjectType   { get; set; } = "";
    public string? Description   { get; set; }
    public string? Organization  { get; set; }

    // Team
    public string                      TeamName    { get; set; } = "";
    public List<MentorTeamMemberDto>   TeamMembers { get; set; } = new();

    // Progress aggregates
    public int MilestoneProgressPct { get; set; }
    public int TaskProgressPct      { get; set; }

    // Task counts
    public int TotalTasks          { get; set; }
    public int OpenTasks           { get; set; }
    public int InProgressTasks     { get; set; }
    public int CompletedTasks      { get; set; }
    public int OverdueTasks        { get; set; }
    public int PendingMentorReview { get; set; }

    // Milestones with nested tasks
    public List<MentorMilestoneDto>          Milestones          { get; set; } = new();
    public List<MentorPendingSubmissionDto>  PendingSubmissions  { get; set; } = new();
}

public class MentorTeamMemberDto
{
    public int    UserId   { get; set; }
    public string FullName { get; set; } = "";
    public string Email    { get; set; } = "";
    public string Phone    { get; set; } = "";
}

public class MentorMilestoneDto
{
    public int      ProjectMilestoneId { get; set; }
    public string   Title              { get; set; } = "";
    public int      OrderIndex         { get; set; }
    public string   Status             { get; set; } = "NotStarted";
    public DateTime? DueDate           { get; set; }
    public int      TotalTasks         { get; set; }
    public int      CompletedTasks     { get; set; }
    public int      ProgressPct        { get; set; }

    public List<MentorTaskDto> Tasks { get; set; } = new();
}

public class MentorTaskDto
{
    public int      Id                     { get; set; }
    public string   Title                  { get; set; } = "";
    public string   Status                 { get; set; } = "Open";
    public DateTime? DueDate               { get; set; }
    public bool     IsOverdue              { get; set; }
    public bool     IsSubmission           { get; set; }
    public string   AssignedToName         { get; set; } = "";
    public string?  LatestSubmissionStatus { get; set; }
    public string?  LatestMentorStatus     { get; set; }
}

public class MentorPendingSubmissionDto
{
    public int      SubmissionId   { get; set; }
    public int      TaskId         { get; set; }
    public string   TaskTitle      { get; set; } = "";
    public string   MilestoneTitle { get; set; } = "";
    public string   SubmittedBy    { get; set; } = "";
    public DateTime SubmittedAt    { get; set; }
    public string   MentorStatus   { get; set; } = "Pending";

    // Populated only by the global /api/mentor/submissions endpoint.
    public int     ProjectId     { get; set; }
    public string  ProjectTitle  { get; set; } = "";
    public int     ProjectNumber { get; set; }
}

// ── Mentor submission review context ─────────────────────────────────────────
// Returned by GET /api/mentor/submissions/{id}/context.
// Contains everything the mentor needs to review a single submission:
// task metadata, the target submission with files, and the full round history.

public class MentorSubmissionContextDto
{
    // Task context
    public int     TaskId          { get; set; }
    public string  TaskTitle       { get; set; } = "";
    public string? TaskDescription { get; set; }
    public string  MilestoneTitle  { get; set; } = "";

    // The specific submission being reviewed
    public int      SubmissionId   { get; set; }
    public string   SubmittedBy    { get; set; } = "";
    public DateTime SubmittedAt    { get; set; }
    public string?  Notes          { get; set; }
    public string   MentorStatus   { get; set; } = "Pending";
    public string?  MentorFeedback { get; set; }

    public List<TaskSubmissionFileDto> Files { get; set; } = new();

    // Full submission history for this task (all rounds, newest first)
    public List<MentorSubmissionRoundDto> History { get; set; } = new();
}

public class MentorSubmissionRoundDto
{
    public int       SubmissionId     { get; set; }
    public int       RoundNumber      { get; set; }
    public DateTime  SubmittedAt      { get; set; }
    public string?   Notes            { get; set; }
    /// <summary>Reviewer (lecturer/admin) decision.</summary>
    public string    Status           { get; set; } = "Submitted";
    public string?   ReviewerFeedback { get; set; }
    /// <summary>Mentor decision for this round.</summary>
    public string    MentorStatus     { get; set; } = "Pending";
    public string?   MentorFeedback   { get; set; }
    public DateTime? MentorReviewedAt { get; set; }
    public int       FileCount          { get; set; }
    /// <summary>When the student forwarded this round to course staff.</summary>
    public DateTime? CourseSubmittedAt  { get; set; }
    /// <summary>Files attached to this specific submission round.</summary>
    public List<TaskSubmissionFileDto> Files { get; set; } = new();
}
