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

    /// <summary>True when a mentor explicitly escalated this submission to the
    /// Lecturer for a decision/override (EscalatedToLecturer = 1 in the DB).</summary>
    public bool EscalatedToLecturer { get; set; }

    /// <summary>Mentor's reason for escalating; null when not escalated.</summary>
    public string? EscalationReason { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Course-wide submission health — GET /api/task-submissions/lecturer-overview
//
//  One row per submission task within the Lecturer's scope (same project filter
//  as GET /api/dashboard?scope=lecturer). The ComputedStatus field drives the
//  Lecturer Submissions KPIs and list, keeping the same underlying data as the
//  Student/Mentor workflow.
//
//  Status vocabulary (matches the design reference):
//    "missing"          — past due, no submission exists
//    "late"             — has submission, past due, not yet approved by mentor
//    "awaiting_lecturer"— has submission, EscalatedToLecturer=1, not approved
//    "awaiting_mentor"  — has submission, MentorStatus='Pending', not escalated
//    "approved"         — MentorStatus='Approved'
// ─────────────────────────────────────────────────────────────────────────────

public class LecturerSubmissionOverviewRowDto
{
    public int       TaskId              { get; set; }
    /// <summary>Task/submission title — "type" column in the design.</summary>
    public string    TaskTitle           { get; set; } = "";
    public int       ProjectId           { get; set; }
    public int       ProjectNumber       { get; set; }
    public string    ProjectTitle        { get; set; } = "";
    public string?   TeamName            { get; set; }
    /// <summary>Comma-separated mentor names; null/empty when none assigned.</summary>
    public string?   MentorName          { get; set; }
    public string    MilestoneTitle      { get; set; } = "";
    public DateTime? EffectiveDueDate    { get; set; }
    public int?      SubmissionId        { get; set; }
    public DateTime? SubmittedAt         { get; set; }
    public string?   MentorStatus        { get; set; }
    public bool      EscalatedToLecturer { get; set; }
    /// <summary>One of: "missing"|"late"|"awaiting_lecturer"|"awaiting_mentor"|"approved"</summary>
    public string    ComputedStatus      { get; set; } = "";
    /// <summary>Positive when past due — how many calendar days overdue today.</summary>
    public int       DaysLate            { get; set; }
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
