namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>
/// Admin-facing view of a single integration's OAuth configuration.
/// ClientSecret is intentionally omitted — it is never sent to the client.
/// </summary>
public class IntegrationSettingsDto
{
    public string Provider    { get; set; } = "";
    public string ClientId    { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string Scopes      { get; set; } = "";
    public bool   IsEnabled   { get; set; }
    /// <summary>True when ClientId is non-empty (secret exists in DB).</summary>
    public bool   HasSecret   { get; set; }
}

/// <summary>Admin request to create or update an integration's configuration.</summary>
public class UpdateIntegrationSettingsRequest
{
    public string ClientId     { get; set; } = "";
    /// <summary>Send empty string to leave the existing secret unchanged.</summary>
    public string ClientSecret { get; set; } = "";
    public string RedirectUri  { get; set; } = "";
    public string Scopes       { get; set; } = "";
    public bool   IsEnabled    { get; set; }
}

/// <summary>
/// Lightweight status for student-facing pages — indicates whether a provider
/// is configured and enabled at the system level, without exposing credentials.
/// </summary>
public class IntegrationStatusDto
{
    public bool IsConfigured { get; set; }
    public bool IsEnabled    { get; set; }
}
