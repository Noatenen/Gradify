using System;
using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Centralised constants for the Innovation-Team integration.
//
//  Everything here lives in Shared/ so both the server (storage/validation)
//  and the client (UI display) reference the same canonical strings —
//  no more "approved" vs "Approved" vs "אושר" drift across layers.
//
//  Treat the constants as the *contract* with the Innovation Team. Adding a
//  new value here + a Label entry is the only client-side change a new
//  upstream status needs.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Machine-readable status tokens we recognise on inbound webhook payloads.
/// The upstream system may send arbitrary strings; we never reject unknown
/// tokens — we just don't have a Hebrew label or terminal-flag for them.
/// </summary>
public static class ExternalRequestStatuses
{
    public const string Pending     = "pending";       // initial state
    public const string Received    = "received";      // acknowledged but not started
    public const string InReview    = "in_review";     // being evaluated by the team
    public const string InProgress  = "in_progress";   // accepted, being worked on
    public const string OnHold      = "on_hold";       // paused — waiting on info
    public const string Approved    = "approved";      // terminal: success
    public const string Completed   = "completed";     // terminal: success (alt)
    public const string Rejected    = "rejected";      // terminal: failure
    public const string Cancelled   = "cancelled";     // terminal: failure
    public const string Closed      = "closed";        // terminal: neutral

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        Pending, Received, InReview, InProgress, OnHold,
        Approved, Completed, Rejected, Cancelled, Closed,
    };

    public static readonly IReadOnlyCollection<string> Terminal = new[]
    {
        Approved, Completed, Rejected, Cancelled, Closed,
    };

    private static readonly HashSet<string> _terminalSet =
        new(Terminal, StringComparer.OrdinalIgnoreCase);

    public static bool IsTerminal(string? s) =>
        !string.IsNullOrWhiteSpace(s) && _terminalSet.Contains(s.Trim());

    /// <summary>Hebrew display label. Unknown tokens are returned verbatim so
    /// the upstream system's choice still surfaces — just unstyled.</summary>
    public static string Label(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "ממתין";
        return s.Trim().ToLowerInvariant() switch
        {
            Pending     => "ממתין",
            Received    => "התקבל",
            InReview    => "בבדיקה",
            InProgress  => "בטיפול",
            OnHold      => "מושהה",
            Approved    => "אושר",
            Completed   => "הושלם",
            Rejected    => "נדחה",
            Cancelled   => "בוטל",
            Closed      => "נסגר",
            _           => s,
        };
    }

    /// <summary>Maps a token to a stable visual bucket so UI styling stays
    /// consistent across statuses. Used by the student-facing status pill.</summary>
    public static string Bucket(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "pending";
        return s.Trim().ToLowerInvariant() switch
        {
            Approved or Completed                       => "done",
            Rejected or Cancelled                       => "rejected",
            InReview or InProgress                      => "progress",
            Closed                                      => "neutral",
            _                                           => "pending",
        };
    }
}

/// <summary>
/// Loose category tokens for the *type* of request. Not enforced — the
/// Innovation Team may send anything — but the constants here keep our own
/// references consistent.
/// </summary>
public static class ExternalRequestTypes
{
    public const string InnovationRequest = "innovation_request";
    public const string Feedback          = "feedback";
    public const string MeetingRequest    = "meeting_request";
    public const string ResourceRequest   = "resource_request";
    public const string General           = "general";

    public static string Label(string? t) =>
        string.IsNullOrWhiteSpace(t)
            ? "בקשה"
            : t.Trim().ToLowerInvariant() switch
            {
                InnovationRequest => "בקשת חדשנות",
                Feedback          => "משוב",
                MeetingRequest    => "בקשת פגישה",
                ResourceRequest   => "בקשת משאב",
                General           => "כללי",
                _                 => t,
            };
}

/// <summary>
/// Form categories used by admins when creating an ExternalForm row.
/// Free-text on the wire; these labels keep the dropdown options consistent.
/// </summary>
public static class ExternalFormCategories
{
    public const string InnovationRequest = "InnovationRequest";
    public const string Feedback          = "Feedback";
    public const string MeetingRequest    = "MeetingRequest";
    public const string ResourceRequest   = "ResourceRequest";
    public const string General           = "General";

    public static readonly IReadOnlyCollection<string> Suggested = new[]
    {
        InnovationRequest, Feedback, MeetingRequest, ResourceRequest, General,
    };

    public static string Label(string? c) =>
        string.IsNullOrWhiteSpace(c)
            ? "ללא קטגוריה"
            : c.Trim() switch
            {
                InnovationRequest => "בקשת חדשנות",
                Feedback          => "משוב",
                MeetingRequest    => "פגישה",
                ResourceRequest   => "משאבים",
                General           => "כללי",
                _                 => c,
            };
}

/// <summary>
/// Audit-log action verbs. Stored in ExternalFormAuditLog.Action so a future
/// reviewer can filter by action without parsing the JSON snapshots.
/// </summary>
public static class ExternalFormAuditActions
{
    public const string Created       = "Created";
    public const string Updated       = "Updated";
    public const string Toggled       = "Toggled";
    public const string SoftDeleted   = "SoftDeleted";
    public const string Restored      = "Restored";
}