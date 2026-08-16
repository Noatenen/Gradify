namespace AuthWithAdmin.Server.Data;

/// <summary>
/// Calendar-specific OAuth configuration (appsettings section "GoogleCalendar").
///
/// The OAuth CLIENT CREDENTIALS are deliberately NOT here. Motiva has exactly
/// one Google Web Application client, and both flows read it from the single
/// existing location:
///
///     Authentication:Google:ClientId
///     Authentication:Google:ClientSecret
///
/// Duplicating them under a second section would mean two places to rotate and
/// two places to leak from.
///
/// Everything in this class is a PATH or a scope list, never a host: the actual
/// redirect URI is composed at request time from Request.Scheme + Request.Host
/// + Request.PathBase, so the same build works on https://localhost:7275 and on
/// https://tests.telem-hit.net/JsGoogle without a config change or a hardcoded
/// environment check.
/// </summary>
public class GoogleCalendarOptions
{
    public const string SectionName = "GoogleCalendar";

    /// <summary>
    /// Default scope set.
    ///
    /// calendar.events is the one that matters and the only one this phase
    /// exists for. openid + email are added because they are how the connected
    /// Google account address is obtained: they make Google return an id_token
    /// alongside the access token, so the address is read straight out of the
    /// token response with NO extra API call and NO extra Calendar permission.
    /// Both are non-sensitive, are already granted to this same OAuth client by
    /// the Google login flow, and require no consent-screen change.
    ///
    /// Dropping them (by overriding Scopes in appsettings) is supported: the
    /// connection still works, the UI just shows "מחובר" without an address.
    /// </summary>
    public const string DefaultScopes =
        "openid email https://www.googleapis.com/auth/calendar.events";

    /// <summary>
    /// Path of the Calendar OAuth callback. Must match the Calendar redirect
    /// URIs registered in Google Cloud. NOT the login flow's /signin-google.
    /// </summary>
    public string RedirectPath { get; set; } = "/api/google-calendar/callback";

    /// <summary>Space-separated scope list requested at consent time.</summary>
    public string Scopes { get; set; } = DefaultScopes;

    /// <summary>App-relative page the callback returns the user to.</summary>
    public string ReturnPath { get; set; } = "/settings";

    /// <summary>How long an issued OAuth state stays usable.</summary>
    public int StateLifetimeMinutes { get; set; } = 10;

    /// <summary>
    /// Refresh an access token this many seconds BEFORE it actually expires, so
    /// a token handed out here cannot expire mid-request further down the call.
    /// </summary>
    public int RefreshSkewSeconds { get; set; } = 120;
}
