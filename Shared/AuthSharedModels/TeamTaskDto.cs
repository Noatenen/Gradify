using System;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ──────────────────────────────────────────────────────────────────────────────
//  TeamTask DTOs
//
//  Team Tasks are a student-created, team-visible work layer that is completely
//  separate from the official Tasks / TaskSubmissions workflow.
//
//  They do NOT:
//    • belong to a milestone
//    • create TaskSubmissions
//    • affect milestone progress percentages
//    • go through mentor review
//    • use complex status machines
//
//  They have only one completion concept: IsDone (bool).
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>Read model returned by all team-task endpoints.</summary>
public class TeamTaskDto
{
    public int       Id               { get; set; }
    public string    Title            { get; set; } = "";
    public string?   Description      { get; set; }
    public int?      AssignedToUserId { get; set; }
    /// <summary>Resolved display name: team-member full name, or "כל הצוות" when AssignedToUserId is null.</summary>
    public string    AssigneeName     { get; set; } = "כל הצוות";
    public DateTime? DueDate          { get; set; }
    public bool      IsDone           { get; set; }
    public int       CreatedByUserId  { get; set; }
    public DateTime  CreatedAt        { get; set; }
    public DateTime? UpdatedAt        { get; set; }
}

/// <summary>Request body for POST /api/projects/team-tasks.</summary>
public class CreateTeamTaskRequest
{
    public string    Title            { get; set; } = "";
    public string?   Description      { get; set; }
    /// <summary>Null means "entire team".</summary>
    public int?      AssignedToUserId { get; set; }
    public DateTime? DueDate          { get; set; }
}

/// <summary>Request body for PUT /api/projects/team-tasks/{id}.</summary>
public class UpdateTeamTaskRequest
{
    public string    Title            { get; set; } = "";
    public string?   Description      { get; set; }
    /// <summary>Null means "entire team".</summary>
    public int?      AssignedToUserId { get; set; }
    public DateTime? DueDate          { get; set; }
}