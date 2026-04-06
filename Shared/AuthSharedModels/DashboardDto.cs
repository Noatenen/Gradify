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
    public ProjectInfoDto?          Project      { get; set; }
    public List<TeamMemberDto>      TeamMembers  { get; set; } = new();
    public List<ContactDto>         Mentors      { get; set; } = new();
    public List<MilestoneSummaryDto> Milestones  { get; set; } = new();
    public UpcomingDeadlineDto?     NextDeadline { get; set; }
    public List<OpenRequestDto>     OpenRequests { get; set; } = new();
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
    public DateTime? DueDate          { get; set; }
    public DateTime? CompletedAt      { get; set; }
    /// <summary>Tasks belonging to this milestone, ordered by DueDate.</summary>
    public List<TaskSummaryDto> Tasks { get; set; } = new();
}

/// <summary>Task row. "Overdue" state is computed client-side: DueDate &lt; today &amp;&amp; Status != "Done".</summary>
public class TaskSummaryDto
{
    public int     Id             { get; set; }
    public string  Title          { get; set; } = "";
    /// <summary>"Open" | "InProgress" | "Done"</summary>
    public string  Status         { get; set; } = "Open";
    public DateTime? DueDate      { get; set; }
    public string  AssignedToName { get; set; } = "";
}

// ── Upcoming deadline ─────────────────────────────────────────────────────────

/// <summary>Derived server-side: the nearest incomplete milestone's deadline.</summary>
public class UpcomingDeadlineDto
{
    public string   Title   { get; set; } = "";
    public DateTime DueDate { get; set; }
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
