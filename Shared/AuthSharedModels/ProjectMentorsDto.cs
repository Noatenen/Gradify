using System;
using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Project mentors — admin/lecturer surface for adding additional mentors to
//  an already-assigned project, including Admin / Staff users (not only those
//  with the Mentor role).
//
//  Backed by the existing ProjectMentors table (PK UNIQUE(ProjectId, UserId)).
//  Idempotent — re-adding the same user is a no-op.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One row of the project's current mentor list.</summary>
public class ProjectMentorDto
{
    public int       UserId      { get; set; }
    public string    FullName    { get; set; } = "";
    public string    Email       { get; set; } = "";
    public string    Phone       { get; set; } = "";
    /// <summary>Roles the user holds, separated by ", " (e.g. "Mentor, Staff").
    /// Lets the UI clarify when an Admin/Staff user is also acting as mentor.</summary>
    public string    Roles       { get; set; } = "";
    public DateTime? AssignedAt  { get; set; }
}

/// <summary>One row in the "candidate mentors" search picker. Excludes users
/// already assigned to the project.</summary>
public class MentorCandidateDto
{
    public int    UserId   { get; set; }
    public string FullName { get; set; } = "";
    public string Email    { get; set; } = "";
    /// <summary>Comma-separated role list — e.g. "Mentor", "Staff", "Admin, Mentor".</summary>
    public string Roles    { get; set; } = "";
}

/// <summary>Payload for POST /api/projects/{projectId}/mentors.</summary>
public class AddProjectMentorRequest
{
    public int UserId { get; set; }
}