using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AuthWithAdmin.Server.Data;

/// <summary>
/// The single owner of Google Calendar OAuth credentials.
///
/// Everything that touches a Google token lives here — protecting it before it
/// reaches SQLite, unprotecting it on the way out, exchanging an authorization
/// code, refreshing an expired access token, revoking a grant, and retiring a
/// connection Google has rejected. The controller composes these; it never sees
/// a raw token and never talks to Google itself.
///
/// Storage rules:
///   * Both tokens are stored through ASP.NET Core Data Protection. The refresh
///     token MUST be protected (it is a long-lived credential); the access token
///     is protected too because it costs one line and there is no reason to
///     leave a bearer token readable in a DB file that also ships as a demo.
///   * Nothing in this class logs a token, an authorization code, or the client
///     secret. Log lines carry a user id and a Google error CODE only.
///
/// GetValidAccessTokenAsync is the API the later event-sync phase will consume;
/// it is implemented now so the refresh/expiry/invalid_grant behaviour is
/// settled before anything depends on it.
/// </summary>
public class GoogleCalendarTokenService
{
    public const string HttpClientName = "GoogleCalendar";

    private const string AuthEndpoint   = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint  = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    /// <summary>
    /// Data Protection purpose. Versioned so a future re-encryption can be
    /// rolled out without silently reading old payloads with new semantics.
    /// </summary>
    private const string ProtectorPurpose = "Motiva.GoogleCalendar.Tokens.v1";

    private readonly DbRepository                       _db;
    private readonly IHttpClientFactory                 _httpFactory;
    private readonly IDataProtector                     _protector;
    private readonly IConfiguration                     _config;
    private readonly GoogleCalendarOptions              _options;
    private readonly ILogger<GoogleCalendarTokenService> _log;

    public GoogleCalendarTokenService(
        DbRepository                        db,
        IHttpClientFactory                  httpFactory,
        IDataProtectionProvider             protectionProvider,
        IConfiguration                      config,
        IOptions<GoogleCalendarOptions>     options,
        ILogger<GoogleCalendarTokenService> log)
    {
        _db          = db;
        _httpFactory = httpFactory;
        _protector   = protectionProvider.CreateProtector(ProtectorPurpose);
        _config      = config;
        _options     = options.Value;
        _log         = log;
    }

    // ── Configuration ─────────────────────────────────────────────────────────
    // Reused from the existing Google login registration. NOT duplicated into a
    // Calendar-specific section — one client, one place to rotate.

    private string ClientId     => _config["Authentication:Google:ClientId"]     ?? "";
    private string ClientSecret => _config["Authentication:Google:ClientSecret"] ?? "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public string Scopes =>
        string.IsNullOrWhiteSpace(_options.Scopes)
            ? GoogleCalendarOptions.DefaultScopes
            : _options.Scopes;

    /// <summary>
    /// Builds the Google consent URL.
    ///
    /// access_type=offline + prompt=consent is what guarantees a refresh token
    /// on every pass, including a reconnect — Google otherwise returns one only
    /// on a user's very first authorization of the client, which for a Motiva
    /// user who already signed in with Google would be never.
    ///
    /// include_granted_scopes=true makes this incremental authorization: the
    /// grant already held by the login flow is preserved rather than replaced.
    /// </summary>
    public string BuildAuthorizationUrl(string state, string redirectUri) =>
        AuthEndpoint
        + "?response_type=code"
        + $"&client_id={Uri.EscapeDataString(ClientId)}"
        + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
        + $"&scope={Uri.EscapeDataString(Scopes)}"
        + "&access_type=offline"
        + "&prompt=consent"
        + "&include_granted_scopes=true"
        + $"&state={Uri.EscapeDataString(state)}";

    // ── Connection state ──────────────────────────────────────────────────────

    /// <summary>
    /// Minimal, non-sensitive connection status. The ONLY source of truth for
    /// "is this user connected" — UserPreferences.GoogleCalendarConnected is a
    /// self-declared checkbox and is never consulted.
    /// </summary>
    public async Task<GoogleCalendarStatusDto> GetStatusAsync(int userId)
    {
        var row = await GetRowAsync(userId);

        if (row is null || row.IsActive != 1 || string.IsNullOrEmpty(row.RefreshTokenProtected))
            return new GoogleCalendarStatusDto { IsConnected = false };

        return new GoogleCalendarStatusDto
        {
            IsConnected = true,
            GoogleEmail = string.IsNullOrWhiteSpace(row.GoogleEmail) ? null : row.GoogleEmail,
            ConnectedAt = row.ConnectedAt,
        };
    }

    // ── Connect ───────────────────────────────────────────────────────────────

    public enum ConnectOutcome
    {
        Success,
        NotConfigured,
        TokenExchangeFailed,
        MissingRefreshToken,
    }

    /// <summary>
    /// Exchanges an authorization code and persists the resulting connection.
    /// <paramref name="redirectUri"/> must be byte-identical to the one used to
    /// start the flow — Google validates it on exchange.
    /// </summary>
    public async Task<ConnectOutcome> CompleteConnectionAsync(int userId, string code, string redirectUri)
    {
        if (!IsConfigured) return ConnectOutcome.NotConfigured;

        var token = await PostTokenRequestAsync(new Dictionary<string, string>
        {
            ["code"]          = code,
            ["client_id"]     = ClientId,
            ["client_secret"] = ClientSecret,
            ["redirect_uri"]  = redirectUri,
            ["grant_type"]    = "authorization_code",
        });

        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            // token?.Error is a Google error CODE ("invalid_grant",
            // "redirect_uri_mismatch", ...) — safe to log, unlike the body.
            _log.LogWarning(
                "Google Calendar code exchange failed for user {UserId}. Google error: {Error}",
                userId, token?.Error ?? "(transport)");
            return ConnectOutcome.TokenExchangeFailed;
        }

        var existing = await GetRowAsync(userId);

        // Reconnect safety: prompt=consent should always return a refresh token,
        // but if one ever fails to arrive we keep the credential we already have
        // rather than overwriting a working grant with nothing.
        string refreshProtected;
        if (!string.IsNullOrEmpty(token.RefreshToken))
            refreshProtected = _protector.Protect(token.RefreshToken);
        else if (!string.IsNullOrEmpty(existing?.RefreshTokenProtected))
            refreshProtected = existing!.RefreshTokenProtected;
        else
        {
            _log.LogWarning(
                "Google returned no refresh token for user {UserId} and none is stored; connection refused.",
                userId);
            return ConnectOutcome.MissingRefreshToken;
        }

        // Falls back to the previously known address so a reconnect made with a
        // trimmed scope list does not blank out an address we already had.
        var email = ReadEmailFromIdToken(token.IdToken)
                    ?? existing?.GoogleEmail
                    ?? "";

        await _db.SaveDataAsync(@"
            INSERT INTO GoogleCalendarConnections
                (UserId, GoogleEmail, AccessTokenProtected, RefreshTokenProtected,
                 AccessTokenExpiresAt, Scopes, ConnectedAt, UpdatedAt, IsActive)
            VALUES
                (@UserId, @GoogleEmail, @AccessTokenProtected, @RefreshTokenProtected,
                 @AccessTokenExpiresAt, @Scopes, datetime('now'), datetime('now'), 1)
            ON CONFLICT(UserId) DO UPDATE SET
                GoogleEmail           = excluded.GoogleEmail,
                AccessTokenProtected  = excluded.AccessTokenProtected,
                RefreshTokenProtected = excluded.RefreshTokenProtected,
                AccessTokenExpiresAt  = excluded.AccessTokenExpiresAt,
                Scopes                = excluded.Scopes,
                ConnectedAt           = datetime('now'),
                UpdatedAt             = datetime('now'),
                IsActive              = 1",
            new
            {
                UserId                = userId,
                GoogleEmail           = email,
                AccessTokenProtected  = _protector.Protect(token.AccessToken),
                RefreshTokenProtected = refreshProtected,
                AccessTokenExpiresAt  = ExpiryStamp(token.ExpiresIn),
                Scopes                = token.Scope ?? Scopes,
            });

        _log.LogInformation("Google Calendar connected for user {UserId}.", userId);
        return ConnectOutcome.Success;
    }

    // ── Access token ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a usable access token for <paramref name="userId"/>, refreshing
    /// it first when it is expired or close to it.
    ///
    /// Reused by the later Calendar event integration — callers only ever need
    /// to check <see cref="GoogleAccessTokenResult.IsConnected"/>.
    /// </summary>
    public async Task<GoogleAccessTokenResult> GetValidAccessTokenAsync(int userId)
    {
        var row = await GetRowAsync(userId);
        if (row is null || row.IsActive != 1)
            return GoogleAccessTokenResult.Disconnected("not_connected");

        var access    = TryUnprotect(row.AccessTokenProtected);
        var expiresAt = ParseUtc(row.AccessTokenExpiresAt);

        if (!string.IsNullOrEmpty(access)
            && expiresAt is not null
            && expiresAt > DateTime.UtcNow.AddSeconds(_options.RefreshSkewSeconds))
        {
            return GoogleAccessTokenResult.Ok(access);
        }

        var refresh = TryUnprotect(row.RefreshTokenProtected);
        if (string.IsNullOrEmpty(refresh))
        {
            // Either the row is corrupt or the Data Protection key ring that
            // encrypted it is gone. Either way the credential is unrecoverable,
            // so retire it instead of failing on every future call.
            await DeactivateAsync(userId);
            _log.LogWarning(
                "Google Calendar credential for user {UserId} could not be unprotected; connection retired.",
                userId);
            return GoogleAccessTokenResult.Disconnected("invalid_credentials");
        }

        var token = await PostTokenRequestAsync(new Dictionary<string, string>
        {
            ["client_id"]     = ClientId,
            ["client_secret"] = ClientSecret,
            ["refresh_token"] = refresh,
            ["grant_type"]    = "refresh_token",
        });

        // Transport failure — Google may be fine in a second. The connection is
        // left intact so a network blip does not disconnect the user.
        if (token is null)
            return GoogleAccessTokenResult.Disconnected("refresh_failed");

        if (!string.IsNullOrEmpty(token.Error))
        {
            // invalid_grant is terminal: the user revoked access in their Google
            // account, the grant expired, or the refresh token was replaced.
            // Retiring the row here is what stops an endless retry loop.
            if (string.Equals(token.Error, "invalid_grant", StringComparison.Ordinal))
            {
                await DeactivateAsync(userId);
                _log.LogInformation(
                    "Google rejected the refresh token for user {UserId} (invalid_grant); connection retired.",
                    userId);
                return GoogleAccessTokenResult.Disconnected("invalid_grant");
            }

            _log.LogWarning(
                "Google Calendar token refresh failed for user {UserId}. Google error: {Error}",
                userId, token.Error);
            return GoogleAccessTokenResult.Disconnected("refresh_failed");
        }

        if (string.IsNullOrEmpty(token.AccessToken))
            return GoogleAccessTokenResult.Disconnected("refresh_failed");

        // Google usually omits refresh_token on a refresh response; when it DOES
        // send one it is a replacement and the old value stops working, so it
        // has to be written through.
        var newRefreshProtected = string.IsNullOrEmpty(token.RefreshToken)
            ? row.RefreshTokenProtected
            : _protector.Protect(token.RefreshToken);

        await _db.SaveDataAsync(@"
            UPDATE GoogleCalendarConnections
               SET AccessTokenProtected  = @AccessTokenProtected,
                   AccessTokenExpiresAt  = @AccessTokenExpiresAt,
                   RefreshTokenProtected = @RefreshTokenProtected,
                   UpdatedAt             = datetime('now')
             WHERE UserId = @UserId",
            new
            {
                UserId                = userId,
                AccessTokenProtected  = _protector.Protect(token.AccessToken),
                AccessTokenExpiresAt  = ExpiryStamp(token.ExpiresIn),
                RefreshTokenProtected = newRefreshProtected,
            });

        return GoogleAccessTokenResult.Ok(token.AccessToken);
    }

    // ── Disconnect ────────────────────────────────────────────────────────────

    /// <summary>
    /// Disconnects the user's Calendar grant.
    ///
    /// The LOCAL credential is invalidated first and unconditionally, so the
    /// operation cannot half-succeed: once this returns, Motiva holds nothing
    /// it could use, whether or not Google's revoke endpoint answered.
    ///
    /// This touches nothing about the Motiva session — no token blacklisting,
    /// no cookie, no users row. Disconnecting Calendar cannot log anyone out.
    /// </summary>
    public async Task DisconnectAsync(int userId)
    {
        var row     = await GetRowAsync(userId);
        var refresh = row is null ? null : TryUnprotect(row.RefreshTokenProtected);

        await DeactivateAsync(userId);

        if (!string.IsNullOrEmpty(refresh))
            await TryRevokeAsync(userId, refresh);

        _log.LogInformation("Google Calendar disconnected for user {UserId}.", userId);
    }

    /// <summary>
    /// Best-effort revoke of the Google-side grant. A failure here is logged and
    /// swallowed: the local credential is already gone, and Google's own grant
    /// remains removable by the user from their account settings.
    /// </summary>
    private async Task TryRevokeAsync(int userId, string refreshToken)
    {
        try
        {
            var http = _httpFactory.CreateClient(HttpClientName);
            var res  = await http.PostAsync(RevokeEndpoint, new FormUrlEncodedContent(
                new Dictionary<string, string> { ["token"] = refreshToken }));

            if (!res.IsSuccessStatusCode)
                _log.LogWarning(
                    "Google revoke returned {Status} for user {UserId}; local credential was already cleared.",
                    (int)res.StatusCode, userId);
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                "Google revoke call failed for user {UserId}: {Message}. Local credential was already cleared.",
                userId, ex.Message);
        }
    }

    /// <summary>
    /// Retires a connection: flag off AND both token columns wiped, so a
    /// deactivated row can never be resurrected into a usable credential.
    /// GoogleEmail / ConnectedAt survive as history, matching how
    /// SlackIntegrations handles a disconnect.
    /// </summary>
    private Task DeactivateAsync(int userId) =>
        _db.SaveDataAsync(@"
            UPDATE GoogleCalendarConnections
               SET IsActive              = 0,
                   AccessTokenProtected  = '',
                   RefreshTokenProtected = '',
                   AccessTokenExpiresAt  = NULL,
                   UpdatedAt             = datetime('now')
             WHERE UserId = @UserId",
            new { UserId = userId });

    // ── Google HTTP ───────────────────────────────────────────────────────────

    /// <summary>
    /// Posts to Google's token endpoint. The response body carries live
    /// credentials, so it is parsed but NEVER logged — only the parsed
    /// <c>error</c> code ever reaches a log line.
    /// </summary>
    private async Task<GoogleTokenResponse?> PostTokenRequestAsync(Dictionary<string, string> form)
    {
        string body;
        try
        {
            var http = _httpFactory.CreateClient(HttpClientName);
            var res  = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form));
            body = await res.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            // Transport only. HttpRequestException's message describes DNS /
            // TLS / connectivity — it has never seen the response body.
            _log.LogWarning("Google token request failed at transport level: {Message}", ex.Message);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GoogleTokenResponse>(body);
        }
        catch (JsonException)
        {
            // Parsing is kept in its own catch and the exception is NOT logged:
            // a JsonException message quotes the offending input, and that input
            // is a token response.
            _log.LogWarning("Google token response could not be parsed.");
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<ConnectionRow?> GetRowAsync(int userId)
    {
        var rows = await _db.GetRecordsAsync<ConnectionRow>(@"
            SELECT Id, UserId, GoogleEmail, AccessTokenProtected, RefreshTokenProtected,
                   AccessTokenExpiresAt, Scopes, ConnectedAt, IsActive
              FROM GoogleCalendarConnections
             WHERE UserId = @UserId",
            new { UserId = userId });

        return rows?.FirstOrDefault();
    }

    private string? TryUnprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;

        try { return _protector.Unprotect(protectedValue); }
        catch (CryptographicException) { return null; }   // key ring rotated away / payload tampered
    }

    /// <summary>
    /// Reads the connected account's address out of the id_token.
    ///
    /// The signature is intentionally not re-validated: this id_token came
    /// straight back from Google's token endpoint over TLS, in response to a
    /// request authenticated with our own client secret — the case OIDC Core
    /// §3.1.3.7 explicitly allows to skip validation. It is used for display
    /// only and never as an authentication decision.
    /// </summary>
    private string? ReadEmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken)) return null;

        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(idToken);
            var email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
            return string.IsNullOrWhiteSpace(email) ? null : email;
        }
        catch
        {
            return null;   // no identity scope granted, or an unexpected shape
        }
    }

    /// <summary>UTC stamp in the same text format SQLite's datetime() produces.</summary>
    private static string ExpiryStamp(int expiresInSeconds) =>
        DateTime.UtcNow.AddSeconds(expiresInSeconds > 0 ? expiresInSeconds : 3600)
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static DateTime? ParseUtc(string? stamp)
    {
        if (string.IsNullOrWhiteSpace(stamp)) return null;

        return DateTime.TryParse(
            stamp, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed) ? parsed : null;
    }

    // ── Internal models ───────────────────────────────────────────────────────

    private sealed class ConnectionRow
    {
        public int     Id                    { get; set; }
        public int     UserId                { get; set; }
        public string  GoogleEmail           { get; set; } = "";
        public string  AccessTokenProtected  { get; set; } = "";
        public string  RefreshTokenProtected { get; set; } = "";
        public string? AccessTokenExpiresAt  { get; set; }
        public string  Scopes                { get; set; } = "";
        public string  ConnectedAt           { get; set; } = "";
        public int     IsActive              { get; set; }
    }

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]  public string? AccessToken  { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("id_token")]      public string? IdToken      { get; set; }
        [JsonPropertyName("expires_in")]    public int     ExpiresIn    { get; set; }
        [JsonPropertyName("scope")]         public string? Scope        { get; set; }
        [JsonPropertyName("error")]         public string? Error        { get; set; }
    }
}

/// <summary>
/// Result of asking for a usable access token. <see cref="AccessToken"/> is
/// non-null exactly when <see cref="IsConnected"/> is true.
/// </summary>
public sealed record GoogleAccessTokenResult(bool IsConnected, string? AccessToken, string Reason)
{
    public static GoogleAccessTokenResult Ok(string accessToken) => new(true, accessToken, "ok");

    public static GoogleAccessTokenResult Disconnected(string reason) => new(false, null, reason);
}
