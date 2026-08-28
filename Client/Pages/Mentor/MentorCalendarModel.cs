using AuthWithAdmin.Client.Components;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Pages.Mentor;

// ─────────────────────────────────────────────────────────────────────────────
//  Vocabulary for יומן ותכנון — the mentor's cross-project planning surface.
//
//  The design reference (Motiva Mentor Calendar.dc.html) defines five entry
//  types, and all five are backed by real data here.
//
//  MEETINGS, AND WHY THEY ARE NO LONGER "UNBUILDABLE"
//  An earlier pass left the design's fifth type out, on the grounds that
//  nothing in the schema stores a mentor-authored meeting. That was true of a
//  DEDICATED meetings entity — participants, location, invitees — and it is
//  still true. It was not true of the thing the design actually draws: a dated
//  entry with a start and an end, attached to a team, that syncs to the
//  mentor's Google Calendar.
//
//  PersonalTasks already carries every one of those columns — ProjectId,
//  DueDate, StartTime, EndTime — and GoogleCalendarEventService.ScheduleAsync
//  already puts it in Google idempotently. So a meeting is not a new entity: it
//  is the shape a mentor's own dated entry takes once it has BOTH a team and an
//  hour. Nothing is fabricated and no table was added; the entry is simply
//  named for what it is.
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

    /// <summary>
    /// A guidance meeting — the mentor's own dated entry that has BOTH a team
    /// and an hour.
    ///
    /// <para>Not a separate entity: the same PersonalTask row as
    /// <see cref="PersonalTask"/>, distinguished by carrying a project AND a
    /// start time. The two facts together are what make an entry a meeting
    /// rather than a reminder — a timed entry with no team is the mentor's own
    /// work block, and a team entry with no hour is a note about that team.</para>
    /// </summary>
    Meeting,
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
    string? Href,
    /// <summary>Attention state — populated for Review entries only, straight
    /// from the shared attention model. A milestone or a deliverable has a
    /// deadline rather than a waiting age, so it has no bucket.</summary>
    MentorAttentionAge? Age = null,
    /// <summary>Pre-rendered waiting phrase from the shared wording source, so
    /// a review reads identically on the calendar, on המשימות שלי and in the
    /// daily digest.</summary>
    string? WaitingLabel = null,
    /// <summary>
    /// Whether <see cref="Date"/> carries a SCHEDULED time-of-day.
    ///
    /// <para><b>True for exactly one thing: a mentor personal task whose owner
    /// filled in שעת התחלה / שעת סיום.</b> That is a slot they chose, so the
    /// hour grid may state it. Everything else stays false and stays in the
    /// day's ללא שעה band, because nothing else in the schema stores an hour
    /// anyone decided:</para>
    ///
    /// <list type="bullet">
    ///   <item>a milestone and a deliverable are dated, not timed;</item>
    ///   <item>a personal task with no times is dated, not timed;</item>
    ///   <item>a pending review DOES carry a real timestamp, but it is the
    ///   moment the submission ARRIVED, not a slot the mentor agreed to.
    ///   Placing it on an hour line would read as "review this at 03:19",
    ///   which nobody decided.</item>
    /// </list>
    ///
    /// <para>No entry ever acquires a time to get onto the grid — the grid is
    /// what receives an entry that already had one.</para>
    /// </summary>
    bool HasTime = false,

    /// <summary>
    /// End of the scheduled block, when there is one.
    ///
    /// <para>Null is NOT "one hour" and not "unknown" — it means the entry is a
    /// DEADLINE AT AN HOUR rather than a block: due at 10:30, with no duration
    /// anyone decided. The grid draws it as a marker on that hour instead of
    /// inventing a length for it.</para>
    /// </summary>
    DateTime? EndsAt = null,

    /// <summary>
    /// The row this entry was built FROM — a PersonalTasks id for a personal
    /// task or a meeting, a TaskSubmissions id for a review. 0 for a milestone
    /// or a deliverable, which are dates on a project rather than entities a
    /// mentor opens.
    ///
    /// <para>It exists so a caller can open the entry IN PLACE instead of
    /// following <see cref="Href"/> to the screen that owns it: a mentor
    /// clicking "בקרוב" on the dashboard gets the task editor or the project
    /// Quick View over the dashboard, and the href becomes the CTA inside it.
    /// Parsing it back out of <see cref="Id"/> would work and would also make
    /// the id's string format load-bearing, which it is not.</para>
    /// </summary>
    int EntityId = 0)
{
    /// <summary>"09:00–11:00" for a block, "10:30" for an hour-deadline, null
    /// when the entry is date-only. The one place times are formatted, so the
    /// day grid, the week grid and the detail modal cannot word one entry three
    /// different ways.</summary>
    public string? TimeRange =>
        !HasTime          ? null
        : EndsAt is DateTime ends ? $"{Date:HH:mm}–{ends:HH:mm}"
        : $"{Date:HH:mm}";

    /// <summary>True for an entry that names an hour but no duration. The grid
    /// draws these as a marker rather than as an hour-long block, because an
    /// hour-long block is a claim the entry does not make.</summary>
    public bool IsPointInTime => HasTime && EndsAt is null;

    /// <summary>"פרויקט · צוות", or the personal-task marker. Never empty.</summary>
    public string Context =>
        ProjectId is null
            ? "משימה אישית"
            : string.Join(" · ", new[] { ProjectTitle, TeamName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>The two types the mentor owns and may therefore edit. Everything
    /// else on this calendar is somebody else's record, shown but not editable
    /// from here.</summary>
    public bool IsEditable =>
        Type is MentorEventType.Meeting or MentorEventType.PersonalTask;

    public bool IsPast => Date.Date < DateTime.Now.Date;
}

public static class MentorEventTypes
{
    /// <summary>Filter order, matching the design's own chip row.</summary>
    public static readonly IReadOnlyList<MentorEventType> Ordered = new[]
    {
        MentorEventType.Meeting,
        MentorEventType.PersonalTask,
        MentorEventType.Milestone,
        MentorEventType.Submission,
        MentorEventType.Review,
    };

    public static string Label(MentorEventType t) => t switch
    {
        MentorEventType.Meeting      => "פגישה",
        MentorEventType.Milestone    => "אבן דרך",
        MentorEventType.Submission   => "הגשה",
        MentorEventType.Review       => "ממתינה לבדיקה",
        _                            => "משימה אישית",
    };

    /// <summary>Plural, for the filter chips.</summary>
    public static string FilterLabel(MentorEventType t) => t switch
    {
        MentorEventType.Meeting      => "פגישות",
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
        MentorEventType.Meeting      => MKpiCard.KpiAccent.Violet,
        MentorEventType.Milestone    => MKpiCard.KpiAccent.Periwinkle,
        MentorEventType.Submission   => MKpiCard.KpiAccent.Amber,
        MentorEventType.Review       => MKpiCard.KpiAccent.Teal,
        _                            => MKpiCard.KpiAccent.Periwinkle,
    };

    /// <summary>CSS modifier suffix — `mcal-dot-milestone`, etc.</summary>
    public static string CssSuffix(MentorEventType t) => t.ToString().ToLowerInvariant();

    public static MentorGlyph.GlyphKind Glyph(MentorEventType t) => t switch
    {
        MentorEventType.Meeting      => MentorGlyph.GlyphKind.Personal,
        MentorEventType.Milestone    => MentorGlyph.GlyphKind.Projects,
        MentorEventType.Submission   => MentorGlyph.GlyphKind.Review,
        MentorEventType.Review       => MentorGlyph.GlyphKind.Request,
        _                            => MentorGlyph.GlyphKind.Personal,
    };

    /// <summary>
    /// True for the two types a mentor can actually CREATE.
    ///
    /// <para>The design draws five type chips in its create form. Three of them
    /// — אבן דרך, הגשה, ממתינה לבדיקה — are system-derived: a milestone belongs
    /// to a project's plan, a deliverable to a milestone, a pending review to a
    /// student's upload. There is no endpoint by which a mentor authors any of
    /// them, so offering the chip would be a control that cannot work.</para>
    /// </summary>
    public static bool IsAuthorable(MentorEventType t) =>
        t is MentorEventType.Meeting or MentorEventType.PersonalTask;
}
