using System.Security.Cryptography;
using System.Text;

namespace AuthWithAdmin.Server.Data;

/// <summary>
/// Server-side, single-use OAuth <c>state</c> values.
///
/// This deliberately does NOT follow the Slack controller's
/// <c>Base64(userId)</c> state. That value is not a secret and not a nonce: it
/// is a reversible encoding of a public integer, so anyone can mint one for any
/// user, and the callback will happily attach the resulting grant to whoever the
/// attacker names. It also gives no replay protection and no expiry.
///
/// What is issued here instead:
///
///   * 256 bits from <see cref="RandomNumberGenerator"/> — unguessable, and it
///     carries no information about the user at all.
///   * bound server-side to the authenticated Motiva UserId, in a row the
///     browser never sees.
///   * single-use — consumption is the UPDATE itself, so a replay affects zero
///     rows and is rejected even if two callbacks race.
///   * short-lived (10 minutes by default) and validated on expiry.
///
/// Only the SHA-256 HASH of the state is stored. A leaked database backup
/// therefore still cannot be used to forge a live authorization.
///
/// The table is provider-keyed so the same mechanism can be reused by other
/// integrations later without a second implementation.
/// </summary>
public class OAuthStateService
{
    /// <summary>Provider key for the Google Calendar connection flow.</summary>
    public const string GoogleCalendarProvider = "GoogleCalendar";

    private readonly DbRepository _db;

    public OAuthStateService(DbRepository db) => _db = db;

    /// <summary>
    /// Issues a fresh state bound to <paramref name="userId"/> and returns the
    /// raw value to put in the authorization URL. The raw value is returned
    /// once and never stored.
    /// </summary>
    public async Task<string> IssueAsync(string provider, int userId, int lifetimeMinutes)
    {
        // Housekeeping: consumed and expired rows have no further purpose.
        // Done on issue rather than on a timer so it needs no background service.
        await _db.SaveDataAsync(
            "DELETE FROM OAuthStates WHERE ExpiresAt < datetime('now', '-1 day')");

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));

        await _db.SaveDataAsync(@"
            INSERT INTO OAuthStates (Provider, StateHash, UserId, CreatedAt, ExpiresAt)
            VALUES (@Provider, @StateHash, @UserId, datetime('now'),
                    datetime('now', @Lifetime))",
            new
            {
                Provider  = provider,
                StateHash = Hash(state),
                UserId    = userId,
                Lifetime  = $"+{Math.Max(1, lifetimeMinutes)} minutes",
            });

        return state;
    }

    /// <summary>
    /// Validates and consumes a state, returning the Motiva UserId it was
    /// issued to, or null when the state is unknown, already used, expired or
    /// belongs to a different provider.
    /// </summary>
    public async Task<int?> ConsumeAsync(string provider, string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;

        var hash = Hash(state);

        // Single-use is enforced BY this statement, not by a read-then-write:
        // ConsumedAt can only move away from NULL once, so a replayed state
        // updates 0 rows and is rejected below.
        var affected = await _db.SaveDataAsync(@"
            UPDATE OAuthStates
               SET ConsumedAt = datetime('now')
             WHERE Provider   = @Provider
               AND StateHash  = @StateHash
               AND ConsumedAt IS NULL
               AND ExpiresAt  > datetime('now')",
            new { Provider = provider, StateHash = hash });

        if (affected != 1) return null;

        var rows = await _db.GetRecordsAsync<int>(
            "SELECT UserId FROM OAuthStates WHERE Provider = @Provider AND StateHash = @StateHash",
            new { Provider = provider, StateHash = hash });

        var userId = rows?.ToList();
        if (userId is null || userId.Count == 0 || userId[0] <= 0) return null;

        return userId[0];
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    /// <summary>Base64url — safe to drop straight into a query string.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
               .TrimEnd('=')
               .Replace('+', '-')
               .Replace('/', '_');
}
