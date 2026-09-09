using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  UsersMeController — /api/users/me
//
//  Role-independent profile surface used by the shared /settings page so every
//  authenticated user (Student / Mentor / Lecturer / Admin) can read and edit
//  their own personal details. Student-specific fields (team / project /
//  Slack / theme) stay on /api/student/me which remains gated to Students.
//
//  The controller never trusts a body-supplied userId — every read and write
//  is scoped to authUserId injected by the AuthCheck filter.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/users/me")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize]
public class UsersMeController : ControllerBase
{
    private readonly DbRepository _db;
    public UsersMeController(DbRepository db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetMe(int authUserId)
    {
        const string userSql = @"
            SELECT  Id, FirstName, LastName, Email, Phone, ProfileImagePath
            FROM    users
            WHERE   Id = @UserId
            LIMIT   1";

        var row = (await _db.GetRecordsAsync<UserRow>(userSql, new { UserId = authUserId }))?.FirstOrDefault();
        if (row is null) return NotFound();

        var roles = (await _db.GetRecordsAsync<string>(
            "SELECT Role FROM UserRoles WHERE UserId = @UserId",
            new { UserId = authUserId }))?.ToList() ?? new List<string>();

        var dto = new UserProfileDto
        {
            Id              = row.Id,
            FirstName       = row.FirstName ?? "",
            LastName        = row.LastName  ?? "",
            Email           = row.Email     ?? "",
            Phone           = row.Phone     ?? "",
            // BASE-RELATIVE, no leading slash. The client renders this straight
            // into <img src>, and a rooted path resolves against the ORIGIN
            // rather than <base href> — so under a sub-path deployment every
            // avatar would 404. Relative resolves against the document base,
            // which is correct both at "/" and under any prefix.
            ProfileImageUrl = string.IsNullOrEmpty(row.ProfileImagePath)
                                ? null
                                : $"profile-images/{row.ProfileImagePath}",
            Roles           = roles,
        };

        return Ok(dto);
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(int authUserId, [FromBody] UpdateUserProfileRequest req)
    {
        if (req is null) return BadRequest();

        var firstName = (req.FirstName ?? "").Trim();
        var lastName  = (req.LastName  ?? "").Trim();
        var phone     = (req.Phone     ?? "").Trim();

        if (firstName.Length == 0 || firstName.Length > 50) return BadRequest("שם פרטי לא תקין");
        if (lastName.Length  == 0 || lastName.Length  > 50) return BadRequest("שם משפחה לא תקין");
        if (phone.Length > 20) return BadRequest("מספר טלפון ארוך מדי");

        await _db.SaveDataAsync(@"
            UPDATE users
            SET    FirstName = @FirstName,
                   LastName  = @LastName,
                   Phone     = @Phone
            WHERE  Id = @UserId",
            new
            {
                FirstName = firstName,
                LastName  = lastName,
                Phone     = phone,
                UserId    = authUserId,
            });

        return NoContent();
    }

    private sealed class UserRow
    {
        public int     Id               { get; set; }
        public string? FirstName        { get; set; }
        public string? LastName         { get; set; }
        public string? Email            { get; set; }
        public string? Phone            { get; set; }
        public string? ProfileImagePath { get; set; }
    }
}
