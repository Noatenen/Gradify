using System.Text;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AuthWithAdmin.Server.Controllers;

/// <summary>
/// Slack OAuth v2 integration.
///
/// Credentials are read from the IntegrationSettings table (admin-configurable via UI).
/// If the DB has no Slack entry, appsettings.json values are used as a fallback so
/// existing deployments continue to work without any admin action.
///
/// Flow:
///   1. GET /api/slack/connect-url  — authenticated student gets the authorization URL.
///   2. Client navigates there; user approves in Slack.
///   3. Slack redirects to GET /api/slack/callback?code=...&amp;state=... (public).
///   4. Callback exchanges code → token, stores it, redirects to /settings.
///   5. DELETE /api/slack/disconnect — deactivates the connection.
///
/// The state parameter carries Base64(userId.ToString()) so the public callback
/// can associate the token with the correct user without a session lookup.
/// </summary>
[Route("api/slack")]
[ApiController]
[Authorize]
public class SlackController : ControllerBase
{
    private readonly DbRepository       _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly SlackOptions       _fallback;   // appsettings fallback

    public SlackController(
        DbRepository db,
        IHttpClientFactory httpFactory,
        IOptions<SlackOptions> opts)
    {
        _db          = db;
        _httpFactory = httpFactory;
        _fallback    = opts.Value;
    }

    // GET /api/slack/system-status  (any authenticated user)
    // Returns whether Slack is configured and enabled at the system level.
    // Used by the student profile page to decide which UI to show.
    [HttpGet("system-status")]
    [ServiceFilter(typeof(AuthCheck))]
    public async Task<IActionResult> GetSystemStatus(int authUserId)
    {
        var cfg = await LoadConfigAsync();
        return Ok(new IntegrationStatusDto
        {
            IsConfigured = cfg is not null,
            IsEnabled    = cfg is not null,
        });
    }

    // GET /api/slack/connect-url  (authenticated student)
    [HttpGet("connect-url")]
    [ServiceFilter(typeof(AuthCheck))]
    public async Task<IActionResult> GetConnectUrl(int authUserId)
    {
        var cfg = await LoadConfigAsync();
        if (cfg is null)
            return StatusCode(503, "Slack integration is not configured.");

        var state = Convert.ToBase64String(Encoding.UTF8.GetBytes(authUserId.ToString()));

        var scopes     = string.IsNullOrWhiteSpace(cfg.Scopes) ? "chat:write" : cfg.Scopes;
        var userScopes = "identity.basic";

        var url = "https://slack.com/oauth/v2/authorize"
            + $"?client_id={Uri.EscapeDataString(cfg.ClientId)}"
            + $"&scope={Uri.EscapeDataString(scopes)}"
            + $"&user_scope={Uri.EscapeDataString(userScopes)}"
            + $"&redirect_uri={Uri.EscapeDataString(cfg.RedirectUri)}"
            + $"&state={Uri.EscapeDataString(state)}";

        return Ok(new { url });
    }

    // GET /api/slack/callback  (public — Slack redirects here)
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error)
    {
        var frontendBase = $"{Request.Scheme}://{Request.Host}";

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return Redirect($"{frontendBase}/settings?slackError=true");

        int userId;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(state));
            if (!int.TryParse(decoded, out userId) || userId <= 0)
                return Redirect($"{frontendBase}/settings?slackError=true");
        }
        catch
        {
            return Redirect($"{frontendBase}/settings?slackError=true");
        }

        var cfg = await LoadConfigAsync();
        if (cfg is null)
            return Redirect($"{frontendBase}/settings?slackError=true");

        var http        = _httpFactory.CreateClient("Slack");
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{cfg.ClientId}:{cfg.ClientSecret}"));
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("code",         code),
            new KeyValuePair<string, string>("redirect_uri", cfg.RedirectUri),
        });

        HttpResponseMessage tokenResponse;
        try
        {
            tokenResponse = await http.PostAsync("https://slack.com/api/oauth.v2.access", form);
        }
        catch
        {
            return Redirect($"{frontendBase}/settings?slackError=true");
        }

        if (!tokenResponse.IsSuccessStatusCode)
            return Redirect($"{frontendBase}/settings?slackError=true");

        var json = await tokenResponse.Content.ReadFromJsonAsync<SlackOAuthResponse>();
        if (json?.Ok != true)
            return Redirect($"{frontendBase}/settings?slackError=true");

        await _db.SaveDataAsync(@"
            INSERT OR REPLACE INTO SlackIntegrations
                (UserId, SlackUserId, SlackTeamId, SlackTeamName,
                 AccessToken, WebhookUrl, IsActive, ConnectedAt)
            VALUES
                (@UserId, @SlackUserId, @SlackTeamId, @SlackTeamName,
                 @AccessToken, @WebhookUrl, 1, datetime('now'))",
            new
            {
                UserId        = userId,
                SlackUserId   = json.AuthedUser?.Id        ?? "",
                SlackTeamId   = json.Team?.Id              ?? "",
                SlackTeamName = json.Team?.Name            ?? "",
                AccessToken   = json.AccessToken           ?? "",
                WebhookUrl    = json.IncomingWebhook?.Url  ?? "",
            });

        return Redirect($"{frontendBase}/settings?slackConnected=true");
    }

    // DELETE /api/slack/disconnect  (authenticated student)
    [HttpDelete("disconnect")]
    [ServiceFilter(typeof(AuthCheck))]
    public async Task<IActionResult> Disconnect(int authUserId)
    {
        await _db.SaveDataAsync(
            "UPDATE SlackIntegrations SET IsActive = 0 WHERE UserId = @UserId",
            new { UserId = authUserId });
        return NoContent();
    }

    // ── Config loader ─────────────────────────────────────────────────────────
    // Priority: IntegrationSettings table → appsettings.json fallback.

    private async Task<SlackConfig?> LoadConfigAsync()
    {
        var rows = await _db.GetRecordsAsync<IntegrationRow>(
            "SELECT ClientId, ClientSecret, RedirectUri, Scopes, IsEnabled FROM IntegrationSettings WHERE Provider = 'Slack'");

        var row = rows?.FirstOrDefault();

        if (row is not null && !string.IsNullOrEmpty(row.ClientId) && row.IsEnabled == 1)
        {
            return new SlackConfig(row.ClientId, row.ClientSecret, row.RedirectUri, row.Scopes);
        }

        // Appsettings fallback — preserves backward compat for existing deployments.
        if (!string.IsNullOrEmpty(_fallback.ClientId))
            return new SlackConfig(_fallback.ClientId, _fallback.ClientSecret, _fallback.RedirectUri, "");

        return null;
    }

    // ── Internal models ───────────────────────────────────────────────────────

    private sealed record SlackConfig(
        string ClientId,
        string ClientSecret,
        string RedirectUri,
        string Scopes);

    private sealed class IntegrationRow
    {
        public string ClientId     { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string RedirectUri  { get; set; } = "";
        public string Scopes       { get; set; } = "";
        public int    IsEnabled    { get; set; }
    }

    private sealed class SlackOAuthResponse
    {
        [JsonPropertyName("ok")]               public bool              Ok              { get; set; }
        [JsonPropertyName("access_token")]     public string?           AccessToken     { get; set; }
        [JsonPropertyName("authed_user")]      public SlackAuthedUser?  AuthedUser      { get; set; }
        [JsonPropertyName("team")]             public SlackTeam?        Team            { get; set; }
        [JsonPropertyName("incoming_webhook")] public SlackWebhook?     IncomingWebhook { get; set; }
    }

    private sealed class SlackAuthedUser
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }

    private sealed class SlackTeam
    {
        [JsonPropertyName("id")]   public string? Id   { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class SlackWebhook
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
    }
}
