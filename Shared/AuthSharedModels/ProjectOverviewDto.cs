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

    /// <summary>The team, by name. Added so the shared Project Workspace can
    /// draw the same identity block for a lecturer that it draws for a mentor —
    /// the mentor payload has carried this since it existed, and the lecturer's
    /// had no member list at all. <see cref="MentorTeamMemberDto"/> is reused
    /// rather than copied: it is the same four columns from the same two
    /// tables, and a second record with the same shape is how two of them start
    /// to disagree.</summary>
    public List<MentorTeamMemberDto> TeamMembers { get; set; } = new();

    /// <summary>
    /// Is the CALLER in ProjectMentors for this project?
    ///
    /// <para>Not "holds the Mentor role" — that is true of a dual-role account
    /// on every project in the system. This is their actual relationship to
    /// THIS one, and it is what lets the lecturer workspace surface a request
    /// still awaiting a mentor recommendation: when the reader is that mentor,
    /// the request is waiting on them and belongs in their attention list.</para>
    ///
    /// <para>Presentation only. Whether they may actually act is answered
    /// per-request by ProjectRequestDetailDto's Viewer* flags, and enforced
    /// again by the endpoint.</para>
    /// </summary>
    public bool ViewerIsProjectMentor { get; set; }

    /// <summary>
    /// The team's own working links — משאבי הפרויקט.
    ///
    /// <para>These already exist: the team maintains them on their own
    /// /project workspace (ProjectResources), and they are where the actual
    /// work lives — the Drive folder, the spec doc, the design file, the
    /// repository. Until now only the team could read them
    /// (GET api/projects/my-resources resolves the project from the CALLER's
    /// team membership), so a lecturer or mentor supervising the project had
    /// no route to the materials they were supervising.</para>
    ///
    /// <para>Carried on this payload rather than behind a new endpoint: it is
    /// four short rows, this call is already made, and it is already scoped to
    /// exactly the right readers.</para>
    /// </summary>
    public List<ProjectResourceDto> Resources { get; set; } = new();
}

public class ProjectOverviewSummaryDto
{
    /// <summary>TASK completion — how much of the work is ticked off.
    /// Secondary information: it belongs beside the task list, not at the top
    /// of the page. <see cref="MilestoneProgressPercent"/> is the project's
    /// headline progress.</summary>
    public int OverallProgressPercent { get; set; }

    /// <summary>MILESTONE completion — the project's primary progress figure,
    /// and the same one MentorProjectDetailDto.MilestoneProgressPct carries, by
    /// the same formula (completed milestones / total). Both workspaces lead
    /// with this so one project cannot be "62%" on one screen and "40%" on the
    /// other.</summary>
    public int MilestoneProgressPercent { get; set; }

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

    // ── What THIS caller may do with this request ───────────────────────────
    //
    // Answered by ExtensionWorkflow.Resolve, the same function the detail
    // endpoint and the /extension/decision gate use. The workspace words a
    // row's status from these, so it cannot promise a decision the endpoint
    // would refuse.

    /// <summary>The caller may post /mentor-recommendation on this request.</summary>
    public bool ViewerCanRecommend { get; set; }

    /// <summary>The caller may post /extension/decision on this request.</summary>
    public bool ViewerCanDecide { get; set; }

    /// <summary>The mentor stage has completed — see
    /// ExtensionWorkflow.MentorStageComplete.</summary>
    public bool MentorStageComplete { get; set; }
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
