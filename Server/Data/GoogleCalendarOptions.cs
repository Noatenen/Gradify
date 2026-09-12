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
    /// Master switch for the ENTIRE external Google Calendar integration.
    ///
    /// Default true, so development and every existing deployment keep the
    /// behaviour they have today. Production sets it to false because the
    /// public deployment's OAuth consent screen no longer declares the
    /// sensitive scope https://www.googleapis.com/auth/calendar.events, and an
    /// affordance that can only end on a Google error page is worse than no
    /// affordance at all.
    ///
    /// ── WHAT IT DOES AND DOES NOT TOUCH ──────────────────────────────────────
    /// It gates ONLY this integration: the consent request, the callback, the
    /// task-scheduling endpoints and the UI that offers them. It is deliberately
    /// NOT a credential switch. Google LOGIN reads the very same client from
    /// Authentication:Google:ClientId/ClientSecret, so disabling the calendar by
    /// clearing those would take login down with it — which is exactly why this
    /// flag exists separately from <c>IsConfigured</c>.
    ///
    /// Motiva's OWN mentor and lecturer calendars are not affected in any way.
    /// They are built from Motiva data (dashboard rows + PersonalTasks) and have
    /// never called Google; Google only ever added a "synced" indicator on top.
    ///
    /// Nothing is dropped when it is off. Stored connections, refresh tokens and
    /// GoogleCalendarEventLinks rows all stay exactly where they are, so flipping
    /// it back to true restores the feature with no migration and no data loss.
    /// </summary>
    public bool Enabled { get; set; } = true;

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
