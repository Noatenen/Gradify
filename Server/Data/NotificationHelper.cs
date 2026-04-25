namespace AuthWithAdmin.Server.Data;

/// <summary>
/// Static helpers for inserting notifications into the Notifications table.
/// Intentionally dependency-free — accepts DbRepository directly so any
/// controller can call it without additional DI registration.
/// </summary>
public static class NotificationHelper
{
    private const string InsertSql = @"
        INSERT INTO Notifications (UserId, Title, Message, Type, RelatedEntityType, RelatedEntityId)
        VALUES (@UserId, @Title, @Message, @Type, @RelatedEntityType, @RelatedEntityId)";

    public static async Task CreateAsync(
        DbRepository db,
        int          userId,
        string       title,
        string       message,
        string       type              = "General",
        string?      relatedEntityType = null,
        int?         relatedEntityId   = null)
    {
        await db.SaveDataAsync(InsertSql, new
        {
            UserId            = userId,
            Title             = title,
            Message           = message,
            Type              = type,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId   = relatedEntityId,
        });
    }

    /// <summary>Creates the same notification for multiple users (deduped by Id).</summary>
    public static async Task CreateForUsersAsync(
        DbRepository    db,
        IEnumerable<int> userIds,
        string           title,
        string           message,
        string           type              = "General",
        string?          relatedEntityType = null,
        int?             relatedEntityId   = null)
    {
        foreach (int uid in userIds.Distinct())
            await CreateAsync(db, uid, title, message, type, relatedEntityType, relatedEntityId);
    }
}
