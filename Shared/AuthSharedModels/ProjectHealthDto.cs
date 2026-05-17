using System;
using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Project Health — internal traffic-light status computed per project from the
//  delay between actual milestone progress and the course schedule.
//
//    Green  : on track or ahead — no milestone delayed past its due date.
//    Orange : delay 1–14 days on the most-delayed milestone.
//    Red    : delay > 14 days on the most-delayed milestone.
//
//  CRITICAL — never expose any of this to students. The controller is gated
//  Admin/Staff/Mentor; the rows below must NOT be projected from any
//  student-facing endpoint.
// ─────────────────────────────────────────────────────────────────────────────

public static class ProjectHealthStatuses
{
    public const string Green  = "Green";
    public const string Orange = "Orange";
    public const string Red    = "Red";

    public static string Label(string status) => status switch
    {
        Green  => "תקין",
        Orange => "דורש תשומת לב",
        Red    => "בסיכון",
        _      => status,
    };
}

public class ProjectHealthRowDto
{
    public int      ProjectId          { get; set; }
    public int      ProjectNumber      { get; set; }
    public string   ProjectTitle       { get; set; } = "";

    /// <summary>Team display name; empty when no team is assigned yet.</summary>
    public string?  TeamName           { get; set; }
    /// <summary>Joined "First Last" student names for the team.</summary>
    public string?  StudentNames       { get; set; }
    /// <summary>Joined mentor names assigned to the project.</summary>
    public string?  MentorName         { get; set; }

    // ── The milestone that drives the status display ────────────────────────
    /// <summary>Title of the milestone shown in the row — the most-delayed one
    /// when the project is delayed, otherwise the current/next-up milestone.</summary>
    public string?   RelevantMilestoneTitle { get; set; }
    /// <summary>Effective due date of the relevant milestone (override-aware).</summary>
    public DateTime? PlannedDueDate         { get; set; }
    /// <summary>Set when the milestone has been completed; null otherwise.
    /// The page displays "טרם הוגש" when null.</summary>
    public DateTime? ActualCompletedAt      { get; set; }

    // ── Traffic light ───────────────────────────────────────────────────────
    /// <summary>"Green" / "Orange" / "Red".</summary>
    public string   Status             { get; set; } = ProjectHealthStatuses.Green;
    /// <summary>Days delayed on the most-delayed milestone. 0 when on track.</summary>
    public int      DelayDays          { get; set; }
}
