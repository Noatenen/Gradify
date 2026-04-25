using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

[Route("api/notifications")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly DbRepository _db;

    public NotificationsController(DbRepository db) => _db = db;

    // ── GET /api/notifications ────────────────────────────────────────────────
    // Returns up to 50 most recent notifications for the current user.
    [HttpGet]
    public async Task<IActionResult> GetRecent(int authUserId)
    {
        const string sql = @"
            SELECT  Id, UserId, Title, Message, Type,
                    RelatedEntityType, RelatedEntityId,
                    IsRead, CreatedAt, ReadAt
            FROM    Notifications
            WHERE   UserId = @UserId
            ORDER   BY CreatedAt DESC
            LIMIT   50";

        var rows = await _db.GetRecordsAsync<NotificationDto>(sql, new { UserId = authUserId });
        return Ok(rows ?? Enumerable.Empty<NotificationDto>());
    }

    // ── GET /api/notifications/unread-count ───────────────────────────────────
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(int authUserId)
    {
        var rows = await _db.GetRecordsAsync<int>(
            "SELECT COUNT(*) FROM Notifications WHERE UserId = @UserId AND IsRead = 0",
            new { UserId = authUserId });
        return Ok(rows?.FirstOrDefault() ?? 0);
    }

    // ── POST /api/notifications/{id}/mark-read ────────────────────────────────
    [HttpPost("{id:int}/mark-read")]
    public async Task<IActionResult> MarkRead(int id, int authUserId)
    {
        await _db.SaveDataAsync(@"
            UPDATE Notifications
            SET    IsRead = 1, ReadAt = datetime('now')
            WHERE  Id = @Id AND UserId = @UserId",
            new { Id = id, UserId = authUserId });
        return Ok();
    }

    // ── POST /api/notifications/mark-all-read ─────────────────────────────────
    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead(int authUserId)
    {
        await _db.SaveDataAsync(@"
            UPDATE Notifications
            SET    IsRead = 1, ReadAt = datetime('now')
            WHERE  UserId = @UserId AND IsRead = 0",
            new { UserId = authUserId });
        return Ok();
    }
}
