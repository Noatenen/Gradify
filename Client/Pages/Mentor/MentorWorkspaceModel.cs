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
/// The design's ageing vocabulary — "התקבלה היום", "ממתינה 3 ימים", "באיחור
/// 5 ימים". A mentor screen measures time SINCE something arrived, where a
/// student screen measures time UNTIL something is due, so this deliberately
/// does not reuse the student's TimeFormat helpers.
/// </summary>
public static class MentorTime
{
    /// <summary>Whole days between <paramref name="from"/> and now, floored at 0.</summary>
    public static int DaysSince(DateTime from) =>
        Math.Max(0, (int)(DateTime.Now.Date - from.ToLocalTime().Date).TotalDays);

    /// <summary>"התקבלה היום" / "ממתינה יום" / "ממתינה 3 ימים".</summary>
    public static string WaitingLabel(DateTime since, string todayText = "התקבלה היום")
    {
        var d = DaysSince(since);
        return d switch
        {
            0 => todayText,
            1 => "ממתינה יום",
            2 => "ממתינה יומיים",
            _ => $"ממתינה {d} ימים",
        };
    }

    /// <summary>"היום" / "לפני יומיים" / "לפני 9 ימים" — for an event that
    /// already happened.</summary>
    public static string AgoLabel(DateTime when)
    {
        var d = DaysSince(when);
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
