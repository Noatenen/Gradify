using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>One student's registration details inside the team creation form.</summary>
public class StudentRegistrationForm
{
    [Required] public string FirstName { get; set; } = "";
    [Required] public string LastName  { get; set; } = "";
    [Required] public string IdNumber  { get; set; } = "";
    [Required] public string Email     { get; set; } = "";
    public string? Phone { get; set; }

    /// <summary>Base64-encoded image bytes (no data-URL prefix).</summary>
    public string? ProfileImageBase64     { get; set; }
    /// <summary>File extension without dot, e.g. "jpg", "png".</summary>
    public string? ProfileImageExtension  { get; set; }
}

/// <summary>Full request payload for POST /api/team-registration.</summary>
public class CreateTeamRequest
{
    [Required] public StudentRegistrationForm Student1 { get; set; } = new();
    [Required] public StudentRegistrationForm Student2 { get; set; } = new();
}

/// <summary>Response from POST /api/team-registration.</summary>
public class TeamRegistrationResultDto
{
    public bool    Success { get; set; }
    public int     TeamId  { get; set; }
    /// <summary>Set when the entire request failed.</summary>
    public string? Error   { get; set; }
    /// <summary>Non-fatal notes (e.g. email send failure).</summary>
    public string? Warning { get; set; }
}
