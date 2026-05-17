using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Role-independent profile surface for the shared /settings page.
//
//  Every authenticated user (Student / Mentor / Lecturer / Admin) reads this
//  via GET /api/users/me and writes editable fields via
//  PUT /api/users/me/profile. Student-specific data (team / project / Slack)
//  remains on the existing /api/student/me endpoint and is rendered only for
//  the Student role.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Slim profile shape that works for any role.</summary>
public class UserProfileDto
{
    public int      Id              { get; set; }
    public string   FirstName       { get; set; } = "";
    public string   LastName        { get; set; } = "";
    public string   Email           { get; set; } = "";
    public string   Phone           { get; set; } = "";
    public string?  ProfileImageUrl { get; set; }
    /// <summary>Role names the user holds. Always at least one entry.</summary>
    public List<string> Roles       { get; set; } = new();
}

/// <summary>Payload for PUT /api/users/me/profile.</summary>
public class UpdateUserProfileRequest
{
    public string FirstName { get; set; } = "";
    public string LastName  { get; set; } = "";
    public string Phone     { get; set; } = "";
}
