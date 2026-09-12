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

    /// <summary>The team's uploaded project logo, base-relative
    /// ("project-logos/{file}"), or null when the team has never uploaded one.
    /// Same source and same URL shape the student's own project page reads
    /// (ProjectTeamProfile.LogoPath, served out of wwwroot/project-logos) — the
    /// lecturer review shows the existing mark, it does not upload or edit it.
    /// Null both when no ProjectTeamProfile row exists and when its LogoPath is
    /// blank, so the header can fall back to no image without a broken tile.</summary>
    public string? LogoUrl        { get; set; }

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

/// <summary>
/// PATCH api/projects/{projectId}/tasks/{taskId}/submission-settings.
///
/// <para>DELIBERATELY NARROW. It carries only the authoring fields a
/// Lecturer/Admin edits and nothing else, so the endpoint cannot become a
/// general Task writer: status, closure, dates, milestone, project and every
/// submission/review state are absent from this shape and therefore cannot be
/// touched, accidentally or otherwise.</para>
///
/// <para>DueDate is deliberately NOT here. Tasks.DueDate is the global date and
/// per-team changes run through TeamTaskDueDateOverrides / 
/// TeamMilestoneDueDateOverrides — "Globals (Tasks.DueDate) are never mutated",
/// as ProjectsController's own task query puts it. Editing it from this form
/// would bypass that mechanism, which is an unrelated business-rule change.</para>
/// </summary>
public class UpdateProjectTaskSubmissionSettingsRequest
{
    /// <summary>Task description. Blank is stored as NULL.</summary>
    public string? Description            { get; set; }

    /// <summary>What the student must submit. Blank is stored as NULL, so an
    /// emptied field correctly hides the "הנחיות הגשה" section rather than
    /// rendering an empty heading.</summary>
    public string? SubmissionInstructions { get; set; }

    /// <summary>Upload policy. Ignored by the server for a non-submission task,
    /// which is what keeps a stray value from being written to a row that has
    /// no submission flow.</summary>
    public int?    MaxFilesCount          { get; set; }
    public int?    MaxFileSizeMb          { get; set; }
    public string? AllowedFileTypes       { get; set; }
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

    // ── Authoring fields ────────────────────────────────────────────────────
    // Added so the Lecturer/Admin task editor can LOAD the values it edits from
    // the payload the project workspace already fetches, instead of a second
    // round-trip per task. All read straight off the Tasks row — the same row
    // the student's TaskDetailDto reads — so what the editor shows is what the
    // student sees, by construction rather than by synchronisation.

    /// <summary>Tasks.Description — rendered to the student as "תיאור המשימה".</summary>
    public string?   Description     { get; set; }

    /// <summary>Tasks.SubmissionInstructions — rendered to the student as
    /// "הנחיות הגשה" on the task card and in the submission form, and shown to
    /// the mentor read-only while reviewing. THE source of truth for an
    /// instantiated project task; TaskTemplates only supplies the default at
    /// creation time and never overwrites this afterwards.</summary>
    public string?   SubmissionInstructions { get; set; }

    /// <summary>Upload policy, submission tasks only. Null on a non-submission
    /// task and on rows created before the policy columns existed.</summary>
    public int?      MaxFilesCount   { get; set; }
    public int?      MaxFileSizeMb   { get; set; }
    public string?   AllowedFileTypes { get; set; }
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
