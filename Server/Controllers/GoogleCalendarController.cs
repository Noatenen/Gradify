using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AuthWithAdmin.Server.Controllers;

/// <summary>
/// Per-user Google Calendar connection (OAuth 2.0 authorization-code flow).
///
/// COMPLETELY SEPARATE from Google LOGIN. The login flow is
/// UsersController.Google -> /api/users/signin-google/{page} on the framework's
/// Google handler, and nothing here touches it: different route, different
/// redirect URI, different scopes, its own state, its own storage. Signing in
/// with Google does not connect a calendar, connecting a calendar does not sign
/// anyone in, and disconnecting a calendar cannot end a Motiva session.
///
/// Flow:
///   1. GET  /api/google-calendar/connect-url  — authenticated user gets a URL.
///   2. Client navigates there; user consents in Google.
///   3. Google redirects to GET /api/google-calendar/callback (public).
///   4. Callback validates state, exchanges the code, stores the connection,
///      and redirects back to the Motiva settings page.
///   5. GET    /api/google-calendar/status     — UI reads the real state.
///   6. DELETE /api/google-calendar/disconnect — revokes + retires the grant.
///
/// The callback URI is composed per request from Scheme + Host + PathBase, so
/// it resolves to https://localhost:7275/api/google-calendar/callback locally
/// and https://tests.telem-hit.net/JsGoogle/api/google-calendar/callback on the
/// test host, from the same build and the same config. No host is hardcoded.
/// </summary>
[Route("api/google-calendar")]
[ApiController]
[Authorize]
public class GoogleCalendarController : ControllerBase
{
    // Query flags the settings page reads. Deliberately coarse: the user is
    // told what to do next, never what went wrong internally, and no token,
    // code or exception text is ever put in a URL.
    private const string ResultConnected = "gcalConnected=true";
    private const string ErrorDenied     = "gcalError=denied";       // user said no
    private const string ErrorState      = "gcalError=state";        // bad/expired/replayed state
    private const string ErrorToken      = "gcalError=token";        // exchange failed
    private const string ErrorNoRefresh  = "gcalError=norefresh";    // no long-lived credential
    private const string ErrorConfig     = "gcalError=config";       // client id/secret missing

    private readonly GoogleCalendarTokenService _tokens;
    private readonly OAuthStateService          _states;
    private readonly GoogleCalendarOptions      _options;

    public GoogleCalendarController(
        GoogleCalendarTokenService      tokens,
        OAuthStateService               states,
        IOptions<GoogleCalendarOptions> options)
    {
        _tokens  = tokens;
        _states  = states;
        _options = options.Value;
    }

    // GET /api/google-calendar/status
    [HttpGet("status")]
    [ServiceFilter(typeof(AuthCheck))]
    public async Task<IActionResult> GetStatus(int authUserId)
        => Ok(await _tokens.GetStatusAsync(authUserId));

    // GET /api/google-calendar/connect-url
    [HttpGet("connect-url")]
    [ServiceFilter(typeof(AuthCheck))]
    public async Task<IActionResult> GetConnectUrl(int authUserId)
    {
        if (!_tokens.IsConfigured)
            return StatusCode(503, "Google Calendar integration is not configured.");

        // The state is minted here, bound to the authenticated caller. The user
        // id itself never leaves the server.
        var state = await _states.IssueAsync(
            OAuthStateService.GoogleCalendarProvider, authUserId, _options.StateLifetimeMinutes);

        return Ok(new GoogleCalendarConnectUrlDto
        {
            Url = _tokens.BuildAuthorizationUrl(state, BuildCallbackUri()),
        });
    }

    // GET /api/google-calendar/callback
    //
    // Anonymous by necessity: Google redirects the BROWSER here, and that
    // navigation carries no Authorization header. Identity comes from the
    // single-use state instead, which is why the state has to be unguessable.
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error)
    {
        // Consent denied (error=access_denied) or Google sent nothing usable.
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            return ReturnToSettings(ErrorDenied);

        // Validate + consume BEFORE anything else, so a forged or replayed
        // callback never reaches the token endpoint at all.
        var userId = await _states.ConsumeAsync(OAuthStateService.GoogleCalendarProvider, state);
        if (userId is null)
            return ReturnToSettings(ErrorState);

        // Same URI as the authorization request — Google rejects the exchange
        // otherwise, which is also why it is derived rather than configured.
        var outcome = await _tokens.CompleteConnectionAsync(userId.Value, code, BuildCallbackUri());

        return outcome switch
        {
            GoogleCalendarTokenService.ConnectOutcome.Success             => ReturnToSettings(ResultConnected),
            GoogleCalendarTokenService.ConnectOutcome.MissingRefreshToken => ReturnToSettings(ErrorNoRefresh),
            GoogleCalendarTokenService.ConnectOutcome.NotConfigured       => ReturnToSettings(ErrorConfig),
            _                                                            => ReturnToSettings(ErrorToken),
        };
    }

    // DELETE /api/google-calendar/disconnect
    [HttpDelete("disconnect")]
    [ServiceFilter(typeof(AuthCheck))]
    public async Task<IActionResult> Disconnect(int authUserId)
    {
        // Succeeds even when Google's revoke call fails — the local credential
        // is cleared first, so there is nothing left for Motiva to misuse. See
        // GoogleCalendarTokenService.DisconnectAsync.
        await _tokens.DisconnectAsync(authUserId);
        return NoContent();
    }

    // ── URL composition ───────────────────────────────────────────────────────
    //
    // PathBase is what makes the deployed /JsGoogle prefix survive. It is empty
    // locally and "/JsGoogle" behind the test host's virtual directory, and it
    // is the same pattern UsersController.getPath and AdminController already
    // use for their absolute links.

    private string Origin() => $"{Request.Scheme}://{Request.Host}{Request.PathBase}";

    private string BuildCallbackUri() => $"{Origin()}{_options.RedirectPath}";

    private RedirectResult ReturnToSettings(string queryFlag) =>
        Redirect($"{Origin()}{_options.ReturnPath}?{queryFlag}");
}
