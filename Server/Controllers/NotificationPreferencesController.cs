using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  NotificationPreferencesController — /api/users/me/notification-preferences
//
//  GET / PUT only. The user can only see and update their own preferences.
//  Missing rows are filled with NotificationTypes.DefaultsForType(...) so a
//  brand-new account has sensible defaults the moment they open the page.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/users/me/notification-preferences")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize]
public class NotificationPreferencesController : ControllerBase
{
    private readonly DbRepository _db;
    public NotificationPreferencesController(DbRepository db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetMine(int authUserId)
    {
        // Resolve the user's roles so we can scope the visible notification
        // types — a Mentor doesn't see student-only "AssignmentPublished",
        // a Lecturer doesn't see "TaskAssigned", etc.
        var roles = (await _db.GetRecordsAsync<string>(
            "SELECT Role FROM UserRoles WHERE UserId = @UserId",
            new { UserId = authUserId }))?.ToList() ?? new List<string>();

        var visibleTypes = NotificationTypes.ForRoles(roles);
        if (visibleTypes.Count == 0)
            return Ok(new NotificationPreferencesDto { Items = new() });

        const string sql = @"
            SELECT NotificationType, InAppEnabled, EmailEnabled
            FROM   UserNotificationPreferences
            WHERE  UserId = @UserId";
        var rows = (await _db.GetRecordsAsync<PreferenceRow>(sql, new { UserId = authUserId }))
                   ?.ToDictionary(r => r.NotificationType, r => r) ?? new();

        // Project only types the user can actually trigger / receive. Missing
        // rows fall back to NotificationTypes.DefaultsForType so a brand-new
        // account has sensible defaults from the very first render.
        var items = visibleTypes.Select(type =>
        {
            if (rows.TryGetValue(type, out var saved))
            {
                return new NotificationPreferenceItemDto
                {
                    NotificationType = type,
                    InAppEnabled     = saved.InAppEnabled == 1,
                    EmailEnabled     = saved.EmailEnabled == 1,
                    Group            = NotificationTypes.GroupOf(type),
                };
            }
            var (inApp, email) = NotificationTypes.DefaultsForType(type);
            return new NotificationPreferenceItemDto
            {
                NotificationType = type,
                InAppEnabled     = inApp,
                EmailEnabled     = email,
                Group            = NotificationTypes.GroupOf(type),
            };
        }).ToList();

        return Ok(new NotificationPreferencesDto { Items = items });
    }

    [HttpPut]
    public async Task<IActionResult> SaveMine(int authUserId, [FromBody] SaveNotificationPreferencesRequest req)
    {
        if (req?.Items is null) return BadRequest("גוף בקשה ריק");

        // Upsert each row — UNIQUE(UserId, NotificationType) makes ON CONFLICT
        // safe and keeps the operation idempotent. Skip unknown type names.
        foreach (var item in req.Items)
        {
            if (string.IsNullOrWhiteSpace(item.NotificationType)) continue;
            if (!NotificationTypes.All.Contains(item.NotificationType)) continue;

            await _db.SaveDataAsync(@"
                INSERT INTO UserNotificationPreferences
                    (UserId, NotificationType, InAppEnabled, EmailEnabled)
                VALUES
                    (@UserId, @Type, @InApp, @Email)
                ON CONFLICT(UserId, NotificationType) DO UPDATE SET
                    InAppEnabled = excluded.InAppEnabled,
                    EmailEnabled = excluded.EmailEnabled,
                    UpdatedAt    = datetime('now')",
                new
                {
                    UserId = authUserId,
                    Type   = item.NotificationType,
                    InApp  = item.InAppEnabled ? 1 : 0,
                    Email  = item.EmailEnabled ? 1 : 0,
                });
        }

        return Ok();
    }

    private sealed class PreferenceRow
    {
        public string NotificationType { get; set; } = "";
        public int    InAppEnabled     { get; set; }
        public int    EmailEnabled     { get; set; }
    }
}
