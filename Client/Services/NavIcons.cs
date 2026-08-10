namespace AuthWithAdmin.Client.Services;

/// <summary>
/// The student rail's navigation glyphs, as SVG path data.
///
/// ONE FAMILY, BY CONSTRUCTION
/// Every icon here is drawn on the same 24x24 grid, as open paths with no
/// fills, and is rendered by AppSideNav at one size with one stroke width and
/// one join/cap style. Nothing per-icon can drift: the size, the weight and
/// the colour all live on the single &lt;svg&gt; the rail emits, not on the
/// data below.
///
/// WHY NOT OPEN-ICONIC
/// The rest of the app uses the open-iconic font, and Mentor / Lecturer /
/// Admin still do. It is the wrong tool for this rail on two counts: it is a
/// FILLED family, so it cannot sit beside the stroked line art the Motiva
/// student surface uses everywhere else (the profile cog, the project caret,
/// the resource and deliverable icons), and its glyphs are drawn at visibly
/// different optical weights. "oi-dashboard" is also simply the wrong picture
/// — it is a speedometer dial, which reads as a metric or a gauge, not as the
/// student's home screen.
///
/// Each icon is the conventional shape for its destination, so a student who
/// has used any other product recognises it without reading the label.
/// </summary>
public static class NavIcons
{
    /// <summary>Four panes — the universal "dashboard / overview" mark.
    /// Replaces oi-dashboard's speedometer.</summary>
    public static readonly IReadOnlyList<string> Dashboard = new[]
    {
        "M3 3h7v9H3z",
        "M14 3h7v5h-7z",
        "M14 12h7v9h-7z",
        "M3 16h7v5H3z",
    };

    /// <summary>Two ticked lines — a checklist. Reads as "my tasks" rather
    /// than as a generic document.</summary>
    public static readonly IReadOnlyList<string> Tasks = new[]
    {
        "m3 7 2 2 4-4",
        "m3 17 2 2 4-4",
        "M13 8h8",
        "M13 18h8",
    };

    /// <summary>A wall calendar with its two rings and header rule.</summary>
    public static readonly IReadOnlyList<string> Calendar = new[]
    {
        "M4 6a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v13a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z",
        "M16 2v4",
        "M8 2v4",
        "M4 10h16",
    };

    /// <summary>A speech bubble. The requests screen is a conversation with
    /// the staff — a thread per request — so the message mark is the honest
    /// picture, not an envelope.</summary>
    public static readonly IReadOnlyList<string> Requests = new[]
    {
        "M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z",
    };

    /// <summary>An open book — the knowledge centre.</summary>
    public static readonly IReadOnlyList<string> Knowledge = new[]
    {
        "M12 7v14",
        "M3 18a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h5a4 4 0 0 1 4 4 4 4 0 0 1 4-4h5a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1h-6a3 3 0 0 0-3 3 3 3 0 0 0-3-3z",
    };
}
