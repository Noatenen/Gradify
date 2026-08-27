namespace AuthWithAdmin.Client.Components;

/// <summary>
/// Hebrew day and month names, and the handful of date phrasings Motiva shows.
///
/// <para><b>Why this is not a CultureInfo.</b> Blazor WASM ships SHARDED ICU
/// data and picks a shard from the browser's language: a user whose Chrome runs
/// in English gets <c>icudt_EFIGS</c>, which contains no Hebrew, and
/// <c>new CultureInfo("he-IL")</c> throws there. In a static field that surfaces
/// as a TypeInitializationException on the first render that reads a label —
/// after loading finished — which is a failure the screen cannot recover from
/// and the user cannot act on. Hardcoding the names removes the globalization
/// payload entirely and renders identically in every locale.</para>
///
/// <para><b>Why it is shared.</b> The mentor calendar and the date picker inside
/// the task editor have to word a date the SAME way — a picker that says
/// "19 באוגוסט" while the grid it feeds says something else is two vocabularies
/// for one product. One copy, so they cannot drift.</para>
/// </summary>
public static class MotivaDates
{
    /// <summary>Sunday-first, which is the Hebrew calendar week.</summary>
    public static readonly string[] DayNames =
        { "ראשון", "שני", "שלישי", "רביעי", "חמישי", "שישי", "שבת" };

    /// <summary>Column headings, where a full weekday name never fits.</summary>
    public static readonly string[] ShortDayNames =
        { "א׳", "ב׳", "ג׳", "ד׳", "ה׳", "ו׳", "ש׳" };

    public static readonly string[] MonthNames =
    {
        "ינואר", "פברואר", "מרץ", "אפריל", "מאי", "יוני",
        "יולי", "אוגוסט", "ספטמבר", "אוקטובר", "נובמבר", "דצמבר",
    };

    /// <summary>Abbreviated months, for the places a full name cannot fit — the
    /// mentor home's 44px date chip is the first. Same hardcoding rationale as
    /// <see cref="MonthNames"/>: no CultureInfo, no ICU shard to be missing.</summary>
    public static readonly string[] ShortMonthNames =
    {
        "ינו׳", "פבר׳", "מרץ", "אפר׳", "מאי", "יוני",
        "יולי", "אוג׳", "ספט׳", "אוק׳", "נוב׳", "דצמ׳",
    };

    /// <summary>"שני".</summary>
    public static string DayName(DateTime d) => DayNames[(int)d.DayOfWeek];

    /// <summary>"אוג׳".</summary>
    public static string ShortMonth(DateTime d) => ShortMonthNames[d.Month - 1];

    /// <summary>"17 באוגוסט".</summary>
    public static string DayAndMonth(DateTime d) => $"{d.Day} ב{MonthNames[d.Month - 1]}";

    /// <summary>"אוגוסט 2026" — a month heading.</summary>
    public static string MonthAndYear(DateTime d) => $"{MonthNames[d.Month - 1]} {d.Year}";

    /// <summary>"שני, 17 באוגוסט 2026".</summary>
    public static string FullDate(DateTime d) => $"{DayName(d)}, {DayAndMonth(d)} {d.Year}";

    /// <summary>
    /// "HH:mm" as the API stores it, or null.
    ///
    /// <para>Invariant-parsed on purpose, for the same ICU reason as the names
    /// above: the stored format is fixed and the server normalizes it on every
    /// write, so culture must not enter into reading it back.</para>
    /// </summary>
    public static TimeSpan? ParseWallClock(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && TimeSpan.TryParseExact(value.Trim(), new[] { @"hh\:mm", @"hh\:mm\:ss" },
                                  System.Globalization.CultureInfo.InvariantCulture, out var t)
            ? t
            : null;

    /// <summary>The one way a wall-clock time is written back out.</summary>
    public static string FormatWallClock(TimeSpan t) => $"{t.Hours:D2}:{t.Minutes:D2}";
}
