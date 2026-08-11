using AuthWithAdmin.Client.Components;

namespace AuthWithAdmin.Client.Pages.Mentor;

// ─────────────────────────────────────────────────────────────────────────────
//  Vocabulary for יומן ותכנון — the mentor's cross-project planning surface.
//
//  The design reference (Motiva Mentor Calendar.dc.html) defines five entry
//  types. Four of them are backed by real data and are implemented here. The
//  fifth — meeting — is NOT: the design's own prototype hardcodes a MEETINGS
//  array because nothing in the schema stores a mentor-authored meeting, and
//  inventing one here would be exactly the fabricated data this build refuses
//  to ship. See MentorCalendarPage's header for the gap and what closing it
//  would take.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>What a calendar entry IS. Drives its colour, its glyph and its
/// filter chip — one enum, so those three can never disagree.</summary>
public enum MentorEventType
{
    /// <summary>A project milestone's due date.</summary>
    Milestone,

    /// <summary>A deliverable the team owes on a dated task.</summary>
    Submission,

    /// <summary>A submission that has arrived and is waiting on the mentor.</summary>
    Review,

    /// <summary>One of the mentor's own reminders.</summary>
    PersonalTask,
}

/// <summary>
/// One dated thing, already resolved for rendering.
///
/// <para><b>Every entry names its project.</b> ProjectTitle/TeamName are not
/// optional decoration — a mentor's calendar is meaningless if an item does not
/// say whose it is, which is the single largest difference between this screen
/// and the student's single-project planner. The only entries without a project
/// are the mentor's own personal tasks, which say so explicitly.</para>
/// </summary>
public sealed record MentorCalendarEvent(
    string Id,
    MentorEventType Type,
    DateTime Date,
    string Title,
    int? ProjectId,
    string? ProjectTitle,
    string? TeamName,
    string? Detail,
    string? Href)
{
    /// <summary>"פרויקט · צוות", or the personal-task marker. Never empty.</summary>
    public string Context =>
        ProjectId is null
            ? "משימה אישית"
            : string.Join(" · ", new[] { ProjectTitle, TeamName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

    public bool IsPast => Date.Date < DateTime.Now.Date;
}

public static class MentorEventTypes
{
    /// <summary>Filter order, matching the design's own chip row.</summary>
    public static readonly IReadOnlyList<MentorEventType> Ordered = new[]
    {
        MentorEventType.Milestone,
        MentorEventType.Submission,
        MentorEventType.Review,
        MentorEventType.PersonalTask,
    };

    public static string Label(MentorEventType t) => t switch
    {
        MentorEventType.Milestone    => "אבן דרך",
        MentorEventType.Submission   => "הגשה",
        MentorEventType.Review       => "ממתינה לבדיקה",
        _                            => "משימה אישית",
    };

    /// <summary>Plural, for the filter chips.</summary>
    public static string FilterLabel(MentorEventType t) => t switch
    {
        MentorEventType.Milestone    => "אבני דרך",
        MentorEventType.Submission   => "הגשות",
        MentorEventType.Review       => "ממתינות לבדיקה",
        _                            => "משימות אישיות",
    };

    /// <summary>
    /// The dot/chip hue.
    ///
    /// <para>These are CATEGORICAL, not status: they say what kind of thing an
    /// entry is, never whether it is healthy or late. That distinction is why
    /// they map onto MKpiCard's presentation accents (which already include
    /// periwinkle and amber for exactly this purpose) rather than onto
    /// MStatusDot, whose violet/teal/rose triad the System Master reserves for
    /// workflow status. Lateness is carried separately, by the rose tone on an
    /// overdue date.</para>
    /// </summary>
    public static MKpiCard.KpiAccent Accent(MentorEventType t) => t switch
    {
        MentorEventType.Milestone    => MKpiCard.KpiAccent.Periwinkle,
        MentorEventType.Submission   => MKpiCard.KpiAccent.Violet,
        MentorEventType.Review       => MKpiCard.KpiAccent.Amber,
        _                            => MKpiCard.KpiAccent.Teal,
    };

    /// <summary>CSS modifier suffix — `mcal-dot-milestone`, etc.</summary>
    public static string CssSuffix(MentorEventType t) => t.ToString().ToLowerInvariant();

    public static MentorGlyph.GlyphKind Glyph(MentorEventType t) => t switch
    {
        MentorEventType.Milestone    => MentorGlyph.GlyphKind.Projects,
        MentorEventType.Submission   => MentorGlyph.GlyphKind.Review,
        MentorEventType.Review       => MentorGlyph.GlyphKind.Request,
        _                            => MentorGlyph.GlyphKind.Personal,
    };
}
