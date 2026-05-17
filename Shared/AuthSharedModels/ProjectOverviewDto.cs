using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Lecturer / Mentor project-detail mini dashboard
//
//  GET /api/projects/{projectId}/overview
//  Server enforces scope: Admin/Staff see any active project; Mentor sees
//  only projects they mentor; students are blocked at the endpoint.
//
//  Effective due dates use the standard chain
//  (TeamTaskDueDateOverrides → TeamMilestoneDueDateOverrides → global).
// ─────────────────────────────────────────────────────────────────────────────

public class ProjectOverviewDto
{
    public ProjectOverviewHeaderDto      Header              { get; set; } = new();
    public ProjectOverviewSummaryDto     Summary             { get; set; } = new();
    public List<ProjectOverviewMilestoneDto> Milestones      { get; set; } = new();
    public List<ProjectOverviewTaskDto>      Tasks           { get; set; } = new();
    public List<ProjectOverviewRequestDto>   OpenRequests    { get; set; } = new();
    public List<ProjectOverviewSubmissionDto> RecentSubmissions { get; set; } = new();
}

public class ProjectOverviewHeaderDto
{
    public int     ProjectId      { get; set; }
    public int     ProjectNumber  { get; set; }
    public string  ProjectTitle   { get; set; } = "";
    public string? TeamName       { get; set; }
    public string  ProjectType    { get; set; } = "";
    public string? MentorNames    { get; set; }
    public string? HealthStatus   { get; set; }
}

public class ProjectOverviewSummaryDto
{
    public int OverallProgressPercent { get; set; }
    public int MilestonesCompleted    { get; set; }
    public int MilestonesTotal        { get; set; }
    public int TasksCompleted         { get; set; }
    public int TasksTotal             { get; set; }
    public int MissingSubmissions     { get; set; }
    public int OpenRequestCount       { get; set; }
    public int OverdueTaskCount       { get; set; }
}

public class ProjectOverviewMilestoneDto
{
    public int       ProjectMilestoneId     { get; set; }
    public string    Title                  { get; set; } = "";
    public int       OrderIndex             { get; set; }
    /// <summary>"NotStarted" | "InProgress" | "Completed" | "Delayed"</summary>
    public string    Status                 { get; set; } = "";
    public DateTime? DueDate                { get; set; }
    public int       TasksCompleted         { get; set; }
    public int       TasksTotal             { get; set; }
    public int       MissingSubmissionCount { get; set; }
    public bool      IsOverdue              { get; set; }
    /// <summary>Tasks belonging to this milestone — pre-grouped server-side.</summary>
    public List<ProjectOverviewTaskDto> Tasks { get; set; } = new();
}

public class ProjectOverviewTaskDto
{
    public int       TaskId          { get; set; }
    public string    Title           { get; set; } = "";
    public string    MilestoneTitle  { get; set; } = "";
    /// <summary>"Open" | "InProgress" | "Done" | "SubmittedToMentor" | "Completed".</summary>
    public string    Status          { get; set; } = "";
    public bool      IsSubmission    { get; set; }
    public DateTime? DueDate         { get; set; }
    /// <summary>True ⇒ effective due date passed and the task is still open.</summary>
    public bool      IsOverdue       { get; set; }
    /// <summary>True for submission tasks that have any submission row.</summary>
    public bool      HasSubmission   { get; set; }
}

public class ProjectOverviewRequestDto
{
    public int      RequestId     { get; set; }
    public string   RequestType   { get; set; } = "";
    public string   Title         { get; set; } = "";
    public string   Status        { get; set; } = "";
    public DateTime CreatedAt     { get; set; }
    public string   CreatedByName { get; set; } = "";
}

public class ProjectOverviewSubmissionDto
{
    public int       SubmissionId         { get; set; }
    public int       TaskId               { get; set; }
    public string    TaskTitle            { get; set; } = "";
    public string    SubmittedByName      { get; set; } = "";
    public DateTime  SubmittedAt          { get; set; }
    /// <summary>"Submitted" | "Reviewed" | "NeedsRevision" + ReviewStatus when published.</summary>
    public string    Status               { get; set; } = "";
    public string?   LatestMentorStatus   { get; set; }
    /// <summary>Truncated reviewer feedback when published. Null otherwise.</summary>
    public string?   LatestFeedback       { get; set; }
}
