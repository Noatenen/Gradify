using System;
using System.Threading;

namespace AuthWithAdmin.Client.Services;

// ─────────────────────────────────────────────────────────────────────────────
//  TimeFormat — Asia/Jerusalem display helper for client-side rendering.
//
//  Why this exists
//    Every datetime column on the server is written by `datetime('now')` in
//    SQLite, which returns UTC. Dapper reads those values into C# DateTime
//    with Kind=Unspecified, and System.Text.Json then serialises them
//    without a Z marker. The Blazor client receives Kind=Unspecified and
//    rendered them verbatim — so an Israeli user saw their own submission
//    timestamped at 11:38 instead of 14:38 (UTC vs IDT, 3-hour gap).
//
//  Strategy
//    Treat every DateTime that comes from the server as UTC and convert
//    explicitly to Asia/Jerusalem before formatting. This is correct for
//    every existing row (legacy + new) because they were all written by
//    `datetime('now')` regardless of their Kind on the wire.
//
//  Notes for DST
//    Asia/Jerusalem switches IST (UTC+02:00) ↔ IDT (UTC+03:00) twice a year.
//    The runtime's tz database handles the boundary automatically when we
//    call ConvertTimeFromUtc. Blazor WASM ships full ICU/tz data so the
//    lookup works in the browser too.
//
//  Fallback
//    If `Asia/Jerusalem` isn't found (paranoid case — corrupted runtime),
//    we fall through to a fixed UTC+02:00 zone so the UI still renders a
//    plausible value rather than blowing up.
// ─────────────────────────────────────────────────────────────────────────────

public static class TimeFormat
{
    private static readonly Lazy<TimeZoneInfo> _israelTz =
        new(ResolveIsraelTz, LazyThreadSafetyMode.PublicationOnly);

    public static TimeZoneInfo IsraelTimeZone => _israelTz.Value;

    private static TimeZoneInfo ResolveIsraelTz()
    {
        // IANA id — works on .NET 6+ across Linux/macOS/Windows AND Blazor WASM.
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem"); }
        catch { /* fall through */ }
        // Legacy Windows id, just in case.
        try { return TimeZoneInfo.FindSystemTimeZoneById("Israel Standard Time"); }
        catch { /* fall through */ }
        // Last-resort static offset. No DST awareness — but better than a
        // hard crash if the tz database is unavailable for some reason.
        return TimeZoneInfo.CreateCustomTimeZone(
            id:                  "Israel-Fallback",
            baseUtcOffset:       TimeSpan.FromHours(2),
            displayName:         "(UTC+02:00) Jerusalem (fallback)",
            standardDisplayName: "IST");
    }

    // ── Core conversion ────────────────────────────────────────────────────

    /// <summary>Treats the input as UTC (Kind.Unspecified is interpreted as
    /// UTC since server timestamps from SQLite arrive with Unspecified
    /// Kind), then converts to Asia/Jerusalem wall-clock time.</summary>
    public static DateTime AsIsrael(this DateTime dt)
    {
        var utc = dt.Kind switch
        {
            DateTimeKind.Local => dt.ToUniversalTime(),
            DateTimeKind.Utc   => dt,
            _                  => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, IsraelTimeZone);
    }

    public static DateTime? AsIsrael(this DateTime? dt) =>
        dt.HasValue ? dt.Value.AsIsrael() : null;

    // ── Formatting (the convenient call site) ──────────────────────────────

    /// <summary>Formats a UTC DateTime in Israel local time. Default format
    /// includes a time component — this helper is intended for timestamps,
    /// not date-only fields like DueDate.</summary>
    public static string IsraelFormat(this DateTime dt, string format = "dd/MM/yyyy HH:mm")
        => dt.AsIsrael().ToString(format);

    public static string IsraelFormat(
        this DateTime? dt,
        string format = "dd/MM/yyyy HH:mm",
        string fallback = "—")
        => dt.HasValue ? dt.Value.IsraelFormat(format) : fallback;
}