using System;
using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Moodle-submission tracking (manual) + minimal lecturer-escalation surface.
//
//  Context: the lecturer-final-review flow was retired (2026-05-17). The
//  official deliverable submission now lives in Moodle, where we have no
//  API access. We expose a manual per-project flag so admins/lecturers can
//  track it inside Gradify, plus a tiny boolean escalation toggle for
//  exceptional cases mentors want a lecturer to look at.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Controlled vocabulary for Projects.MoodleSubmissionStatus. NULL on the
/// DB row means "not tracked yet" — distinct from "Unknown" which the user
/// chose explicitly. Treat the constants as opaque strings.
/// </summary>
public static class MoodleSubmissionStatuses
{
    public const string Submitted    = "SubmittedToMoodle";
    public const string NotSubmitted = "NotSubmittedToMoodle";
    public const string Unknown      = "Unknown";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Submitted, NotSubmitted, Unknown,
    };

    /// <summary>Hebrew display label. Returns "לא נמדד" for null / not-tracked.</summary>
    public static string Label(string? s) => s switch
    {
        Submitted    => "הוגש במודל",
        NotSubmitted => "לא הוגש במודל",
        Unknown      => "סטטוס לא ידוע",
        _            => "לא נמדד",
    };

    /// <summary>Stable visual bucket for UI styling.</summary>
    public static string Bucket(string? s) => s switch
    {
        Submitted    => "ok",
        NotSubmitted => "warn",
        Unknown      => "neutral",
        _            => "muted",
    };
}

/// <summary>Read shape for the current Moodle status on a project.</summary>
public class ProjectMoodleStatusDto
{
    public int       ProjectId      { get; set; }
    public string?   Status         { get; set; }
    public string?   Notes          { get; set; }
    public DateTime? UpdatedAt      { get; set; }
    public int?      UpdatedByUserId { get; set; }
    public string?   UpdatedByName  { get; set; }
}

/// <summary>Write shape for PATCH /api/projects/{id}/moodle-status.</summary>
public class SaveProjectMoodleStatusRequest
{
    /// <summary>One of <see cref="MoodleSubmissionStatuses"/> values, or null
    /// to clear the field back to "not tracked".</summary>
    public string?  Status { get; set; }
    /// <summary>Optional free-text note shown alongside the status.</summary>
    public string?  Notes  { get; set; }
}

/// <summary>Optional payload for the escalate endpoint — just a reason.</summary>
public class EscalateSubmissionRequest
{
    public string?  Reason { get; set; }
}
