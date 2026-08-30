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

/// <summary>
/// WHEN A STUCK SUBMISSION BECOMES THE LECTURER'S.
///
/// <para>Every row the pending-mentor-approvals endpoint returns has
/// <c>MentorStatus = 'Pending'</c> — by definition it is waiting on a MENTOR,
/// so for its first days it is the mentor's item and nobody else's. What makes
/// it the lecturer's is age: at this threshold the two supervision actions
/// unlock — "תזכורת למנחה" (POST remind-mentor) and the override-approve —
/// and the wait itself becomes the thing only a lecturer can resolve.</para>
///
/// <para>This is not a new rule. It is the split the lecturer's own submissions
/// screen already draws its groups on: <c>DaysWaiting &gt;= 3</c> is
/// "ממתינות לאישורך", below it is "בבדיקת מנחה" (LecturerSubmissionsPage), and
/// it is the same single threshold the mentor attention model uses. Named here
/// so a dashboard filtering on it and a screen grouping on it cannot drift.</para>
/// </summary>
public static class PendingMentorApprovals
{
    /// <summary>Calendar days a submission must have waited on its mentor
    /// before the lecturer's supervision actions unlock and it belongs on the
    /// lecturer's own queue.</summary>
    public const int LecturerActionThresholdDays = 3;

    /// <summary>True when the next move on this stuck submission is the
    /// lecturer's — remind, or override-approve.</summary>
    public static bool AwaitsLecturer(int daysWaiting) =>
        daysWaiting >= LecturerActionThresholdDays;
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
/// One row in the lecturer "אושרו" (approved submissions) list.
/// Returned by GET /api/task-submissions/approved (Admin / Staff only).
/// Covers both mentor-approved and lecturer-override-approved submissions.
/// </summary>
public class ApprovedSubmissionRowDto
{
    public int       SubmissionId   { get; set; }
    public int       TaskId         { get; set; }
    public string    TaskTitle      { get; set; } = "";

    public int       ProjectId      { get; set; }
    public int       ProjectNumber  { get; set; }
    public string    ProjectTitle   { get; set; } = "";
    public string?   TeamName       { get; set; }

    /// <summary>Comma-separated mentor names for the project.</summary>
    public string?   MentorName     { get; set; }
    public string    MilestoneTitle { get; set; } = "";

    public DateTime  SubmittedAt    { get; set; }
    /// <summary>When MentorStatus was set to 'Approved' (either by mentor or override).</summary>
    public DateTime? ApprovedAt     { get; set; }
    /// <summary>"MentorApproved" | "LecturerOverride" — from ApprovalSources constants.</summary>
    public string?   ApprovalSource { get; set; }
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
