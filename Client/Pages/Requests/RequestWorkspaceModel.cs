using AuthWithAdmin.Client.Components;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Pages.Requests;

// ─────────────────────────────────────────────────────────────────────────────
//  Shared view-model vocabulary for the student Requests workspace.
//
//  The finalized Motiva Requests screen organises every request into exactly
//  three responsibility-based buckets. The domain has eight statuses. This file
//  is the single place that maps one onto the other, so the KPI row, the status
//  filter, the group headers and the per-row status chip can never disagree
//  about which bucket a request is in.
//
//  Nothing here changes a business rule: the statuses, their transitions and
//  their canonical Hebrew labels (RequestStatuses.Label) are untouched. This is
//  a presentation grouping over them, and RequestStatuses.Label remains the
//  authoritative name wherever the precise status matters (the thread's own
//  StatusChange events still render it verbatim).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The Design's three responsibility buckets.</summary>
public enum RequestBucket
{
    /// <summary>The ball is in the student's court.</summary>
    Waiting,

    /// <summary>Filed and in flight on the academic side — with a mentor OR
    /// with a lecturer / staff member. The member name predates the extension
    /// flow's mentor stage and is kept because the student dashboard and the
    /// top-nav badge both name it; <see cref="RequestBuckets.GroupLabel"/> and
    /// <see cref="RequestBuckets.WhereLabel"/> carry the accurate words.</summary>
    Lecturer,

    /// <summary>Finished — read-only history.</summary>
    Done,
}

public static class RequestBuckets
{
    /// <summary>Filter ids, mirroring the Design's own `st-*` keys. Used by both
    /// the KPI cards and the status dropdown, which write the same state.</summary>
    public const string WaitingFilterId  = "st-wait";
    public const string LecturerFilterId = "st-lect";
    public const string DoneFilterId     = "st-done";

    /// <summary>
    /// Maps a domain status onto its bucket.
    ///
    /// <para><b>This is a fold of <see cref="RequestOwnership"/>, not a second
    /// status mapping.</b> "Whose court is this request in" is answered once,
    /// in Shared, by <c>RequestOwnership.NextActionOwner</c> — the same answer
    /// the student dashboard's attention card and the staff queues read. This
    /// method only groups that answer the way the student's own workspace
    /// needs it: the student's two "someone else is holding it" owners (Mentor
    /// and Staff) are one bucket here, because from the student's side there is
    /// nothing to do about either.</para>
    ///
    /// <para>It used to restate the mapping as its own status switch. The two
    /// agreed status for status, which is exactly why the restatement was worth
    /// removing: a status added to <see cref="RequestStatuses"/> later now
    /// reaches this page through the shared default arm instead of needing the
    /// same edit in two files.</para>
    /// </summary>
    public static RequestBucket Of(string status) =>
        RequestOwnership.NextActionOwner(status) switch
        {
            RequestOwnership.Owner.Student => RequestBucket.Waiting,
            RequestOwnership.Owner.None    => RequestBucket.Done,
            // Mentor + Staff — in flight with someone who is not the student.
            _                              => RequestBucket.Lecturer,
        };

    /// <summary>
    /// WHO IS HOLDING an in-flight request, for the one bucket that folds two
    /// owners together. Metadata on a queue row, never a status of its own —
    /// every arm below is an existing <see cref="RequestStatuses"/> value.
    ///
    /// <para>Only <see cref="RequestBucket.Lecturer"/> rows need it: the other
    /// two buckets are one owner each and their own row label already says so.
    /// </para>
    /// </summary>
    public static string WhereLabel(string status) => status switch
    {
        RequestStatuses.NeedsInfo                   => "ממתינה לפרטים ממך",
        RequestStatuses.PendingMentorRecommendation => "אצל המנחה",
        RequestStatuses.PendingLecturerDecision     => "אצל המרצה",
        RequestStatuses.Resolved                    => "טופלה",
        RequestStatuses.Closed                      => "נסגרה",
        _                                           => "אצל הצוות האקדמי",
    };

    public static string FilterId(RequestBucket bucket) => bucket switch
    {
        RequestBucket.Waiting  => WaitingFilterId,
        RequestBucket.Lecturer => LecturerFilterId,
        _                      => DoneFilterId,
    };

    /// <summary>The bucket's name wherever the student chooses between them —
    /// the queue's filter pills and the KPI chips read the same three words, so
    /// a chip can never be labelled differently from the tab that selects it.
    ///
    /// <para>"בטיפול" rather than "בטיפול מרצה": this bucket also holds the
    /// requests waiting on a MENTOR, and naming one of its two owners made the
    /// other look misfiled. Which of them is holding a given request is said on
    /// the row itself, by <see cref="WhereLabel"/>.</para></summary>
    public static string GroupLabel(RequestBucket bucket) => bucket switch
    {
        RequestBucket.Waiting  => "דורשות את פעולתך",
        RequestBucket.Lecturer => "בטיפול",
        _                      => "טופלו",
    };

    /// <summary>Per-row status chip ("תגובה חדשה"). Deliberately shorter and
    /// more direct than RequestStatuses.Label, which is written from the staff
    /// side ("הוחזרה לסטודנט").</summary>
    public static string RowLabel(RequestBucket bucket) => bucket switch
    {
        RequestBucket.Waiting  => "תגובה חדשה",
        RequestBucket.Lecturer => "אצל המרצה",
        _                      => "טופל",
    };

    /// <summary>The permitted semantic tone for the bucket.
    ///
    /// <para>The Design draws the "waiting on you" row in amber. The System
    /// Master scopes its "three semantic colors only" rule to exactly this
    /// component, so the row resolves onto Rose — the Master's attention tone —
    /// while the amber stays where it is presentational: the KPI card.</para></summary>
    public static MStatusDot.DotTone Tone(RequestBucket bucket) => bucket switch
    {
        RequestBucket.Waiting  => MStatusDot.DotTone.Rose,
        RequestBucket.Lecturer => MStatusDot.DotTone.Violet,
        _                      => MStatusDot.DotTone.Teal,
    };

    /// <summary>Row-surface modifier suffix (`sreq-thread-waiting`, …).</summary>
    public static string CssSuffix(RequestBucket bucket) =>
        bucket.ToString().ToLowerInvariant();
}

/// <summary>
/// A file the student has staged but not yet sent. Shared by the page (which
/// owns the per-request draft dictionaries) and RequestThreadRow (which renders
/// them), so both sides describe a pending upload the same way.
/// Mirrors RequestAttachmentUploadRequest, which is what it becomes on send.
/// </summary>
public sealed record PendingRequestAttachment(
    string Name,
    string Base64,
    string ContentType,
    long   Size);
