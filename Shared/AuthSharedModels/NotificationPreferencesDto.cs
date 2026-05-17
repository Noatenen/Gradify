using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  User notification preferences — per-user matrix of (NotificationType →
//  in-app / email). Backed by the UserNotificationPreferences table.
//  Missing rows fall back to NotificationTypes.DefaultsByType (see below).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Canonical NotificationType identifiers + Hebrew labels + groups.</summary>
public static class NotificationTypes
{
    // Requests
    public const string RequestCreated             = "RequestCreated";
    public const string RequestStatusChanged       = "RequestStatusChanged";
    public const string RequestCommentAdded        = "RequestCommentAdded";

    // Tasks
    public const string TaskAssigned               = "TaskAssigned";
    public const string TaskDueSoon                = "TaskDueSoon";
    public const string TaskOverdue                = "TaskOverdue";

    // Submissions
    public const string SubmissionSubmitted        = "SubmissionSubmitted";
    public const string SubmissionFeedbackReceived = "SubmissionFeedbackReceived";

    // Milestones
    public const string MilestoneDueSoon           = "MilestoneDueSoon";
    public const string MilestoneOverdue           = "MilestoneOverdue";

    // Assignments
    public const string AssignmentPublished        = "AssignmentPublished";

    public static readonly IReadOnlyList<string> All = new[]
    {
        RequestCreated, RequestStatusChanged, RequestCommentAdded,
        TaskAssigned, TaskDueSoon, TaskOverdue,
        SubmissionSubmitted, SubmissionFeedbackReceived,
        MilestoneDueSoon, MilestoneOverdue,
        AssignmentPublished,
    };

    /// <summary>Group identifiers used by the settings UI.</summary>
    public static class Groups
    {
        public const string Requests    = "Requests";    // בקשות
        public const string Tasks       = "Tasks";       // משימות
        public const string Submissions = "Submissions"; // הגשות
        public const string Milestones  = "Milestones";  // אבני דרך
        public const string Assignments = "Assignments"; // שיבוצים
    }

    public static string GroupOf(string type) => type switch
    {
        RequestCreated or RequestStatusChanged or RequestCommentAdded => Groups.Requests,
        TaskAssigned or TaskDueSoon or TaskOverdue                    => Groups.Tasks,
        SubmissionSubmitted or SubmissionFeedbackReceived             => Groups.Submissions,
        MilestoneDueSoon or MilestoneOverdue                          => Groups.Milestones,
        AssignmentPublished                                           => Groups.Assignments,
        _ => Groups.Requests,
    };

    public static string GroupLabel(string group) => group switch
    {
        Groups.Requests    => "בקשות",
        Groups.Tasks       => "משימות",
        Groups.Submissions => "הגשות",
        Groups.Milestones  => "אבני דרך",
        Groups.Assignments => "שיבוצים",
        _                  => group,
    };

    public static string Label(string type) => type switch
    {
        RequestCreated             => "פתיחת בקשה חדשה",
        RequestStatusChanged       => "עדכון סטטוס בקשה",
        RequestCommentAdded        => "תגובה חדשה בבקשה",
        TaskAssigned               => "שיוך משימה",
        TaskDueSoon                => "משימה קרובה לתאריך יעד",
        TaskOverdue                => "משימה באיחור",
        SubmissionSubmitted        => "הגשה חדשה",
        SubmissionFeedbackReceived => "קבלת משוב על הגשה",
        MilestoneDueSoon           => "אבן דרך קרובה לתאריך יעד",
        MilestoneOverdue           => "אבן דרך באיחור",
        AssignmentPublished        => "פרסום שיבוצים",
        _                          => type,
    };

    /// <summary>Canonical default state when a user has no row yet.
    /// Both in-app and email are ON for every type — the system errs on the
    /// side of keeping mentors / staff aware of every actionable event. Users
    /// can still opt out per-type through the settings UI.</summary>
    public static (bool InApp, bool Email) DefaultsForType(string type)
    {
        // The `type` parameter is retained for API parity and for future
        // overrides. Today, every visible type defaults to email-on.
        _ = type;
        return (true, true);
    }

    // ── Per-role visibility map ──────────────────────────────────────────────
    //  Each role only sees notification types it can act on. The lists are
    //  authoritative — the settings GET endpoint filters server-side using
    //  ForRoles so the UI never decides what to hide.
    //
    //  Multi-role users see the union of their roles' types — Admin + Staff
    //  end up with the same list; an Admin who is also a Mentor sees mentor
    //  types in addition to staff types.

    private static readonly IReadOnlyList<string> _studentTypes = new[]
    {
        RequestStatusChanged, RequestCommentAdded,
        TaskAssigned, TaskDueSoon, TaskOverdue,
        SubmissionFeedbackReceived,
        AssignmentPublished,
    };

    private static readonly IReadOnlyList<string> _mentorTypes = new[]
    {
        RequestCreated, RequestCommentAdded,
        SubmissionSubmitted,
        TaskDueSoon, TaskOverdue,
    };

    // Lecturer (Staff) and Admin share the same scope — both run the program.
    private static readonly IReadOnlyList<string> _staffTypes = new[]
    {
        RequestCreated, RequestCommentAdded,
        SubmissionSubmitted, SubmissionFeedbackReceived,
        TaskDueSoon, TaskOverdue,
    };

    /// <summary>Returns the notification types that should be visible to a
    /// user with the given roles, preserving canonical order from
    /// <see cref="All"/>.</summary>
    public static IReadOnlyList<string> ForRoles(IEnumerable<string>? roles)
    {
        if (roles is null) return Array.Empty<string>();

        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            if (string.Equals(role, "Student", StringComparison.OrdinalIgnoreCase))
                foreach (var t in _studentTypes) union.Add(t);
            else if (string.Equals(role, "Mentor", StringComparison.OrdinalIgnoreCase))
                foreach (var t in _mentorTypes)  union.Add(t);
            else if (string.Equals(role, "Staff", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                foreach (var t in _staffTypes)   union.Add(t);
        }

        // Stable canonical order — keeps the UI groups looking identical
        // across users, regardless of which role buckets contributed which
        // types.
        return All.Where(union.Contains).ToList();
    }
}

public class NotificationPreferenceItemDto
{
    public string NotificationType { get; set; } = "";
    public bool   InAppEnabled     { get; set; } = true;
    public bool   EmailEnabled     { get; set; }
    /// <summary>Group identifier (Requests / Tasks / Submissions / Milestones
    /// / Assignments) — populated by the server using NotificationTypes.GroupOf
    /// so the client can render group headers without repeating the mapping.</summary>
    public string Group            { get; set; } = "";
}

/// <summary>Returned by GET /api/users/me/notification-preferences.</summary>
public class NotificationPreferencesDto
{
    public List<NotificationPreferenceItemDto> Items { get; set; } = new();
}

/// <summary>Payload for PUT /api/users/me/notification-preferences.
/// The server upserts every entry by (UserId, NotificationType).</summary>
public class SaveNotificationPreferencesRequest
{
    public List<NotificationPreferenceItemDto> Items { get; set; } = new();
}
