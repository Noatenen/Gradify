using System.Net;
using System.Text;

namespace AuthWithAdmin.Server.Data;

// ─────────────────────────────────────────────────────────────────────────────
//  GoogleDriveLinkValidator — pure helper for the Drive-link submission flow.
//
//  Two-step validation:
//    1. Format   — absolute HTTPS, host ∈ {drive.google.com, docs.google.com},
//                  no embedded userinfo, ≤ 2048 chars.
//    2. Reachable — auto-redirect DISABLED, headers + a small chunk of body
//                  inspected. We accept the link only if the response page
//                  is the actual Drive viewer/folder/doc shell, not one of
//                  Google's "Request access" / "Sign in" interstitials.
//
//  Why we sniff a piece of the body:
//    Google returns HTTP 200 for private Drive folders and documents — the
//    HTML body is the only differentiator between the public viewer and the
//    "Request access" page. We read at most 32 KB of HTML, never the file
//    itself, look for known deny markers, and reject on any match.
//
//  SSRF defence: the format step already enforces host ∈ {drive,docs}.google.com.
//  We allow exactly ONE in-flight redirect, and only if it stays on those
//  same hosts. Anything else → not shared.
// ─────────────────────────────────────────────────────────────────────────────

public static class GoogleDriveLinkValidator
{
    /// <summary>Canonical Hebrew error shown to the student when the link is
    /// rejected at any stage of the reachability check.</summary>
    public const string NotSharedMessage =
        "הקישור אינו פתוח לצפייה. יש לשתף את הקובץ/התיקייה בדרייב כך שלכל מי שיש את הקישור תהיה גישה.";

    private static readonly HashSet<string> AllowedHosts =
        new(StringComparer.OrdinalIgnoreCase) { "drive.google.com", "docs.google.com" };

    /// <summary>
    /// Body fingerprints that indicate a Drive interstitial — request-access
    /// page or sign-in challenge. All values are lowercase and compared
    /// case-insensitively against the (lowercased) response body slice.
    /// We use a multi-signal approach: any match is enough to reject.
    /// </summary>
    private static readonly string[] DenyMarkers =
    {
        // English Drive "Request access" page
        "you need access",
        "request access to this",
        "request access</",                 // button label
        "why is this happening",
        // Sign-in challenge wrappers
        "<title>sign in",
        "signin/v2/identifier",
        "/serviceloginauth",
        "accounts.google.com/servicelogin",
        "accountchooser?continue",
        // Hebrew variants of the access-denied / sign-in pages
        "אין לך הרשאה",
        "בקשת גישה",
        "בקש גישה",
        "אין לך גישה",
        "התחברות לחשבון",
    };

    // ── Single static HttpClient with redirects DISABLED. ─────────────────
    // We don't want to follow Google's 302 → accounts.google.com handoff;
    // we want to *detect* it.
    private static readonly HttpClient _probeClient = BuildProbeClient();

    private static HttpClient BuildProbeClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect            = false,
            ConnectTimeout               = TimeSpan.FromSeconds(4),
            PooledConnectionLifetime     = TimeSpan.FromMinutes(5),
            AutomaticDecompression       = DecompressionMethods.All,
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
    }

    /// <summary>Format check only — does not hit the network.</summary>
    /// <returns>Null on success; otherwise a Hebrew error message.</returns>
    public static string? ValidateFormat(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "יש להזין קישור Google Drive";

        var trimmed = raw.Trim();
        if (trimmed.Length > 2048)
            return "הקישור ארוך מדי";

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return "הקישור אינו תקין";

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return "יש להזין קישור HTTPS בלבד";

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return "הקישור אינו תקין";

        if (!AllowedHosts.Contains(uri.Host))
            return "יש להזין קישור של Google Drive בלבד";

        return null;
    }

    /// <summary>
    /// Reachability + accessibility probe. Returns null when the link is
    /// publicly shared, otherwise <see cref="NotSharedMessage"/>.
    /// The <paramref name="factory"/> param is kept for API compatibility
    /// but the probe uses the static handler-backed client above so we can
    /// disable auto-redirect (which the factory's default client doesn't).
    /// </summary>
    public static async Task<string?> IsReachableAsync(
        string url,
        IHttpClientFactory factory,
        CancellationToken cancellationToken = default)
        => await ProbeAsync(url, hop: 0, cancellationToken);

    private static async Task<string?> ProbeAsync(
        string url, int hop, CancellationToken cancellationToken)
    {
        if (hop > 1) return NotSharedMessage; // at most one redirect hop

        HttpResponseMessage? resp = null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            // Browser-like UA — empty-UA clients sometimes get auth-walled
            // even when the file is fully public.
            req.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Gradify-Probe) AppleWebKit/537.36");
            req.Headers.AcceptLanguage.ParseAdd("en;q=0.9, he;q=0.8");

            resp = await _probeClient.SendAsync(
                req,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            int statusCode = (int)resp.StatusCode;

            // 3xx → inspect Location header.
            if (statusCode >= 300 && statusCode < 400)
            {
                var loc = resp.Headers.Location;
                if (loc is null) return NotSharedMessage;

                Uri? abs = loc.IsAbsoluteUri
                    ? loc
                    : Uri.TryCreate(new Uri(url), loc, out var built) ? built : null;

                if (abs is null || !AllowedHosts.Contains(abs.Host))
                    return NotSharedMessage;

                // One-hop follow within Drive/Docs hosts only.
                return await ProbeAsync(abs.AbsoluteUri, hop + 1, cancellationToken);
            }

            // Anything other than 2xx → not shared.
            if (statusCode < 200 || statusCode >= 300) return NotSharedMessage;

            // 2xx → sniff a chunk of the body to differentiate the public
            // viewer from Google's interstitial pages. Google's
            // request-access page is served as 200 OK with HTML.
            const int snippetBytes = 32 * 1024;
            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[snippetBytes];
            int total = 0;
            while (total < buffer.Length)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(total, buffer.Length - total),
                    cancellationToken);
                if (read == 0) break;
                total += read;
            }

            if (total == 0) return NotSharedMessage; // empty body = suspicious

            string snippet = Encoding.UTF8.GetString(buffer, 0, total).ToLowerInvariant();
            foreach (var marker in DenyMarkers)
            {
                if (snippet.Contains(marker, StringComparison.Ordinal))
                    return NotSharedMessage;
            }
            return null;
        }
        catch (OperationCanceledException)
        {
            return NotSharedMessage; // timeout
        }
        catch
        {
            return NotSharedMessage; // DNS / TLS / network failure
        }
        finally
        {
            resp?.Dispose();
        }
    }
}