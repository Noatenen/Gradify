using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

/// <summary>
/// Admin-only CRUD for system-level integration configurations (OAuth credentials).
/// Credentials stored here are read by provider-specific controllers (e.g. SlackController)
/// so students never need to enter app credentials themselves.
/// </summary>
[Route("api/admin/integrations")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public class IntegrationSettingsController : ControllerBase
{
    private readonly DbRepository _db;

    public IntegrationSettingsController(DbRepository db) => _db = db;

    // GET /api/admin/integrations/slack
    // Returns current Slack configuration (without the secret).
    [HttpGet("slack")]
    public async Task<IActionResult> GetSlack(int authUserId)
    {
        var rows = await _db.GetRecordsAsync<IntegrationRow>(
            "SELECT Provider, ClientId, ClientSecret, RedirectUri, Scopes, IsEnabled FROM IntegrationSettings WHERE Provider = 'Slack'");

        var row = rows?.FirstOrDefault();
        if (row is null)
            return Ok(new IntegrationSettingsDto { Provider = "Slack" });

        return Ok(new IntegrationSettingsDto
        {
            Provider    = "Slack",
            ClientId    = row.ClientId,
            RedirectUri = row.RedirectUri,
            Scopes      = row.Scopes,
            IsEnabled   = row.IsEnabled == 1,
            HasSecret   = !string.IsNullOrEmpty(row.ClientSecret),
        });
    }

    // PUT /api/admin/integrations/slack
    // Upserts Slack configuration. Empty ClientSecret = keep existing secret.
    [HttpPut("slack")]
    public async Task<IActionResult> UpsertSlack(
        [FromBody] UpdateIntegrationSettingsRequest req,
        int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.ClientId))
            return BadRequest("ClientId is required.");

        // SQLite upsert: ON CONFLICT(Provider) preserves the existing secret
        // when the incoming ClientSecret is empty.
        await _db.SaveDataAsync(@"
            INSERT INTO IntegrationSettings
                (Provider, ClientId, ClientSecret, RedirectUri, Scopes, IsEnabled, UpdatedAt)
            VALUES
                ('Slack', @ClientId, @ClientSecret, @RedirectUri, @Scopes, @IsEnabled, datetime('now'))
            ON CONFLICT(Provider) DO UPDATE SET
                ClientId     = excluded.ClientId,
                ClientSecret = CASE
                                   WHEN excluded.ClientSecret = '' THEN ClientSecret
                                   ELSE excluded.ClientSecret
                               END,
                RedirectUri  = excluded.RedirectUri,
                Scopes       = excluded.Scopes,
                IsEnabled    = excluded.IsEnabled,
                UpdatedAt    = datetime('now')",
            new
            {
                req.ClientId,
                ClientSecret = req.ClientSecret ?? "",
                req.RedirectUri,
                Scopes    = req.Scopes ?? "",
                IsEnabled = req.IsEnabled ? 1 : 0,
            });

        return NoContent();
    }

    // ── Internal DB row model ─────────────────────────────────────────────────
    private sealed class IntegrationRow
    {
        public string Provider     { get; set; } = "";
        public string ClientId     { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string RedirectUri  { get; set; } = "";
        public string Scopes       { get; set; } = "";
        public int    IsEnabled    { get; set; }
    }
}
