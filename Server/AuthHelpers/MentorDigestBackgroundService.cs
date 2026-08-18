using AuthWithAdmin.Server.Data;

namespace AuthWithAdmin.Server.AuthHelpers;

// ─────────────────────────────────────────────────────────────────────────────
//  MentorDigestBackgroundService — fires the daily mentor digest.
//
//  WHY NOT Delay(24h), THE PATTERN TokenCleanupBackgroundService USES
//  A rolling 24-hour timer fires 24 hours after PROCESS START. That is fine for
//  token cleanup, which only cares that it happens daily, and wrong for a
//  digest: every restart shifts the send time, so a "morning summary" drifts
//  into the afternoon and then into the night. This service instead computes
//  the next wall-clock occurrence of the configured hour and sleeps to it, so a
//  restart at any time simply re-targets the same hour.
//
//  THE HOUR IS CONFIGURATION, NOT A CONSTANT
//  MentorDigest:SendAtLocalTime in appsettings.json, "HH:mm" in ISRAEL local
//  time. The default lives in configuration where it can be changed without a
//  rebuild; the service only knows how to read and schedule it.
//
//  OFF UNLESS SOMEONE SAID ON — MentorDigest:Enabled DEFAULTS TO FALSE
//  This used to default to true, which meant the ONLY thing standing between a
//  freshly started server and real mail leaving the building was a config file
//  that is git-ignored. A clone, a fresh container or a colleague's checkout got
//  no appsettings.json at all, so it inherited "enabled" plus an EMPTY
//  Email:NonDeliverableDomains list — the one combination where the demo cohort
//  is mailed for real. Defaulting to false inverts that: the unconfigured state
//  is the silent one, and sending is something a deployment opts into.
//
//  Turning the scheduler off never disables the FEATURE. The admin manual
//  trigger (POST /api/mentor/digest/run) is unaffected, which is what a
//  development or demo environment actually wants.
// ─────────────────────────────────────────────────────────────────────────────
public class MentorDigestBackgroundService : BackgroundService
{
    private const string EnabledKey  = "MentorDigest:Enabled";
    private const string SendAtKey   = "MentorDigest:SendAtLocalTime";

    /// <summary>Used only when configuration is missing or unparseable, so a
    /// deployment that forgot the key still gets a sane morning digest instead
    /// of silence. Configuration always wins.</summary>
    private static readonly TimeSpan FallbackSendAt = new(7, 0, 0);

    private readonly IServiceProvider _services;
    private readonly IConfiguration   _config;
    private readonly ILogger<MentorDigestBackgroundService> _log;

    public MentorDigestBackgroundService(
        IServiceProvider services,
        IConfiguration config,
        ILogger<MentorDigestBackgroundService> log)
    {
        _services = services;
        _config   = config;
        _log      = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Default FALSE: an absent or unreadable setting must never mean "mail
        // everyone at 07:00". See the header.
        if (!_config.GetValue(EnabledKey, false))
        {
            _log.LogInformation(
                "Mentor daily digest scheduler is OFF — set {Key}=true to enable " +
                "scheduled sending. The admin manual trigger is unaffected.", EnabledKey);
            return;
        }

        _log.LogInformation(
            "Mentor daily digest scheduler started — {SendAt} {Zone}",
            SendAtLocalTime, IsraelTime.ZoneDisplayName);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Re-read the setting every cycle so changing the hour does not
            // require a restart on a host that reloads configuration.
            var nextUtc = IsraelTime.NextOccurrenceUtc(SendAtLocalTime, DateTime.UtcNow);
            var wait    = nextUtc - DateTime.UtcNow;
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;

            _log.LogInformation(
                "Next mentor digest run at {NextUtc:u} (in {Hours:0.0}h)",
                nextUtc, wait.TotalHours);

            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;   // shutdown
            }

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Never let one bad run kill the loop — tomorrow's digest must
                // still fire. The MentorDigestRuns ledger means a retry today
                // cannot double-send, so simply falling through to the next
                // scheduled occurrence is safe.
                _log.LogError(ex, "Mentor daily digest run failed");
            }
        }

        _log.LogInformation("Mentor daily digest scheduler stopped");
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        // A fresh scope per run: DbRepository is scoped and holds one
        // SqliteConnection, so it must never be resolved from the root provider
        // or shared across runs.
        using var scope = _services.CreateScope();
        var digests = scope.ServiceProvider.GetRequiredService<MentorDigestService>();

        var results = await digests.SendForAllMentorsAsync(MentorDigestService.Triggers.Scheduled);

        int sent = results.Count(r => r.Sent);
        _log.LogInformation(
            "Mentor daily digest: {Sent} sent, {Skipped} skipped, {Mentors} mentors evaluated",
            sent, results.Count - sent, results.Count);

        _ = ct;
    }

    /// <summary>Parses "HH:mm" from configuration. An unparseable value logs and
    /// falls back rather than crashing the host — a typo in appsettings should
    /// cost the configured hour, not the whole application.</summary>
    private TimeSpan SendAtLocalTime
    {
        get
        {
            var raw = _config.GetValue<string>(SendAtKey);
            if (string.IsNullOrWhiteSpace(raw)) return FallbackSendAt;

            if (TimeSpan.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                && parsed >= TimeSpan.Zero && parsed < TimeSpan.FromDays(1))
                return parsed;

            _log.LogWarning(
                "{Key} = '{Raw}' is not a valid HH:mm time — falling back to {Fallback}",
                SendAtKey, raw, FallbackSendAt);
            return FallbackSendAt;
        }
    }
}
