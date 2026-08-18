namespace AuthWithAdmin.Server.Data;

// ─────────────────────────────────────────────────────────────────────────────
//  EmailDeliveryPolicy — decides who an outgoing message may actually be
//  relayed to.
//
//  WHY THIS EXISTS
//  The demo cohort (users 61-73) carries deliberately fake addresses on the
//  motiva.local domain. Nothing in the application knew that, so a real send
//  was attempted against a domain that cannot exist — and SMTP made the failure
//  invisible: Gmail ACCEPTS the message for relay, SendMailAsync returns
//  cleanly, and the rejection arrives later as an asynchronous bounce to the
//  sending mailbox. No amount of error handling at the call site can catch
//  that, because at send time nothing has gone wrong yet. The only place to
//  stop it is BEFORE the relay, which is what this class does.
//
//  THE ADDRESS ITSELF IS NEVER REWRITTEN. users.Email stays the single
//  canonical contact field for every user (it is the only user-email column in
//  the schema, and the mentor profile screen reads exactly the same value).
//  This class decides whether that address is worth relaying to; it never
//  invents, derives or substitutes one in production.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class EmailDeliveryPolicy
{
    private const string DomainsKey  = "Email:NonDeliverableDomains";
    private const string RedirectKey = "Email:DevRedirectTo";

    private readonly IReadOnlyList<string> _nonDeliverable;
    private readonly string?               _devRedirectTo;
    private readonly bool                  _isDevelopment;

    public EmailDeliveryPolicy(IConfiguration config, IWebHostEnvironment env)
    {
        _isDevelopment = env.IsDevelopment();

        _nonDeliverable = config.GetSection(DomainsKey).Get<string[]>()
                          ?? Array.Empty<string>();

        // Read ONLY in Development. Reading it unconditionally and checking the
        // environment at the call site would leave one missed check between a
        // production deployment and every user's mail being redirected, so the
        // value simply does not exist outside Development.
        _devRedirectTo = _isDevelopment
            ? config.GetValue<string>(RedirectKey)?.Trim()
            : null;
    }

    /// <summary>True when the address sits on a domain configured as a test
    /// placeholder. Case-insensitive, and matched on the domain part only so a
    /// local-part that happens to contain the string cannot trip it.</summary>
    public bool IsNonDeliverable(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return true;

        int at = address.LastIndexOf('@');
        if (at < 0 || at == address.Length - 1) return true;   // not an address

        var domain = address[(at + 1)..].Trim();
        return _nonDeliverable.Any(d =>
            string.Equals(domain, d, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What should actually happen to one message's recipient list.</summary>
    /// <param name="Recipients">Addresses to relay. Empty means do not send.</param>
    /// <param name="Skipped">Addresses dropped as non-deliverable, for logging.</param>
    /// <param name="Redirected">True when Development redirected the message.</param>
    public sealed record Decision(
        List<string> Recipients,
        List<string> Skipped,
        bool         Redirected);

    public Decision Resolve(IEnumerable<string>? recipients)
    {
        var all = (recipients ?? Enumerable.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Development override. Deliberately redirects the WHOLE list —
        // including otherwise-deliverable addresses — because the point of a
        // dev inbox is that no real person is mailed while testing. Guarded by
        // _devRedirectTo being null outside Development, so this branch cannot
        // execute in production even if the key is present in a config file.
        if (!string.IsNullOrWhiteSpace(_devRedirectTo))
            return new Decision(new List<string> { _devRedirectTo! }, all, Redirected: true);

        var deliverable = all.Where(a => !IsNonDeliverable(a)).ToList();
        var skipped     = all.Where(IsNonDeliverable).ToList();

        return new Decision(deliverable, skipped, Redirected: false);
    }
}
