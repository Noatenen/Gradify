using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  PermissionsController — /api/permissions
//
//  Admin-only management of Role → Permission assignments, plus a per-user
//  read endpoint (/current-user) every signed-in client may call to discover
//  what they can do.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/permissions")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
public class PermissionsController : ControllerBase
{
    private static readonly (string Role, string Display, string Description)[] KnownRoles = new[]
    {
        ("Admin",   "מנהל",     "גישה מלאה לכל אזורי הניהול והגדרות המערכת"),
        ("Staff",   "מרצה",     "ניהול שיבוצים, פרויקטים וטפסים — ללא הרשאות מערכת רגישות"),
        ("Mentor",  "מנטור",    "צפייה בפרויקטים שהוקצו ובהגשות הצוות"),
        ("Student", "סטודנט",   "גישה לקטלוג הפרויקטים והגשת טופס שיבוץ"),
    };

    private readonly DbRepository _db;

    public PermissionsController(DbRepository db) => _db = db;

    // ── GET /api/permissions ────────────────────────────────────────────────
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetCatalog(int authUserId)
    {
        var rows = await PermissionsRepository.GetCatalogAsync(_db);
        return Ok(rows);
    }

    // ── GET /api/permissions/roles ──────────────────────────────────────────
    [HttpGet("roles")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetRoles(int authUserId)
    {
        var counts = (await _db.GetRecordsAsync<RoleCountRow>(@"
            SELECT  Role AS RoleName, COUNT(DISTINCT UserId) AS UserCount
            FROM    UserRoles
            GROUP   BY Role"))?.ToList() ?? new();

        var permCounts = (await _db.GetRecordsAsync<RolePermCountRow>(@"
            SELECT  RoleName, COUNT(1) AS PermissionCount
            FROM    RolePermissions
            GROUP   BY RoleName"))?.ToList() ?? new();

        var byRoleUsers = counts.ToDictionary(r => r.RoleName, r => r.UserCount, StringComparer.OrdinalIgnoreCase);
        var byRolePerms = permCounts.ToDictionary(r => r.RoleName, r => r.PermissionCount, StringComparer.OrdinalIgnoreCase);

        var result = KnownRoles.Select(r => new RoleSummaryDto
        {
            RoleName        = r.Role,
            DisplayName     = r.Display,
            Description     = r.Description,
            UserCount       = byRoleUsers.TryGetValue(r.Role, out var uc) ? uc : 0,
            PermissionCount = byRolePerms.TryGetValue(r.Role, out var pc) ? pc : 0,
        }).ToList();

        return Ok(result);
    }

    // ── GET /api/permissions/roles/{roleName} ───────────────────────────────
    [HttpGet("roles/{roleName}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetRoleDetail(string roleName, int authUserId)
    {
        var meta = KnownRoles.FirstOrDefault(r => string.Equals(r.Role, roleName, StringComparison.OrdinalIgnoreCase));
        if (meta.Role is null) return NotFound("התפקיד לא נמצא");

        var assigned = await PermissionsRepository.GetRolePermissionsAsync(_db, meta.Role);
        var catalog  = await PermissionsRepository.GetCatalogAsync(_db);

        return Ok(new RolePermissionsDto
        {
            RoleName    = meta.Role,
            DisplayName = meta.Display,
            Description = meta.Description,
            Permissions = assigned,
            Catalog     = catalog
        });
    }

    // ── PUT /api/permissions/roles/{roleName} ───────────────────────────────
    [HttpPut("roles/{roleName}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> UpdateRolePermissions(
        string roleName, int authUserId, [FromBody] SaveRolePermissionsRequest req)
    {
        if (req is null) return BadRequest("נתונים חסרים");

        var meta = KnownRoles.FirstOrDefault(r => string.Equals(r.Role, roleName, StringComparison.OrdinalIgnoreCase));
        if (meta.Role is null) return NotFound("התפקיד לא נמצא");

        // Reject keys that don't exist in the catalog — keeps the table clean.
        var catalog = await PermissionsRepository.GetCatalogAsync(_db);
        var validKeys = catalog.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clean     = req.Permissions
            .Where(k => !string.IsNullOrWhiteSpace(k) && validKeys.Contains(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Safety: never let the Admin role lose Permissions.Manage — that would
        // lock out the only path back into this page.
        if (string.Equals(meta.Role, "Admin", StringComparison.OrdinalIgnoreCase) &&
            !clean.Contains(Permissions.Permissions_Manage, StringComparer.OrdinalIgnoreCase))
        {
            clean.Add(Permissions.Permissions_Manage);
        }

        await PermissionsRepository.ReplaceRolePermissionsAsync(_db, meta.Role, clean);
        return Ok();
    }

    // ── GET /api/permissions/current-user ───────────────────────────────────
    // Open to any signed-in user — clients use it to drive UI visibility.
    [HttpGet("current-user")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser(int authUserId)
    {
        var roles       = await PermissionsRepository.GetUserRolesAsync(_db, authUserId);
        var permissions = await PermissionsRepository.GetUserPermissionsAsync(_db, authUserId);

        return Ok(new CurrentUserPermissionsDto
        {
            UserId      = authUserId,
            Roles       = roles,
            Permissions = permissions.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
        });
    }

    // ── DB row types ─────────────────────────────────────────────────────────
    private sealed class RoleCountRow
    {
        public string RoleName  { get; set; } = "";
        public int    UserCount { get; set; }
    }
    private sealed class RolePermCountRow
    {
        public string RoleName        { get; set; } = "";
        public int    PermissionCount { get; set; }
    }
}
