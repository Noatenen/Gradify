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

    /// <summary>Filed and in flight on the academic side.</summary>
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
    /// <para><b>Why the default arm is Lecturer.</b> NeedsInfo is the only status
    /// that hands the request back to the student, and Resolved/Closed are the
    /// only terminal ones. Everything else — New, InProgress, WaitingForStaff and
    /// the two extension-flow statuses (PendingMentorRecommendation,
    /// PendingLecturerDecision) — is in flight on the academic side, which is
    /// exactly the server's own definition of "active" in the duplicate-extension
    /// guard. Falling through rather than listing them means a status added later
    /// is treated as in-flight instead of silently vanishing from every bucket.</para>
    /// </summary>
    public static RequestBucket Of(string status) => status switch
    {
        RequestStatuses.NeedsInfo => RequestBucket.Waiting,
        RequestStatuses.Resolved or RequestStatuses.Closed => RequestBucket.Done,
        _ => RequestBucket.Lecturer,
    };

    public static string FilterId(RequestBucket bucket) => bucket switch
    {
        RequestBucket.Waiting  => WaitingFilterId,
        RequestBucket.Lecturer => LecturerFilterId,
        _                      => DoneFilterId,
    };

    /// <summary>Group heading in the workspace ("ממתין לתגובתך").</summary>
    public static string GroupLabel(RequestBucket bucket) => bucket switch
    {
        RequestBucket.Waiting  => "ממתין לתגובתך",
        RequestBucket.Lecturer => "בטיפול מרצה",
        _                      => "הושלמו",
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
