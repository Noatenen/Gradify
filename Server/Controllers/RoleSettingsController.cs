using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  RoleSettingsController — /api/role-settings
//
//  Reads and writes the simple feature-flag matrix backing the
//  "ניהול → הרשאות לפי תפקיד" page. Decoupled from the larger
//  Permissions / RolePermissions key-based system at PermissionsController;
//  the two coexist.
//
//  Authorization model:
//    - GET /api/role-settings           → Admin (full matrix)
//    - GET /api/role-settings/{role}    → Admin (single row)
//    - PUT /api/role-settings/{role}    → Admin (save row)
//    - GET /api/role-settings/me        → any signed-in user (their effective
//                                          UNION-of-roles flag set)
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/role-settings")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
public class RoleSettingsController : ControllerBase
{
    // Hebrew labels for the table's role column. Mirrors the convention used
    // by PermissionsController.KnownRoles. Order = display order in the UI.
    private static readonly (string RoleName, string Display, string Description)[] KnownRoles = new[]
    {
        ("Admin",   "מנהל",   "גישה מלאה לכל אזורי הניהול והגדרות המערכת"),
        ("Staff",   "מרצה",   "ניהול שיבוצים, פרויקטים וטפסים — ללא הרשאות מערכת רגישות"),
        ("Mentor",  "מנחה",   "צפייה בפרויקטים שהוקצו ובהגשות הצוות"),
        ("Student", "סטודנט", "גישה למשימות, הגשות ופתיחת בקשות"),
        ("User",    "משתמש",  "משתמש בסיסי — ללא הרשאות מיוחדות"),
    };

    private readonly DbRepository _db;
    public RoleSettingsController(DbRepository db) => _db = db;

    // ── GET /api/role-settings ──────────────────────────────────────────────
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetAll(int authUserId)
    {
        const string sql = @"SELECT * FROM RoleSettings";
        var rows = (await _db.GetRecordsAsync<RoleSettingsRow>(sql))?.ToList() ?? new();

        // Return one row per known role even if the seed didn't cover it.
        var result = KnownRoles.Select(kr =>
        {
            var found = rows.FirstOrDefault(r => r.RoleName == kr.RoleName);
            return MapToDto(kr.RoleName, found);
        }).ToList();

        return Ok(result);
    }

    // ── GET /api/role-settings/{role} ───────────────────────────────────────
    [HttpGet("{roleName}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetForRole(string roleName, int authUserId)
    {
        if (!KnownRoles.Any(kr => kr.RoleName == roleName))
            return NotFound("תפקיד לא ידוע");

        const string sql = @"SELECT * FROM RoleSettings WHERE RoleName = @RoleName LIMIT 1";
        var row = (await _db.GetRecordsAsync<RoleSettingsRow>(sql, new { RoleName = roleName }))
                  ?.FirstOrDefault();

        return Ok(MapToDto(roleName, row));
    }

    // ── PUT /api/role-settings/{role} ───────────────────────────────────────
    [HttpPut("{roleName}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Save(string roleName, [FromBody] SaveRoleSettingsRequest req, int authUserId)
    {
        if (req is null) return BadRequest("גוף בקשה ריק");
        if (!KnownRoles.Any(kr => kr.RoleName == roleName))
            return BadRequest("תפקיד לא ידוע");

        const string sql = @"
            INSERT INTO RoleSettings
                (RoleName, CanManageRequests, CanManageMilestones, CanManageAssignments,
                 CanManageUsers, CanManageAirtable, CanOpenRequests, CanViewTasks,
                 CanSubmitTasks, CanViewLecturerDashboard, UpdatedAt)
            VALUES
                (@RoleName, @CanManageRequests, @CanManageMilestones, @CanManageAssignments,
                 @CanManageUsers, @CanManageAirtable, @CanOpenRequests, @CanViewTasks,
                 @CanSubmitTasks, @CanViewLecturerDashboard, datetime('now'))
            ON CONFLICT(RoleName) DO UPDATE SET
                CanManageRequests        = excluded.CanManageRequests,
                CanManageMilestones      = excluded.CanManageMilestones,
                CanManageAssignments     = excluded.CanManageAssignments,
                CanManageUsers           = excluded.CanManageUsers,
                CanManageAirtable        = excluded.CanManageAirtable,
                CanOpenRequests          = excluded.CanOpenRequests,
                CanViewTasks             = excluded.CanViewTasks,
                CanSubmitTasks           = excluded.CanSubmitTasks,
                CanViewLecturerDashboard = excluded.CanViewLecturerDashboard,
                UpdatedAt                = datetime('now')";

        await _db.SaveDataAsync(sql, new
        {
            RoleName = roleName,
            CanManageRequests        = req.CanManageRequests        ? 1 : 0,
            CanManageMilestones      = req.CanManageMilestones      ? 1 : 0,
            CanManageAssignments     = req.CanManageAssignments     ? 1 : 0,
            CanManageUsers           = req.CanManageUsers           ? 1 : 0,
            CanManageAirtable        = req.CanManageAirtable        ? 1 : 0,
            CanOpenRequests          = req.CanOpenRequests          ? 1 : 0,
            CanViewTasks             = req.CanViewTasks             ? 1 : 0,
            CanSubmitTasks           = req.CanSubmitTasks           ? 1 : 0,
            CanViewLecturerDashboard = req.CanViewLecturerDashboard ? 1 : 0,
        });

        return Ok();
    }

    // ── GET /api/role-settings/me ──────────────────────────────────────────
    //
    // Returns the effective feature set for the calling user — UNION across
    // every role they belong to. Pages and components consume this through
    // the client-side PermissionService.
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMine(int authUserId)
    {
        const string rolesSql = @"SELECT Role FROM UserRoles WHERE UserId = @UserId";
        var roles = (await _db.GetRecordsAsync<string>(rolesSql, new { UserId = authUserId }))
                    ?.Distinct().ToList() ?? new();

        if (roles.Count == 0)
            return Ok(new CurrentUserFeaturesDto { UserId = authUserId });

        // Bool-OR across rows. SQLite's MAX() works on integers and yields
        // the same result as OR for {0,1} columns.
        string inClause = string.Join(",", roles.Select((_, i) => $"@r{i}"));
        var roleParams = new Dictionary<string, object>();
        for (int i = 0; i < roles.Count; i++) roleParams[$"@r{i}"] = roles[i];
        // Build a Dapper-friendly anonymous-like dynamic param via DynamicParameters:
        var dyn = new global::Dapper.DynamicParameters();
        for (int i = 0; i < roles.Count; i++) dyn.Add($"r{i}", roles[i]);

        string aggSql = $@"
            SELECT
                MAX(CanManageRequests)        AS CanManageRequests,
                MAX(CanManageMilestones)      AS CanManageMilestones,
                MAX(CanManageAssignments)     AS CanManageAssignments,
                MAX(CanManageUsers)           AS CanManageUsers,
                MAX(CanManageAirtable)        AS CanManageAirtable,
                MAX(CanOpenRequests)          AS CanOpenRequests,
                MAX(CanViewTasks)             AS CanViewTasks,
                MAX(CanSubmitTasks)           AS CanSubmitTasks,
                MAX(CanViewLecturerDashboard) AS CanViewLecturerDashboard
            FROM   RoleSettings
            WHERE  RoleName IN ({inClause})";

        var agg = (await _db.GetRecordsAsync<RoleSettingsRow>(aggSql, dyn))?.FirstOrDefault();

        var features = new List<string>();
        if (agg is not null)
        {
            if (agg.CanManageRequests        == 1) features.Add(RoleFeatures.CanManageRequests);
            if (agg.CanManageMilestones      == 1) features.Add(RoleFeatures.CanManageMilestones);
            if (agg.CanManageAssignments     == 1) features.Add(RoleFeatures.CanManageAssignments);
            if (agg.CanManageUsers           == 1) features.Add(RoleFeatures.CanManageUsers);
            if (agg.CanManageAirtable        == 1) features.Add(RoleFeatures.CanManageAirtable);
            if (agg.CanOpenRequests          == 1) features.Add(RoleFeatures.CanOpenRequests);
            if (agg.CanViewTasks             == 1) features.Add(RoleFeatures.CanViewTasks);
            if (agg.CanSubmitTasks           == 1) features.Add(RoleFeatures.CanSubmitTasks);
            if (agg.CanViewLecturerDashboard == 1) features.Add(RoleFeatures.CanViewLecturerDashboard);
        }

        return Ok(new CurrentUserFeaturesDto
        {
            UserId   = authUserId,
            Roles    = roles,
            Features = features,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    private static RoleSettingsDto MapToDto(string roleName, RoleSettingsRow? row) => new()
    {
        RoleName                 = roleName,
        CanManageRequests        = (row?.CanManageRequests        ?? 0) == 1,
        CanManageMilestones      = (row?.CanManageMilestones      ?? 0) == 1,
        CanManageAssignments     = (row?.CanManageAssignments     ?? 0) == 1,
        CanManageUsers           = (row?.CanManageUsers           ?? 0) == 1,
        CanManageAirtable        = (row?.CanManageAirtable        ?? 0) == 1,
        CanOpenRequests          = (row?.CanOpenRequests          ?? 0) == 1,
        CanViewTasks             = (row?.CanViewTasks             ?? 0) == 1,
        CanSubmitTasks           = (row?.CanSubmitTasks           ?? 0) == 1,
        CanViewLecturerDashboard = (row?.CanViewLecturerDashboard ?? 0) == 1,
        UpdatedAt                = row?.UpdatedAt,
    };

    private sealed class RoleSettingsRow
    {
        public string    RoleName                 { get; set; } = "";
        public int       CanManageRequests        { get; set; }
        public int       CanManageMilestones      { get; set; }
        public int       CanManageAssignments     { get; set; }
        public int       CanManageUsers           { get; set; }
        public int       CanManageAirtable        { get; set; }
        public int       CanOpenRequests          { get; set; }
        public int       CanViewTasks             { get; set; }
        public int       CanSubmitTasks           { get; set; }
        public int       CanViewLecturerDashboard { get; set; }
        public DateTime? UpdatedAt                { get; set; }
    }
}
