using System;
using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Supervision feature — "משימות ממתינות לאישור מנחה"
//
//  Lecturers/admins use the inbox to spot mentor approval bottlenecks. The DTO
//  below feeds that table; reminder + override actions get separate request
//  payloads so the wire format is unambiguous.
//
//  Privacy: ApprovalSource and OverrideReason are exposed ONLY through
//  Admin/Staff/Mentor endpoints — student endpoints never project them.
// ─────────────────────────────────────────────────────────────────────────────

public static class ApprovalSources
{
    public const string MentorApproved   = "MentorApproved";
    public const string LecturerOverride = "LecturerOverride";
}

/// <summary>One row in the lecturer "pending mentor approvals" table.</summary>
public class PendingMentorApprovalRowDto
{
    public int      SubmissionId         { get; set; }
    public int      TaskId               { get; set; }

    // Project / team context
    public int      ProjectId            { get; set; }
    public int      ProjectNumber        { get; set; }
    public string   ProjectTitle         { get; set; } = "";
    public string?  TeamName             { get; set; }

    // The mentor(s) the lecturer can poke. Joined names; empty when no mentor
    // is assigned yet (rare, but surfaced verbatim so the lecturer sees it).
    public string?  MentorName           { get; set; }

    // Milestone + task labels
    public string   MilestoneTitle       { get; set; } = "";
    public string   TaskTitle            { get; set; } = "";

    public DateTime SubmittedAt          { get; set; }
    /// <summary>Calendar-day age (today - SubmittedAt). Drives the >3-day reminder gate.</summary>
    public int      DaysWaiting          { get; set; }

    /// <summary>Mentor status — always "Pending" in this list, but kept for parity.</summary>
    public string   MentorStatus         { get; set; } = "Pending";

    /// <summary>Last reminder timestamp; null when no reminder has been sent yet.</summary>
    public DateTime? LastMentorReminderAt { get; set; }
}

/// <summary>
/// Payload for PUT /api/task-submissions/{id}/lecturer-override-approve.
/// The reason is REQUIRED — it documents why the lecturer bypassed the mentor.
/// It is internal-only and never returned to the student.
/// </summary>
public class LecturerOverrideApproveRequest
{
    public string OverrideReason { get; set; } = "";
}
