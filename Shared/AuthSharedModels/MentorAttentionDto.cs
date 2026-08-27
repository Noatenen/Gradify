using System;
using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Mentor Attention — the ONE definition of "waiting for mentor attention".
//
//  WHY THIS LIVES IN Shared AND IS COMPUTED ON THE SERVER
//  Four surfaces need this answer — בית, המשימות שלי, יומן ותכנון, and the
//  daily digest. The digest runs inside a BackgroundService with no browser
//  attached, so the definition cannot live in client C#: if it did, the digest
//  would be forced to reimplement it, which is exactly the duplication this
//  model exists to remove. The server computes WaitingDays and the bucket; the
//  client renders what it is told and never recomputes an age.
//
//  WAITING AGE IS NOT A DEADLINE
//  There is no SLA and no review deadline in this product. These buckets say
//  how long something has been waiting, never that anyone is late. The wording
//  helpers below enforce that — see MentorAging.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>What an attention item IS. Drives which section renders it and
/// which wording it gets.</summary>
public enum MentorAttentionKind
{
    /// <summary>A TaskSubmissions row with MentorStatus = 'Pending'.</summary>
    Submission,

    /// <summary>A ProjectRequests row with Status = 'PendingMentorRecommendation'.</summary>
    Request,

    /// <summary>One of the mentor's own PersonalTasks rows. Not "waiting" — it
    /// carries a real DueDate, and is the ONLY kind that may read as late.</summary>
    PersonalTask,
}

/// <summary>
/// How long an actionable item has been waiting, bucketed.
///
/// <para><b>These are attention states, not compliance states.</b> Nothing here
/// means "late": the product has no review deadline, so an item at
/// <see cref="NeedsAttention"/> has simply been waiting long enough that the
/// mentor should notice it.</para>
/// </summary>
public enum MentorAttentionAge
{
    /// <summary>Arrived today (0 calendar days).</summary>
    New,

    /// <summary>Waiting 1–2 calendar days.</summary>
    Waiting,

    /// <summary>Waiting 3+ calendar days — the only state that escalates visually.</summary>
    NeedsAttention,
}

public static class MentorAttention
{
    /// <summary>
    /// The single attention threshold, in calendar days, for BOTH submissions
    /// and requests.
    ///
    /// <para><b>Why one number, and why 3.</b> Before this model there were
    /// four hardcoded thresholds across four files — submissions escalated at
    /// 4 days on Home and Tasks, requests at 3 days on Home, Tasks and
    /// Requests, and the lecturer's supervision inbox flagged a pending
    /// submission at &gt; 3. Two different numbers for one idea is not a product
    /// distinction anyone can explain. 3 wins because it already had a
    /// precedent: it is the gate on the lecturer's "remind mentor" button, so a
    /// mentor's own screen now escalates at the same moment a lecturer is
    /// invited to chase them.</para>
    ///
    /// <para>It is NOT an SLA, a deadline, or an overdue threshold.</para>
    /// </summary>
    public const int AttentionThresholdDays = 3;

    /// <summary>Buckets a waiting age. Negative ages (a clock skew, a future
    /// timestamp) floor to today rather than throwing.</summary>
    public static MentorAttentionAge AgeOf(int waitingDays) =>
        waitingDays >= AttentionThresholdDays ? MentorAttentionAge.NeedsAttention
        : waitingDays >= 1                    ? MentorAttentionAge.Waiting
        :                                       MentorAttentionAge.New;

    /// <summary>Worst-first ordering rank. The canonical sort for every mentor
    /// queue: NeedsAttention, then Waiting, then New.</summary>
    public static int Severity(MentorAttentionAge age) => age switch
    {
        MentorAttentionAge.NeedsAttention => 0,
        MentorAttentionAge.Waiting        => 1,
        _                                 => 2,
    };
}

// ─────────────────────────────────────────────────────────────────────────────
//  Wording — ONE source, consumed by both the Razor pages and the digest
//  composer, so an email and a screen can never phrase the same item
//  differently.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Hebrew wording for mentor waiting age.
///
/// <para><b>Factual and item-focused, never accusatory.</b> "ממתינה לבדיקה כבר
/// 4 ימים" describes the item; "לא בדקת את ההגשה כבר 4 ימים" describes the
/// mentor. The system guides, it does not accuse — so the subject of every
/// sentence here is the submission or the request, never the reader.</para>
///
/// <para><b>No lateness vocabulary.</b> באיחור / overdue / missed deadline are
/// absent by construction: no deadline exists for a submission or a request, so
/// claiming one would be inventing data. Deadline wording lives separately, on
/// the client, and applies only to personal tasks — which do carry a real
/// DueDate.</para>
/// </summary>
public static class MentorAging
{
    /// <summary>The row/meta phrase for a waiting item.</summary>
    public static string WaitingLabel(MentorAttentionKind kind, int days) => kind switch
    {
        MentorAttentionKind.Submission => days switch
        {
            <= 0 => "התקבלה היום",
            1    => "ממתינה לבדיקה יום",
            2    => "ממתינה לבדיקה יומיים",
            _    => $"ממתינה לבדיקה כבר {days} ימים",
        },
        MentorAttentionKind.Request => days switch
        {
            <= 0 => "התקבלה היום",
            1    => "ממתינה לתגובתך יום",
            2    => "ממתינה לתגובתך יומיים",
            _    => $"ממתינה לתגובתך כבר {days} ימים",
        },
        // Personal tasks are dated, not waiting — a caller that reaches here is
        // asking the wrong question, so answer with nothing rather than with a
        // sentence that implies a waiting age the item does not have.
        _ => "",
    };

    /// <summary>
    /// The compact form, for a scannable list row.
    ///
    /// <para><see cref="WaitingLabel"/> is a full sentence ("ממתינה לבדיקה כבר 5
    /// ימים") — right for an email body or a detail panel, and too dense for a
    /// queue the mentor scans vertically. This says the same thing in two words
    /// and stays kind-agnostic, because the section a row sits in has already
    /// said whether it is a submission or a request.</para>
    ///
    /// <para>It carries no escalation word: the row's tone does that, and the
    /// escalation is stated in words on the row's accessible name via
    /// <see cref="StateLabel"/>. Same two rules as everything else here — the
    /// subject is the item, never the reader, and no lateness vocabulary.</para>
    /// </summary>
    public static string ShortLabel(int days) => days switch
    {
        <= 0 => "התקבלה היום",
        1    => "ממתינה יום",
        2    => "ממתינה יומיים",
        _    => $"ממתינה {days} ימים",
    };

    /// <summary>Short state chip. Only NeedsAttention gets a word of its own —
    /// New and Waiting are already fully described by their WaitingLabel, and a
    /// second chip repeating it would be noise.</summary>
    public static string? StateLabel(MentorAttentionAge age) =>
        age == MentorAttentionAge.NeedsAttention ? "דורש תשומת לב" : null;

    /// <summary>Plural group label for the digest and for section headings.</summary>
    public static string GroupLabel(MentorAttentionKind kind) => kind switch
    {
        MentorAttentionKind.Submission   => "הגשות ממתינות לבדיקה",
        MentorAttentionKind.Request      => "בקשות ממתינות לתגובתך",
        _                                => "משימות אישיות להיום",
    };
}

// ─────────────────────────────────────────────────────────────────────────────
//  DTOs — returned by GET /api/mentor/attention
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One thing that currently needs the mentor.</summary>
public class MentorAttentionItemDto
{
    public MentorAttentionKind Kind { get; set; }

    /// <summary>Notification-style entity tag: "TaskSubmission" |
    /// "ProjectRequest" | "PersonalTask". Matches the vocabulary already used by
    /// Notifications.RelatedEntityType so a digest row and a notification row
    /// point at the same thing the same way.</summary>
    public string EntityType { get; set; } = "";
    public int    EntityId   { get; set; }

    public string  Title          { get; set; } = "";
    public int?    ProjectId      { get; set; }
    public string? ProjectTitle   { get; set; }
    public string? TeamName       { get; set; }
    public string? MilestoneTitle { get; set; }

    /// <summary>Who put it there — the submitting student, or the requesting
    /// student. Null for personal tasks.</summary>
    public string? ActorName  { get; set; }

    /// <summary>RequestTypes constant, for requests only. Lets the client render
    /// its existing type badge without a second lookup.</summary>
    public string? RequestType { get; set; }

    /// <summary>When this item began waiting on the mentor. Null for personal
    /// tasks, which are dated rather than waiting.</summary>
    public DateTime? WaitingSince { get; set; }

    /// <summary>Israel-local calendar days since <see cref="WaitingSince"/>,
    /// computed once on the server. 0 for personal tasks.</summary>
    public int WaitingDays { get; set; }

    public MentorAttentionAge Age { get; set; }

    /// <summary>Personal tasks only — a real deadline, and the only field in
    /// this model that may legitimately read as "באיחור".</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Personal tasks only. Always false for waiting items: a
    /// submission or a request has no deadline to be late for.</summary>
    public bool IsOverdue { get; set; }

    /// <summary>Where this item opens in the mentor experience. Server-owned so
    /// the digest, the notification and the row all agree.
    ///
    /// <para>Always a deep link to THIS entity, never to the list it lives in:
    /// a submission opens its review drawer (<c>?submissionId=</c>), a request
    /// its own row (<c>?requestId=</c>) and a personal task its editor
    /// (<c>?editTask=</c>). A caller that renders one specific item must send
    /// the mentor here rather than to a filtered queue, or the mentor has to
    /// find the item a second time.</para></summary>
    public string Href { get; set; } = "";

    /// <summary>Pre-rendered waiting phrase, so a caller with no access to the
    /// wording helper (an email body, a notification message) never has to
    /// rebuild it. Empty for personal tasks.</summary>
    public string WaitingLabel { get; set; } = "";
}

/// <summary>Headline numbers. Every one of these is derived from the same Items
/// list on the server — a caller must never recount, or the digest and the
/// screen can drift apart.</summary>
public class MentorAttentionCountsDto
{
    public int PendingSubmissions   { get; set; }
    public int SubmissionsNewToday  { get; set; }

    public int AwaitingRequests     { get; set; }
    public int RequestsNewToday     { get; set; }

    /// <summary>Open personal tasks due today or earlier.</summary>
    public int PersonalTasksDueToday { get; set; }
    /// <summary>Subset of the above whose DueDate is strictly before today.</summary>
    public int PersonalTasksOverdue  { get; set; }

    /// <summary>Waiting items (submissions + requests) at 3+ days. Personal
    /// tasks are excluded — they are late against a deadline, which is a
    /// different idea and gets its own count.</summary>
    public int NeedsAttention { get; set; }

    /// <summary>The one number the hero and the digest headline both say:
    /// submissions + requests + personal tasks due today or earlier.</summary>
    public int Total { get; set; }
}

/// <summary>Returned by GET /api/mentor/attention.</summary>
public class MentorAttentionDto
{
    public MentorAttentionCountsDto Counts { get; set; } = new();

    /// <summary>Every actionable item, already in canonical worst-first order:
    /// NeedsAttention (oldest first), then Waiting (oldest first), then New
    /// (oldest first). Filtering by Kind preserves that order, so a page never
    /// needs to re-sort.</summary>
    public List<MentorAttentionItemDto> Items { get; set; } = new();

    /// <summary>Israel-local date the snapshot was computed for. Lets a caller
    /// reason about "today" without guessing the server's timezone.</summary>
    public DateTime AsOfLocalDate { get; set; }
}
