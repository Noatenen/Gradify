using AuthWithAdmin.Client.Components;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Pages.Mentor;

// ─────────────────────────────────────────────────────────────────────────────
//  Shared view-model vocabulary for the Motiva Mentor workspace.
//
//  Same job, and the same shape, as the student side's RequestWorkspaceModel:
//  the design speaks in a small number of role-facing buckets, the domain
//  speaks in statuses, and this file is the ONE place that maps one onto the
//  other. The KPI cards, the section headings, the row chips and the filters
//  all read from here, so they cannot disagree about which bucket a thing is
//  in or what colour it should be.
//
//  Nothing here changes a business rule. RequestStatuses and its canonical
//  Hebrew labels are untouched and stay authoritative wherever the precise
//  status matters; this is a presentation grouping over them.
// ─────────────────────────────────────────────────────────────────────────────

// ── Project health ──────────────────────────────────────────────────────────

/// <summary>
/// The design's three project states. Every mentor screen uses exactly these,
/// in these three colours, and the design reference is explicit that there is
/// no fourth: "שלושת הצבעים הסמנטיים ממופים לשלושת מצבי הפרויקט".
/// </summary>
public enum MentorHealth
{
    /// <summary>במסלול — teal.</summary>
    OnTrack,

    /// <summary>דורש תשומת לב — violet.</summary>
    Attention,

    /// <summary>בסיכון — rose.</summary>
    AtRisk,
}

public static class MentorHealthStates
{
    /// <summary>
    /// Maps the server's HealthStatus onto a bucket.
    ///
    /// <para>Two vocabularies reach the client for the same idea:
    /// <c>MentorProjectSummaryDto.HealthStatus</c> carries the Projects table's
    /// own strings ("OnTrack" / "NeedsAttention" / "AtRisk"), while the lecturer
    /// dashboard computes <see cref="HealthBuckets"/> ("Healthy" / "Attention" /
    /// "AtRisk"). Both are accepted here so a caller never has to know which
    /// endpoint its row came from.</para>
    ///
    /// <para><b>Null is OnTrack, deliberately.</b> HealthStatus is nullable and
    /// is null for most seeded projects — nothing has flagged them. Treating
    /// "unknown" as at-risk would invent an alarm the data does not support,
    /// and treating it as its own fourth state would break the design's
    /// three-colour rule. A project with no recorded problem reads as being on
    /// track, which is also how the legacy mentor page treated it.</para>
    /// </summary>
    public static MentorHealth Of(string? healthStatus) => healthStatus switch
    {
        "AtRisk"                          => MentorHealth.AtRisk,
        "NeedsAttention" or "Attention"   => MentorHealth.Attention,
        _                                 => MentorHealth.OnTrack,
    };

    public static string Label(MentorHealth h) => h switch
    {
        MentorHealth.OnTrack   => "במסלול",
        MentorHealth.Attention => "דורש תשומת לב",
        _                      => "בסיכון",
    };

    /// <summary>Plural form, for the KPI card that counts projects in the state.</summary>
    public static string CountLabel(MentorHealth h) => h switch
    {
        MentorHealth.OnTrack   => "במסלול",
        MentorHealth.Attention => "דורשים תשומת לב",
        _                      => "בסיכון",
    };

    public static MStatusDot.DotTone Tone(MentorHealth h) => h switch
    {
        MentorHealth.OnTrack   => MStatusDot.DotTone.Teal,
        MentorHealth.Attention => MStatusDot.DotTone.Violet,
        _                      => MStatusDot.DotTone.Rose,
    };

    public static MKpiCard.KpiAccent Accent(MentorHealth h) => h switch
    {
        MentorHealth.OnTrack   => MKpiCard.KpiAccent.Teal,
        MentorHealth.Attention => MKpiCard.KpiAccent.Violet,
        _                      => MKpiCard.KpiAccent.Rose,
    };

    /// <summary>URL vocabulary for the state — the value carried by
    /// <c>/mentor/projects?health=</c>. A slug rather than the enum name so the
    /// link stays readable, and a single pair with <see cref="FromSlug"/> so the
    /// screen that WRITES the link and the screen that READS it cannot drift.</summary>
    public static string Slug(MentorHealth h) => h switch
    {
        MentorHealth.OnTrack   => "ontrack",
        MentorHealth.Attention => "attention",
        _                      => "atrisk",
    };

    /// <summary>The inverse of <see cref="Slug"/>. An unknown or missing value
    /// is "no filter" — a hand-edited URL must never blank the list.</summary>
    public static MentorHealth? FromSlug(string? slug) => (slug ?? "").ToLowerInvariant() switch
    {
        "ontrack"   => MentorHealth.OnTrack,
        "attention" => MentorHealth.Attention,
        "atrisk"    => MentorHealth.AtRisk,
        _           => null,
    };

    /// <summary>Ranking for "worst first" ordering — the order every mentor
    /// list uses, because the whole point of the screen is that the projects
    /// needing attention are the ones you should hit first.</summary>
    public static int Severity(MentorHealth h) => h switch
    {
        MentorHealth.AtRisk    => 0,
        MentorHealth.Attention => 1,
        _                      => 2,
    };
}

// ── Requests, from the mentor's side ────────────────────────────────────────

/// <summary>
/// The design's four responsibility buckets for a request — "מה ממתין לך, מה
/// עבר למרצה ומה כבר טופל". This is the mentor's counterpart to the student's
/// three-bucket <c>RequestBucket</c>, and it is a different split on purpose:
/// a student only needs to know whether the ball is theirs, in flight, or
/// done, whereas a mentor sits in the middle of a three-party hand-off and
/// needs to see which of the other two parties is holding it.
/// </summary>
public enum MentorRequestBucket
{
    /// <summary>מחכות לתגובתך — the mentor is the blocker.</summary>
    AwaitingMentor,

    /// <summary>בבדיקת מרצה — escalated to the academic side.</summary>
    WithLecturer,

    /// <summary>ממתינות לתגובת הצוות — handed back to the students.</summary>
    AwaitingTeam,

    /// <summary>טופלו — terminal.</summary>
    Closed,
}

public static class MentorRequestBuckets
{
    /// <summary>Display order — the order the design lists the groups in, which
    /// is also descending urgency for the mentor.</summary>
    public static readonly IReadOnlyList<MentorRequestBucket> Ordered = new[]
    {
        MentorRequestBucket.AwaitingMentor,
        MentorRequestBucket.WithLecturer,
        MentorRequestBucket.AwaitingTeam,
        MentorRequestBucket.Closed,
    };

    /// <summary>
    /// Maps a domain status onto a mentor bucket.
    ///
    /// <para><b>Only PendingMentorRecommendation waits on the mentor, and that
    /// is a capability statement, not a stylistic one.</b> The server gives a
    /// mentor exactly one decision endpoint —
    /// <c>POST /api/project-requests/{id}/mentor-recommendation</c>, which is
    /// <c>[Authorize(Roles = Mentor)]</c> and gated on this status. Every other
    /// transition runs through <c>/handle</c>, which is
    /// <c>[Authorize(Roles = Admin, Staff)]</c>, and <c>/reply</c> requires the
    /// caller to be a team member. So a mentor genuinely cannot move anything
    /// else forward.</para>
    ///
    /// <para>A <c>New</c> request was considered for this bucket — it is the
    /// design's implied "first line for your own teams" case — and deliberately
    /// rejected: putting it under "מחכות לתגובתך" would promise an action the
    /// API cannot perform, which is the one thing a work queue must never do.
    /// It reads as in-flight on the academic side instead, which is what it
    /// actually is.</para>
    ///
    /// <para><b>Why the default arm is WithLecturer.</b> NeedsInfo is the only
    /// status that hands a request back to the team and Resolved/Closed are the
    /// only terminal ones; everything else is in flight on the academic side.
    /// Falling through rather than enumerating means a status added later shows
    /// up as in-flight instead of silently vanishing from every bucket — the
    /// same safety property the student mapping has.</para>
    /// </summary>
    public static MentorRequestBucket Of(string status) => status switch
    {
        RequestStatuses.PendingMentorRecommendation
            => MentorRequestBucket.AwaitingMentor,
        RequestStatuses.NeedsInfo
            => MentorRequestBucket.AwaitingTeam,
        RequestStatuses.Resolved or RequestStatuses.Closed
            => MentorRequestBucket.Closed,
        _   => MentorRequestBucket.WithLecturer,
    };

    public static string GroupLabel(MentorRequestBucket b) => b switch
    {
        MentorRequestBucket.AwaitingMentor => "מחכות לתגובתך",
        MentorRequestBucket.WithLecturer   => "בבדיקת מרצה",
        MentorRequestBucket.AwaitingTeam   => "ממתינות לתגובת הצוות",
        _                                  => "טופלו",
    };

    /// <summary>Per-row chip. Shorter and written from the mentor's side, the
    /// way RequestBuckets.RowLabel is written from the student's.</summary>
    public static string RowLabel(MentorRequestBucket b) => b switch
    {
        MentorRequestBucket.AwaitingMentor => "דורש את פעולתך",
        MentorRequestBucket.WithLecturer   => "בבדיקת מרצה",
        MentorRequestBucket.AwaitingTeam   => "ממתין לצוות",
        _                                  => "נסגרה",
    };

    /// <summary>
    /// Row tone. The design draws "waiting on you" in amber and escalates it to
    /// rose past three days, but the System Master scopes MStatusDot to violet /
    /// teal / rose only — so the bucket resolves onto the Master's three, and
    /// the amber stays where it is presentational: the KPI card.
    /// </summary>
    public static MStatusDot.DotTone Tone(MentorRequestBucket b) => b switch
    {
        MentorRequestBucket.AwaitingMentor => MStatusDot.DotTone.Rose,
        MentorRequestBucket.WithLecturer   => MStatusDot.DotTone.Violet,
        MentorRequestBucket.AwaitingTeam   => MStatusDot.DotTone.Violet,
        _                                  => MStatusDot.DotTone.Teal,
    };

    /// <summary>True when the mentor can actually decide this request now.
    /// Drives whether decision controls render at all — a request sitting with
    /// the lecturer must show status and history only, never buttons that
    /// would post a decision the mentor no longer owns.</summary>
    public static bool IsActionable(string status) =>
        Of(status) == MentorRequestBucket.AwaitingMentor;
}

// ── Relative-time wording ───────────────────────────────────────────────────

/// <summary>
/// DEADLINE wording for the mentor screens — "יעד היום", "בעוד 4 ימים",
/// "באיחור 3 ימים".
///
/// <para><b>Deadlines only. Waiting age is not here any more.</b> This class
/// used to own both, and that was the source of the drift the attention model
/// exists to remove: <c>WaitingLabel</c> and <c>DaysSince</c> computed a
/// browser-local calendar age that four Razor call sites then compared against
/// two different hardcoded thresholds (4 for submissions, 3 for requests), while
/// the server's own supervision inbox computed a third answer in SQL. Waiting
/// age is now computed once on the server in Israel-local calendar days
/// (IsraelTime) and worded once in Shared (<see cref="MentorAging"/>), which the
/// daily digest reads too.</para>
///
/// <para>What remains is genuinely different and correctly lives here: time
/// UNTIL a real due date, for personal tasks and milestones. Those have actual
/// deadlines, so — unlike a submission or a request — they may legitimately read
/// as באיחור. Nothing else in the mentor area may.</para>
/// </summary>
public static class MentorTime
{
    /// <summary>"היום" / "אתמול" / "לפני 9 ימים" — for an event that already
    /// happened, such as a request's last activity. Not a waiting age: it
    /// reports when something last moved, never how long anyone has held it.</summary>
    public static string AgoLabel(DateTime when)
    {
        var d = Math.Max(0, (int)(DateTime.Now.Date - when.ToLocalTime().Date).TotalDays);
        return d switch
        {
            0 => "היום",
            1 => "אתמול",
            2 => "לפני יומיים",
            _ => $"לפני {d} ימים",
        };
    }

    /// <summary>Deadline wording relative to today: "יעד היום", "בעוד 4 ימים",
    /// "באיחור 3 ימים". Returns null when there is no date, so the caller can
    /// omit the phrase entirely rather than print an empty one.</summary>
    public static string? DeadlineLabel(DateTime? due)
    {
        if (due is null) return null;
        var days = (int)(due.Value.ToLocalTime().Date - DateTime.Now.Date).TotalDays;
        return days switch
        {
            0        => "יעד היום",
            1        => "יעד מחר",
            > 1      => $"בעוד {days} ימים",
            -1       => "באיחור יום",
            -2       => "באיחור יומיים",
            _        => $"באיחור {-days} ימים",
        };
    }

    /// <summary>True when the deadline has passed — drives the rose tone on a
    /// due-date chip.</summary>
    public static bool IsOverdue(DateTime? due) =>
        due is not null && due.Value.ToLocalTime().Date < DateTime.Now.Date;
}

// ── Where a mentor surface sends a click ────────────────────────────────────

/// <summary>
/// The mentor experience's navigation vocabulary, in one place.
///
/// <para>Two kinds of click exist on a mentor screen and they must never be
/// confused:</para>
/// <list type="bullet">
///   <item><b>A collection</b> — a KPI card, a "לכל ה…" link — navigates to the
///   workspace that owns that collection, with the matching filter applied.</item>
///   <item><b>One specific item</b> — a row, the CTA on a single item — opens
///   THAT item, by id, in the existing details UI on its own page. For an
///   attention item that destination is already computed by the server and
///   arrives as <c>MentorAttentionItemDto.Href</c>; these helpers cover the
///   surfaces that do not come from the attention model.</item>
/// </list>
///
/// <para>Nothing here is a new screen or a new parameter: every value below is
/// a route the mentor experience already serves — <c>?focus=</c> on המשימות
/// שלי, <c>?editTask=</c> on its personal-task editor, and <c>?health=</c> on
/// הפרויקטים שלי.</para>
/// </summary>
public static class MentorLinks
{
    // ── Collections ─────────────────────────────────────────────────────────

    /// <summary>המשימות שלי, unfiltered.</summary>
    public const string Tasks = "/mentor/tasks";

    /// <summary>המשימות שלי, scoped to הגשות לבדיקה.</summary>
    public const string Reviews = "/mentor/tasks?focus=reviews";

    /// <summary>המשימות שלי, scoped to בקשות הדורשות פעולה.</summary>
    public const string Requests = "/mentor/tasks?focus=requests";

    /// <summary>המשימות שלי, scoped to משימות אישיות.</summary>
    public const string PersonalTasks = "/mentor/tasks?focus=personal";

    /// <summary>בקשות — every request on the mentor's projects, not only the
    /// ones awaiting them.</summary>
    public const string RequestsInbox = "/mentor-requests";

    /// <summary>הפרויקטים שלי, optionally scoped to one health state.</summary>
    public static string Projects(MentorHealth? health = null) =>
        health is null
            ? "/mentor/projects"
            : $"/mentor/projects?health={MentorHealthStates.Slug(health.Value)}";

    // ── Single items ────────────────────────────────────────────────────────

    /// <summary>One personal task's editor. Same link the calendar and the
    /// attention model already use, so all three open the same modal.</summary>
    public static string PersonalTask(int taskId) => $"/mentor/tasks?editTask={taskId}";
}
