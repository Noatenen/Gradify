namespace AuthWithAdmin.Server.Data;

// ─────────────────────────────────────────────────────────────────────────────
//  IsraelTime — the ONE place the server decides what "today" means.
//
//  WHY THIS EXISTS
//  Waiting age is measured in CALENDAR DAYS, not elapsed 24-hour periods, and
//  the calendar in question is Israel's. Before this file the codebase had
//  three different answers for the same question:
//
//    MentorTime.DaysSince (client)  calendar days, browser-local
//    pending-mentor-approvals (SQL) CAST(julianday('now') - julianday(x))
//                                   → elapsed 24h periods, UTC
//    MentorController overdue       datetime('now') in SQL, DateTime.UtcNow in C#
//
//  A submission that arrived at 23:00 yesterday was 1 day old to the mentor and
//  0 days old to the lecturer. Everything server-side now routes through here,
//  so the same item gets the same WaitingDays no matter where the process runs.
//
//  DEPLOYMENT INDEPENDENCE
//  Nothing here reads DateTime.Now or the machine's local timezone. The zone is
//  resolved explicitly, so a container in UTC and a laptop in Asia/Jerusalem
//  produce identical numbers.
// ─────────────────────────────────────────────────────────────────────────────
public static class IsraelTime
{
    /// <summary>IANA id — correct on Linux/macOS and, since .NET 6, on Windows
    /// too whenever ICU is available.</summary>
    private const string IanaId = "Asia/Jerusalem";

    /// <summary>Windows registry id, for a host running in globalization-invariant
    /// mode where the IANA lookup fails.</summary>
    private const string WindowsId = "Israel Standard Time";

    private static readonly TimeZoneInfo Zone = Resolve();

    /// <summary>The zone actually in use. Logged once at startup so a
    /// misconfigured host is visible instead of silently producing UTC dates.</summary>
    public static string ZoneDisplayName => Zone.Id;

    /// <summary>
    /// The IANA id to hand to external systems that speak wall-clock time —
    /// Google Calendar's <c>start.timeZone</c> / <c>end.timeZone</c>, for one.
    ///
    /// <para>Deliberately the CONSTANT, not <see cref="ZoneDisplayName"/>: on a
    /// host that fell back to the Windows id or to the fixed-offset zone, that
    /// property returns a name Google would reject. Motiva has no per-user
    /// timezone model, and the product is an Israeli college programme, so
    /// Asia/Jerusalem is the answer for every user — and sending the IANA id
    /// rather than a UTC instant is what makes Google handle DST for us.</para>
    /// </summary>
    public static string IanaZoneId => IanaId;

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { IanaId, WindowsId })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException)  { }
            catch (InvalidTimeZoneException)   { }
        }

        // Last resort: a fixed +02:00 offset. This is Israel Standard Time
        // WITHOUT daylight saving, so between late March and late October it is
        // one hour behind the real local clock. That shifts a calendar-day
        // boundary by an hour — it does not corrupt anything — and it only ever
        // applies on a host with no timezone database at all. Preferring it over
        // throwing keeps the mentor pages working on such a host.
        return TimeZoneInfo.CreateCustomTimeZone(
            "Israel (fixed +02:00 fallback)", TimeSpan.FromHours(2),
            "Israel (fixed +02:00 fallback)", "Israel (fixed +02:00 fallback)");
    }

    /// <summary>Current Israel wall-clock time.</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    /// <summary>Today's Israel calendar date, at midnight.</summary>
    public static DateTime Today => Now.Date;

    /// <summary>
    /// Converts a timestamp READ FROM SQLITE into Israel wall-clock time.
    ///
    /// <para>Every timestamp this app writes uses <c>datetime('now')</c>, which
    /// SQLite evaluates in UTC. Dapper hands those back as
    /// <see cref="DateTimeKind.Unspecified"/>, so the Kind has to be asserted
    /// before converting — otherwise ConvertTimeFromUtc throws on a value it
    /// thinks might already be local.</para>
    /// </summary>
    public static DateTime FromDbUtc(DateTime stored)
    {
        var utc = stored.Kind switch
        {
            DateTimeKind.Utc         => stored,
            DateTimeKind.Local       => stored.ToUniversalTime(),
            _                        => DateTime.SpecifyKind(stored, DateTimeKind.Utc),
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
    }

    /// <summary>
    /// Whole Israel calendar days between a stored UTC timestamp and today.
    ///
    /// <para>Calendar days, not elapsed hours: something that arrived at 23:00
    /// last night is 1 day old this morning, which is how a person reads it.
    /// Floored at 0 so a future timestamp (clock skew, a seeded fixture) reads
    /// as "today" rather than as a negative age.</para>
    /// </summary>
    public static int CalendarDaysSince(DateTime storedUtc) =>
        Math.Max(0, (Today - FromDbUtc(storedUtc).Date).Days);

    /// <summary>
    /// Whole Israel calendar days between today and a DATE-ONLY value such as
    /// PersonalTasks.DueDate.
    ///
    /// <para>Deliberately does NOT timezone-convert. A due date is a date the
    /// user picked, not an instant, and shifting it by an offset is how a task
    /// due "today" starts rendering as due yesterday. Negative = overdue.</para>
    /// </summary>
    public static int DaysUntilDate(DateTime storedDate) =>
        (storedDate.Date - Today).Days;

    /// <summary>
    /// The next UTC instant at which the given Israel wall-clock time occurs.
    ///
    /// <para>Used by the digest scheduler instead of a <c>Delay(24h)</c> loop: a
    /// rolling 24-hour timer fires 24 hours after PROCESS START, so every
    /// restart drifts the send time and a "morning digest" slowly becomes an
    /// afternoon one. Anchoring to the wall clock means a restart re-targets the
    /// same hour tomorrow.</para>
    ///
    /// <para>DST is handled by construction: the target is built in local terms
    /// and converted back, so the hour stays put across a transition. If the
    /// requested time falls in the skipped hour of a spring-forward, the
    /// conversion lands on the following valid instant rather than throwing.</para>
    /// </summary>
    public static DateTime NextOccurrenceUtc(TimeSpan localTimeOfDay, DateTime fromUtc)
    {
        var localNow    = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, Zone);
        var candidate   = localNow.Date + localTimeOfDay;
        if (candidate <= localNow) candidate = candidate.AddDays(1);

        // A spring-forward skips 02:00–03:00; a target inside that window does
        // not exist on that date. Step forward until it does.
        for (int guard = 0; Zone.IsInvalidTime(candidate) && guard < 4; guard++)
            candidate = candidate.AddHours(1);

        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), Zone);
    }
}
