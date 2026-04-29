using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Data;

// ─────────────────────────────────────────────────────────────────────────────
//  PermissionsRepository — read/write helpers around Permissions/RolePermissions.
//
//  Permissions complement (do NOT replace) the existing role-based gates:
//  controllers still use [Authorize(Roles = ...)] as the primary boundary;
//  permission checks refine UI/feature access on top.
// ─────────────────────────────────────────────────────────────────────────────

public static class PermissionsRepository
{
    /// <summary>Returns the flattened permission keys for a given user (unioned across all their roles).</summary>
    public static async Task<HashSet<string>> GetUserPermissionsAsync(DbRepository db, int userId)
    {
        const string sql = @"
            SELECT DISTINCT rp.PermissionKey
            FROM   UserRoles  ur
            JOIN   RolePermissions rp ON rp.RoleName = ur.Role
            WHERE  ur.UserId = @UserId";

        var rows = await db.GetRecordsAsync<string>(sql, new { UserId = userId });
        return rows is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(rows, StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<bool> UserHasPermissionAsync(DbRepository db, int userId, string permissionKey)
    {
        const string sql = @"
            SELECT 1
            FROM   UserRoles ur
            JOIN   RolePermissions rp
                   ON rp.RoleName = ur.Role
            WHERE  ur.UserId = @UserId AND rp.PermissionKey = @Key
            LIMIT  1";

        var rows = await db.GetRecordsAsync<int>(sql, new { UserId = userId, Key = permissionKey });
        return rows is not null && rows.Any();
    }

    public static async Task<List<string>> GetUserRolesAsync(DbRepository db, int userId)
    {
        const string sql = "SELECT Role FROM UserRoles WHERE UserId = @UserId ORDER BY Role";
        var rows = await db.GetRecordsAsync<string>(sql, new { UserId = userId });
        return rows?.ToList() ?? new List<string>();
    }

    /// <summary>Returns the catalog of all permissions, ordered by group + sort.</summary>
    public static async Task<List<PermissionDto>> GetCatalogAsync(DbRepository db)
    {
        const string sql = @"
            SELECT  Id, Key, DisplayName, GroupName, Description, SortOrder
            FROM    Permissions
            ORDER   BY GroupName, SortOrder, Id";
        var rows = await db.GetRecordsAsync<PermissionDto>(sql);
        return rows?.ToList() ?? new List<PermissionDto>();
    }

    /// <summary>Returns assigned permission keys for a single role.</summary>
    public static async Task<List<string>> GetRolePermissionsAsync(DbRepository db, string roleName)
    {
        const string sql = "SELECT PermissionKey FROM RolePermissions WHERE RoleName = @Role";
        var rows = await db.GetRecordsAsync<string>(sql, new { Role = roleName });
        return rows?.ToList() ?? new List<string>();
    }

    /// <summary>Bulk-replaces a role's permissions.</summary>
    public static async Task ReplaceRolePermissionsAsync(DbRepository db, string roleName, IEnumerable<string> keys)
    {
        await db.SaveDataAsync(
            "DELETE FROM RolePermissions WHERE RoleName = @Role",
            new { Role = roleName });

        foreach (var key in keys.Distinct())
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            await db.SaveDataAsync(@"
                INSERT OR IGNORE INTO RolePermissions (RoleName, PermissionKey)
                VALUES (@Role, @Key)",
                new { Role = roleName, Key = key });
        }
    }
}
