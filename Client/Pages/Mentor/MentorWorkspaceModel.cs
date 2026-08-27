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

    // The Slug / FromSlug pair that used to live here was the URL vocabulary
    // for `/mentor/projects?health=`. פרויקטים בהנחייתי no longer filters by
    // health — see MentorProjectSignal for why — so the pair was removed rather
    // than left promising a query parameter no screen reads. The rest of this
    // vocabulary stands: HealthStatus is still the product's health column, and
    // this is still the one place that words and colours it.

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

// ── What a supervised project needs from the mentor ─────────────────────────

/// <summary>
/// The one thing on a supervised project that is waiting on the mentor right
/// now. This is the axis <c>Motiva Mentor Teams.dc.html</c> filters, colours
/// and orders פרויקטים בהנחייתי by — its rows carry one dot and one phrase, so
/// a project has exactly ONE signal, the most serious one.
///
/// <para><b>Why this replaced project health as that screen's filter.</b>
/// <c>Projects.HealthStatus</c> is never written by this application — the same
/// finding that already made <c>MentorHomePage.TeamHealth</c> stop reading it —
/// so filtering by it put every project in במסלול and left the other two
/// options permanently empty. These four states are read off columns the
/// product actually maintains: a milestone's due date, the pending
/// mentor-review count, and the project's open requests.</para>
///
/// <para><see cref="MentorHealthStates"/> is untouched and still owns the
/// health vocabulary for the day that column starts being written.</para>
///
/// <para>Declaration order IS the seriousness order (see <see cref="Priority"/>),
/// and it matches the order Home's team chip already uses: a missed deadline
/// outranks a queue, and a queue outranks a request someone else may still be
/// holding.</para>
/// </summary>
public enum MentorProjectSignal
{
    /// <summary>אבן דרך שהמועד שלה עבר — rose.</summary>
    Late,

    /// <summary>הגשה שממתינה לבדיקת המנחה — rose.</summary>
    Submission,

    /// <summary>בקשה פתוחה על הפרויקט — violet.</summary>
    Request,

    /// <summary>שום דבר לא ממתין — teal.</summary>
    None,
}

public static class MentorProjectSignals
{
    // ── Filter keys ─────────────────────────────────────────────────────────
    // The design's own five options: הכל · דורש תשומת לב · הגשה לבדיקה ·
    // בקשה פתוחה · מתקדם כרגיל. Two of them are not signals but *groupings*
    // over them, which is why the filter is keyed by string rather than by the
    // enum: "attention" is any signal at all and "ok" is the absence of one.
    //
    // Late deliberately has no tab of its own — the reference does not draw one,
    // and a late project is reachable through דורש תשומת לב, which counts it.

    /// <summary>The conventional MFilterTabs "show everything" key.</summary>
    public const string FilterAll       = "";
    public const string FilterAttention = "attention";
    public const string FilterOk        = "ok";

    /// <summary>URL/tab vocabulary for one signal. Paired with
    /// <see cref="Normalize"/> so the screen that writes a link and the screen
    /// that reads it cannot drift.</summary>
    public static string Slug(MentorProjectSignal s) => s switch
    {
        MentorProjectSignal.Late       => "late",
        MentorProjectSignal.Submission => "submission",
        MentorProjectSignal.Request    => "request",
        _                              => FilterOk,
    };

    /// <summary>Any inbound filter value — a query string, a saved link —
    /// reduced to one of the keys the tab strip actually offers. Anything
    /// unrecognised becomes "show everything": a hand-edited URL must never
    /// blank the list.</summary>
    public static string Normalize(string? key) => (key ?? "").ToLowerInvariant() switch
    {
        FilterAttention                  => FilterAttention,
        "submission"                     => "submission",
        "request"                        => "request",
        FilterOk                         => FilterOk,
        // "late" is a real signal with no tab of its own; it resolves to the
        // tab that does contain it rather than to an empty selection.
        "late"                           => FilterAttention,
        _                                => FilterAll,
    };

    /// <summary>
    /// The project's single signal, from the three real sources.
    ///
    /// <para><paramref name="milestoneOverdue"/> is passed in rather than derived
    /// here because it has two possible sources — the roadmap's own
    /// <c>Overdue</c> milestone when stages are configured, and the summary's
    /// current-milestone due date when they are not — and this vocabulary
    /// should not have to know which one the caller had.</para>
    /// </summary>
    public static MentorProjectSignal Of(bool milestoneOverdue, int pendingReviews, int openRequests) =>
          milestoneOverdue   ? MentorProjectSignal.Late
        : pendingReviews > 0 ? MentorProjectSignal.Submission
        : openRequests   > 0 ? MentorProjectSignal.Request
        :                      MentorProjectSignal.None;

    /// <summary>Worst first — the order פרויקטים בהנחייתי sorts by, before
    /// falling back to the nearest deadline.</summary>
    public static int Priority(MentorProjectSignal s) => (int)s;

    /// <summary>Short name for the state, for the drawer's chip and for the
    /// accessible name of a row's bare dot.</summary>
    public static string Label(MentorProjectSignal s) => s switch
    {
        MentorProjectSignal.Late       => "אבן דרך באיחור",
        MentorProjectSignal.Submission => "הגשה לבדיקה",
        MentorProjectSignal.Request    => "בקשה פתוחה",
        _                              => "מתקדם כרגיל",
    };

    /// <summary>The Master's three semantics, mapped the way the reference
    /// colours its list: a request is violet because someone else may still be
    /// holding it, anything overdue or queued on the mentor is rose, and a
    /// quiet project is teal.</summary>
    public static MStatusDot.DotTone Tone(MentorProjectSignal s) => s switch
    {
        MentorProjectSignal.Request => MStatusDot.DotTone.Violet,
        MentorProjectSignal.None    => MStatusDot.DotTone.Teal,
        _                           => MStatusDot.DotTone.Rose,
    };

    /// <summary>
    /// The same four states worded as PROJECT HEALTH — תקין / דורש מעקב / בסיכון.
    ///
    /// <para>This is a projection, not a second opinion. Home's team chip and
    /// the project workspace's title chip used to each derive three health
    /// states from the same three columns in their own private helper; both now
    /// read this, so a project cannot be בסיכון on one screen and תקין on
    /// another. It is deliberately NOT <see cref="MentorHealthStates"/>: that
    /// vocabulary words <c>Projects.HealthStatus</c>, a column this application
    /// never writes.</para></summary>
    public static string HealthLabel(MentorProjectSignal s) => s switch
    {
        MentorProjectSignal.Late => "בסיכון",
        MentorProjectSignal.None => "תקין",
        _                        => "דורש מעקב",
    };

    public static MStatusBadge.BadgeVariant HealthVariant(MentorProjectSignal s) => s switch
    {
        MentorProjectSignal.Late => MStatusBadge.BadgeVariant.Rose,
        MentorProjectSignal.None => MStatusBadge.BadgeVariant.Teal,
        _                        => MStatusBadge.BadgeVariant.Violet,
    };

    /// <summary>
    /// The one sentence that says what this project needs from the mentor
    /// NEXT — "3 הגשות ממתינות לבדיקתך", "בקשה ממתינה להמלצתך",
    /// "אבן דרך באיחור 4 ימים", "מתקדם כרגיל".
    ///
    /// <para>Every branch states a real count or a real date. There is no
    /// "פעילות אחרונה" or "בעלים כרגע" here because no column backs them.</para>
    ///
    /// <para>This lived as a private static on פרויקטים בהנחייתי until בית's
    /// team list needed the identical phrase. Two copies would have let one
    /// screen call a team "בקשה פתוחה" while the other called it
    /// "בקשה ממתינה להמלצתך" for the same row — the drift this file exists to
    /// remove. It pairs with <see cref="HealthLabel"/>: the badge states the
    /// STATE, this states the ACTION.</para></summary>
    public static string Note(
        MentorProjectSignal signal, DateTime? due, int reviews, IReadOnlyList<ProjectRequestRowDto> requests)
        => signal switch
    {
        MentorProjectSignal.Late       => $"אבן דרך {MentorTime.DeadlineLabel(due)}",
        MentorProjectSignal.Submission => reviews == 1
                                              ? "הגשה ממתינה לבדיקתך"
                                              : $"{reviews} הגשות ממתינות לבדיקתך",
        MentorProjectSignal.Request    => requests.Any(r => MentorRequestBuckets.IsActionable(r.Status))
                                              ? "בקשה ממתינה להמלצתך"
                                              : requests.Count == 1
                                                    ? "בקשה פתוחה"
                                                    : $"{requests.Count} בקשות פתוחות",
        _                              => "מתקדם כרגיל",
    };

    /// <summary>Whether a signal is selected by one of the five tab keys.</summary>
    public static bool Matches(MentorProjectSignal s, string? key) => Normalize(key) switch
    {
        FilterAttention => s != MentorProjectSignal.None,
        FilterOk        => s == MentorProjectSignal.None,
        FilterAll       => true,
        var slug        => Slug(s) == slug,
    };
}

// ── One attention item, as a row ────────────────────────────────────────────

/// <summary>
/// How an attention item is worded and coloured in a list row.
///
/// <para>Every mentor surface that draws the reference's five-track attention
/// row — בית, and the workspace at <c>/mentor/projects/{id}</c> — reads these.
/// They lived as private statics on MentorHomePage until the workspace needed
/// the identical row; copying them would have let one screen call an item
/// "בקשה" while the other called it something else, which is the exact class of
/// drift the attention model exists to remove.</para>
///
/// <para>Nothing here computes an age or a threshold. <c>WaitingDays</c> and
/// <see cref="MentorAttentionAge"/> arrive already decided by
/// <c>GET /api/mentor/attention</c>, in Israel-local calendar days.</para>
/// </summary>
public static class MentorAttentionRows
{
    /// <summary>The type tag — the reference's three, and no fourth.</summary>
    public static string TypeLabel(MentorAttentionItemDto i) => i.Kind switch
    {
        MentorAttentionKind.Submission   => "הגשה לבדיקה",
        MentorAttentionKind.Request      => "בקשה",
        MentorAttentionKind.PersonalTask => "משימה",
        _                                => "פריט",
    };

    /// <summary>
    /// The tag's semantic role, rendered by the shared <c>MStatusBadge</c>.
    ///
    /// <para>Blue for a submission, violet for a request, neutral for the
    /// mentor's own task. <c>Info</c> is the blue role inside
    /// <c>.motiva-final</c> — the fourth semantic role the finished screens
    /// added — which is why a submission maps onto it rather than onto
    /// violet.</para></summary>
    public static MStatusBadge.BadgeVariant TypeVariant(MentorAttentionItemDto i) => i.Kind switch
    {
        MentorAttentionKind.Submission   => MStatusBadge.BadgeVariant.Info,
        MentorAttentionKind.Request      => MStatusBadge.BadgeVariant.Violet,
        _                                => MStatusBadge.BadgeVariant.Neutral,
    };

    /// <summary>The team that owns the item, falling back to the project title
    /// when a project has no named team. An em dash when it belongs to neither —
    /// a personal reminder is the one kind with no project.</summary>
    public static string Team(MentorAttentionItemDto i)
    {
        if (!string.IsNullOrWhiteSpace(i.TeamName))     return i.TeamName!;
        if (!string.IsNullOrWhiteSpace(i.ProjectTitle)) return i.ProjectTitle!;
        return "—";
    }

    /// <summary>The milestone the item sits under, or an em dash — so the
    /// tracks stay legible as columns rather than collapsing.</summary>
    public static string Stage(MentorAttentionItemDto i) =>
        string.IsNullOrWhiteSpace(i.MilestoneTitle) ? "—" : i.MilestoneTitle!;

    /// <summary>
    /// The trailing time cell, and the one place the two clocks in this model
    /// are kept apart.
    ///
    /// <para>A submission or a request has a WAITING AGE and may never read as
    /// באיחור, because it has no deadline to be late for. A personal task has a
    /// real DueDate and is the only kind that may.</para></summary>
    public static string TimeLabel(MentorAttentionItemDto i)
    {
        if (i.Kind != MentorAttentionKind.PersonalTask)
            return ShortWait(i.WaitingDays);

        if (i.IsOverdue)
        {
            var days = Math.Max(1, (int)(DateTime.Today - (i.DueDate?.Date ?? DateTime.Today)).TotalDays);
            return $"באיחור {days} י׳";
        }

        if (i.DueDate?.Date == DateTime.Today) return "יעד היום";
        if (i.DueDate is null)                 return "—";

        return $"עד {i.DueDate.Value:dd.MM}";
    }

    /// <summary>The waiting age at column width. <c>MentorAging.WaitingLabel</c>
    /// is the full sentence and is still what the digest and the detail panels
    /// print; a 90px cell cannot hold it.</summary>
    public static string ShortWait(int days) => days switch
    {
        <= 0 => "היום",
        1    => "אתמול",
        _    => $"{days} ימים",
    };

    /// <summary>True for a genuine escalation — a waiting item past the shared
    /// 3-day threshold, or an overdue personal task. This never decides what
    /// "escalated" means; the age arrives already set by the server.</summary>
    public static bool IsEscalated(MentorAttentionItemDto i) =>
        i.Age == MentorAttentionAge.NeedsAttention || i.IsOverdue;
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
/// שלי, <c>?editTask=</c> on its personal-task editor, and <c>?filter=</c> on
/// פרויקטים בהנחייתי.</para>
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

    /// <summary>יומן ותכנון. Named here for the same reason as the rest: Home's
    /// "בקרוב" block and the top bar's calendar shortcut both point at it, and
    /// a literal in each would be two places to miss if the route moved.</summary>
    public const string Calendar = "/mentor/calendar";

    /// <summary>פרויקטים בהנחייתי, optionally scoped to one attention state.
    ///
    /// <para>Took over from a <c>?health=</c> overload when that screen's filter
    /// moved from project health — a column this application never writes — to
    /// the attention signals it does maintain. <see cref="MentorProjectSignals"/>
    /// owns both halves of the pair, so the link written here and the tab
    /// selected there cannot drift.</para></summary>
    public static string Projects(MentorProjectSignal? signal = null) =>
        signal is null
            ? "/mentor/projects"
            : $"/mentor/projects?filter={MentorProjectSignals.Slug(signal.Value)}";

    // ── Single items ────────────────────────────────────────────────────────

    /// <summary>One personal task's editor. Same link the calendar and the
    /// attention model already use, so all three open the same modal.</summary>
    public static string PersonalTask(int taskId) => $"/mentor/tasks?editTask={taskId}";
}
