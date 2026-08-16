using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Components;

namespace AuthWithAdmin.Client.Services;

/// <summary>
/// Decides whether a post-login destination may be honoured.
///
/// <para><b>Why this exists.</b> Until now the login flow discarded the
/// requested URL entirely — <c>GetRedirectTarget</c> returned a role default and
/// nothing carried the intent through. A mentor clicking a link in the 07:00
/// daily digest with an expired session landed on /dashboard/mentor and had to
/// find the item by hand, which is exactly the friction the digest exists to
/// remove.</para>
///
/// <para><b>An email link is the highest-value phishing pivot there is</b>, so
/// this is written as a whitelist of shapes rather than a blacklist of attacks:
/// a value is rejected unless it is unmistakably a path inside this
/// application. Two independent gates apply, and BOTH must pass —
/// <see cref="IsLocal"/> for security, <see cref="IsReachableBy"/> so a
/// mentor is never redirected into a page their role will bounce them out
/// of.</para>
/// </summary>
public static class ReturnUrlPolicy
{
    /// <summary>Query-string key. Stated once so the writer and the reader
    /// cannot disagree about its spelling.</summary>
    public const string QueryKey = "returnUrl";

    /// <summary>Auth pages are never a useful destination — capturing one would
    /// bounce the user back to login after logging in.</summary>
    private static readonly string[] AuthRoutes =
    {
        PageRoutes.Login, PageRoutes.Signup, PageRoutes.Pending,
        PageRoutes.VerifyEmail, PageRoutes.ForgetPassword,
        PageRoutes.ResetPassword, PageRoutes.Redirect,
    };

    // ── Gate 1: is it a local path at all? ───────────────────────────────────

    /// <summary>
    /// True only for a path that cannot leave this origin.
    ///
    /// <para>The dangerous cases this rejects, in order of how often they are
    /// actually used against redirect parameters:</para>
    /// <list type="bullet">
    /// <item><c>https://evil.com</c> — an outright absolute URL.</item>
    /// <item><c>//evil.com</c> — protocol-relative. Browsers treat this as an
    /// absolute URL on the current scheme, and it is the classic bypass for a
    /// naive "must start with /" check.</item>
    /// <item><c>/\evil.com</c> and any backslash — browsers normalise <c>\</c>
    /// to <c>/</c>, so this is protocol-relative in disguise.</item>
    /// <item>Control characters and newlines, which some parsers strip before
    /// resolving, turning an inert string into a live URL.</item>
    /// </list>
    ///
    /// <para>The value is checked exactly as received, already decoded once by
    /// the query binder. It is deliberately NOT decoded again: a second decode
    /// would let <c>%252F</c> become a separator that the first decode did not
    /// produce, and would also corrupt a legitimate path containing an escaped
    /// slash.</para>
    /// </summary>
    public static bool IsLocal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        // Must be rooted. A bare "mentor/tasks" is ambiguous and not accepted.
        if (value[0] != '/') return false;

        // Protocol-relative, in both spellings.
        if (value.Length > 1 && (value[1] == '/' || value[1] == '\\')) return false;

        // No backslashes anywhere — there is no legitimate use in a route here,
        // and every browser folds them into forward slashes.
        if (value.Contains('\\')) return false;

        // Control characters, including CR/LF and tab.
        foreach (var c in value)
            if (char.IsControl(c)) return false;

        // NO Uri.TryCreate(..., UriKind.Absolute) CHECK HERE — it is wrong on
        // this runtime and silently breaks the feature. Under the WebAssembly
        // (Unix) runtime, "/mentor-requests?requestId=27" IS parsed as an
        // absolute URI, because a leading slash reads as an absolute file path.
        // Adding that check as a "belt and braces" guard made IsLocal reject
        // every legitimate destination, so a returnUrl was never carried and
        // login always fell back to the dashboard — the exact bug this class
        // exists to fix, reintroduced by the guard meant to harden it.
        //
        // It is also redundant. A value that reaches this line begins with a
        // single '/' followed by a non-slash, non-backslash character, and no
        // absolute URL can look like that: a scheme requires "scheme:" before
        // any slash, and a protocol-relative URL requires two leading slashes.
        // Both are already excluded above.
        return true;
    }

    // ── Gate 2: can this user actually use the destination? ──────────────────

    /// <summary>
    /// Whether the user's roles plausibly permit the destination.
    ///
    /// <para>Deliberately PERMISSIVE: each page's own <c>[Authorize]</c> is the
    /// real access boundary, and this only prevents a redirect that would
    /// certainly dead-end. Landing an unauthorised user on a page sends them to
    /// PendingPage — "אין לך אישור להשתמש במערכת" — which is alarming and wrong
    /// for someone who merely followed a stale link, so the two shells with
    /// unambiguous prefixes are checked here and everything else is allowed
    /// through.</para>
    ///
    /// <para>The prefixes mirror the attributes actually on those pages: every
    /// mentor route is <c>[Authorize(Roles = Mentor, Admin, Staff)]</c> and
    /// every management route is <c>[Authorize(Roles = Admin, Staff)]</c>.</para>
    /// </summary>
    public static bool IsReachableBy(string path, User? user)
    {
        // Compare on the path only — a query string never affects authorization.
        var p = path.Split('?')[0].Split('#')[0].TrimStart('/').ToLowerInvariant();

        if (p.StartsWith("management/") || p == "management" || p == "dashboard/lecturer")
            return RoleService.IsAdminOrStaff(user);

        if (p.StartsWith("mentor/") || p == "mentor-requests" || p == "dashboard/mentor")
            return RoleService.IsMentor(user) || RoleService.IsAdminOrStaff(user);

        // Student and shared routes: let the route decide.
        return true;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// The destination to use, or null when the caller should fall back to the
    /// role default. Null is returned for anything that fails either gate —
    /// there is no partial acceptance and no repair of a malformed value.
    /// </summary>
    public static string? Sanitize(string? value, User? user)
    {
        if (!IsLocal(value)) return null;
        if (!IsReachableBy(value!, user)) return null;

        // Never hand back an auth page: it would bounce straight back here.
        var first = value!.Split('?')[0].TrimStart('/');
        if (AuthRoutes.Any(r => string.Equals(first, r, StringComparison.OrdinalIgnoreCase)))
            return null;

        return value;
    }

    /// <summary>
    /// Builds "<c>Login?returnUrl=…</c>" for the page the user was refused.
    ///
    /// <para>Returns a bare login route when the current location is not worth
    /// preserving — the app root, or an auth page — so a normal "log in" never
    /// acquires a pointless parameter.</para>
    /// </summary>
    public static string LoginWithReturn(NavigationManager nav)
    {
        var relative = nav.ToBaseRelativePath(nav.Uri);   // e.g. "mentor-requests?requestId=27"

        if (string.IsNullOrWhiteSpace(relative)) return PageRoutes.Login;

        var first = relative.Split('?')[0].TrimStart('/');
        if (AuthRoutes.Any(r => string.Equals(first, r, StringComparison.OrdinalIgnoreCase)))
            return PageRoutes.Login;

        var target = "/" + relative;
        if (!IsLocal(target)) return PageRoutes.Login;

        return $"{PageRoutes.Login}?{QueryKey}={Uri.EscapeDataString(target)}";
    }
}
