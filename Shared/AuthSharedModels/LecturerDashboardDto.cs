using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Lecturer / Mentor dashboard DTOs   —  GET /api/dashboard?scope=lecturer|mentor
//
//  Returned by LecturerDashboardController.GetOverview. The same shape is sent
//  to both scopes — only the WHERE clause changes server-side. Mentor scope
//  is enforced at the controller (cannot be widened by client).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Health bucket vocabulary used everywhere on the dashboard.</summary>
public static class HealthBuckets
{
    public const string Healthy   = "Healthy";   // 80–100
    public const string Attention = "Attention"; // 60–79
    public const string AtRisk    = "AtRisk";    // 0–59

    public static string Label(string b) => b switch
    {
        Healthy   => "תקין",
        Attention => "דורש תשומת לב",
        AtRisk    => "בסיכון",
        _         => b,
    };

    public static string FromScore(int score) => score switch
    {
        >= 80 => Healthy,
        >= 60 => Attention,
        _     => AtRisk,
    };
}

/// <summary>Top-level payload returned by the dashboard endpoint.</summary>
public class DashboardOverviewDto
{
    public string                  Scope    { get; set; } = "";
    public DashboardSummaryDto     Summary  { get; set; } = new();
    public DashboardChartsDto      Charts   { get; set; } = new();
    public List<DashboardProjectRowDto> Projects { get; set; } = new();
}

/// <summary>"תמונת מצב כללית" cards — top of the page.</summary>
public class DashboardSummaryDto
{
    public int TotalProjects        { get; set; }
    public int ActiveProjects       { get; set; }
    public int SubmissionsCompleted { get; set; }
    public int SubmissionsMissing   { get; set; }
    public int OverdueTasks         { get; set; }
    public int OpenRequests         { get; set; }
}

/// <summary>Aggregated counts for the simple visualizations.</summary>
public class DashboardChartsDto
{
    /// <summary>Project-health distribution: { Healthy, Attention, AtRisk } counts.</summary>
    public int HealthHealthy   { get; set; }
    public int HealthAttention { get; set; }
    public int HealthAtRisk    { get; set; }

    /// <summary>Submission split across all required submissions in scope.</summary>
    public int SubmissionsSubmitted { get; set; }
    public int SubmissionsNotYet    { get; set; }

    /// <summary>One bar per milestone — counts overdue mandatory tasks under it (scope-wide).</summary>
    public List<MilestoneOverdueBarDto> OverdueByMilestone { get; set; } = new();
}

public class MilestoneOverdueBarDto
{
    public int    ProjectMilestoneTemplateId { get; set; }
    public string MilestoneTitle             { get; set; } = "";
    public int    OverdueTaskCount           { get; set; }
}

/// <summary>One row in the project-list table, ready for display.</summary>
public class DashboardProjectRowDto
{
    public int       ProjectId            { get; set; }
    public int       ProjectNumber        { get; set; }
    public string    ProjectTitle         { get; set; } = "";
    public string?   TeamName             { get; set; }
    /// <summary>Comma-separated mentor names ("מנחה א', מנחה ב'") — empty when no mentors are assigned.</summary>
    public string?   MentorNames          { get; set; }
    public string?   CurrentMilestoneTitle { get; set; }
    public DateTime? CurrentMilestoneDueDate { get; set; }
    public int       OverdueTaskCount     { get; set; }
    public int       MissingSubmissionCount { get; set; }
    public int       OpenRequestCount     { get; set; }
    public int       HealthScore          { get; set; }
    public string    HealthBucket         { get; set; } = HealthBuckets.Healthy;
}
