using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Data;

// ─────────────────────────────────────────────────────────────────────────────
//  NotificationDispatcher — sends an "important" notification both as an
//  in-app row AND, when the recipient has opted in, as an email.
//
//  Design:
//    - Wraps NotificationHelper.CreateForUsersAsync as the in-app primitive.
//      Always creates the in-app row — preserves the existing UX.
//    - Looks up each recipient's UserNotificationPreferences row for the
//      given NotificationType. Missing rows fall back to
//      NotificationTypes.DefaultsForType(...) so a fresh account already
//      receives email for high-priority types.
//    - Sends one email per opted-in recipient via the existing EmailHelper.
//      Email failures are caught and logged — they never fail the call.
//
//  Static + dependency-free in spirit (takes EmailHelper as a parameter), so
//  controllers can call it inline without extra DI ceremony.
// ─────────────────────────────────────────────────────────────────────────────
public static class NotificationDispatcher
{
    /// <summary>Sends a notification to a set of users (in-app always, email
    /// per their saved preference for this NotificationType).</summary>
    public static async Task DispatchAsync(
        DbRepository    db,
        EmailHelper?    email,
        IEnumerable<int> userIds,
        string           title,
        string           message,
        string           type,
        string?          relatedEntityType = null,
        int?             relatedEntityId   = null)
    {
        var distinctIds = userIds.Distinct().ToList();
        if (distinctIds.Count == 0) return;

        // 1. In-app — always written first so the UX is preserved even if
        //    email config is broken on this environment.
        await NotificationHelper.CreateForUsersAsync(
            db, distinctIds, title, message, type, relatedEntityType, relatedEntityId);

        // 2. Resolve which recipients have email opted-in for this type +
        //    have a non-empty email. NULL rows fall back to defaults.
        if (email is null) return;

        string idsCsv = string.Join(",", distinctIds);
        const string sqlTpl = @"
            SELECT  u.Id        AS UserId,
                    COALESCE(u.Email, '') AS Email,
                    COALESCE(p.EmailEnabled, -1) AS EmailEnabledRaw
            FROM    users u
            LEFT JOIN UserNotificationPreferences p
                            ON p.UserId = u.Id AND p.NotificationType = @Type
            WHERE   u.Id IN ({0})
              AND   u.IsActive = 1";
        string sql = string.Format(sqlTpl, idsCsv);
        var rows = (await db.GetRecordsAsync<EmailRow>(sql, new { Type = type }))?.ToList() ?? new();

        var (defaultInApp, defaultEmail) = NotificationTypes.DefaultsForType(type);

        foreach (var r in rows)
        {
            // No email address → can't send safely, skip.
            if (string.IsNullOrWhiteSpace(r.Email)) continue;

            bool emailOn = r.EmailEnabledRaw == -1
                ? defaultEmail            // no row → use default
                : r.EmailEnabledRaw == 1; // explicit value
            if (!emailOn) continue;

            try
            {
                await email.SendEmail(new MailModel
                {
                    Subject    = title,
                    Body       = BuildEmailBody(title, message),
                    Recipients = new List<string> { r.Email },
                });
            }
            catch (Exception ex)
            {
                // Email failures must never break the in-app flow.
                Console.WriteLine($"NotificationDispatcher: email send failed for user {r.UserId}, type={type}: {ex.Message}");
            }
        }
    }

    private static string BuildEmailBody(string title, string message)
    {
        // Minimal RTL Hebrew-friendly body. Templated emails for specific
        // events can land later; for now we wrap the same in-app text so the
        // recipient gets a clean, readable email on day one.
        return $@"
            <div dir=""rtl"" style=""font-family: 'Assistant', Arial, sans-serif; line-height:1.5; color:#0f172a;"">
                <h2 style=""margin:0 0 12px;"">{System.Net.WebUtility.HtmlEncode(title)}</h2>
                <p style=""margin:0 0 16px; white-space:pre-wrap;"">{System.Net.WebUtility.HtmlEncode(message)}</p>
                <hr style=""border:none; border-top:1px solid #e2e8f0; margin:18px 0;"" />
                <p style=""color:#64748b; font-size:12px;"">
                    התראה זו נשלחה אליך לפי העדפות ההתראות שלך במערכת Gradify.
                    ניתן לשנות את ההעדפות בעמוד ההגדרות → העדפות התראות.
                </p>
            </div>";
    }

    private sealed class EmailRow
    {
        public int    UserId          { get; set; }
        public string Email           { get; set; } = "";
        /// <summary>1 = on, 0 = off, -1 = no row (fall back to default).</summary>
        public int    EmailEnabledRaw { get; set; }
    }
}
