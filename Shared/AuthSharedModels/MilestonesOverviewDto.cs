using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Lecturer / Admin "אבני דרך" overview
//
//  Cross-project milestone snapshot. Each row represents one project's
//  CURRENT milestone (the first non-completed milestone by order). Globals
//  are never compared raw — every date uses the standard effective chain
//  (TeamTaskDueDateOverrides → TeamMilestoneDueDateOverrides → global).
//
//  Server endpoint: GET /api/milestones/overview?scope=lecturer|mentor
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Stable status vocabulary used across the table + filters.</summary>
public static class MilestoneOverviewStatuses
{
    public const string Healthy        = "Healthy";        // תקין
    public const string Overdue        = "Overdue";        // באיחור
    public const string NeedsAttention = "NeedsAttention"; // דורש תשומת לב

    public static string Label(string s) => s switch
    {
        Healthy        => "תקין",
        Overdue        => "באיחור",
        NeedsAttention => "דורש תשומת לב",
        _              => s,
    };
}

public class MilestonesOverviewDto
{
    public string                          Scope     { get; set; } = "";
    public MilestonesOverviewSummaryDto    Summary   { get; set; } = new();
    public MilestonesOverviewFiltersDto    Filters   { get; set; } = new();
    public List<MilestonesOverviewRowDto>  Rows      { get; set; } = new();
}

public class MilestonesOverviewSummaryDto
{
    public int TotalActiveProjects { get; set; }
    public int OverdueMilestones   { get; set; }
    public int CompletedTasks      { get; set; }
    public int OpenTasks           { get; set; }
    public int TeamsNeedingAttention { get; set; }
}

/// <summary>Distinct dropdown options the page populates from real data.</summary>
public class MilestonesOverviewFiltersDto
{
    /// <summary>Distinct ProjectType names across the rows.</summary>
    public List<string> ProjectTypes { get; set; } = new();
    /// <summary>Distinct current-milestone titles across the rows.</summary>
    public List<string> Milestones   { get; set; } = new();
    /// <summary>Distinct mentor names across the rows ("comma-separated names" exploded).</summary>
    public List<string> Mentors      { get; set; } = new();
}

public class MilestonesOverviewRowDto
{
    public int       ProjectId               { get; set; }
    public int       ProjectNumber           { get; set; }
    public string    ProjectTitle            { get; set; } = "";
    public string?   TeamName                { get; set; }
    public string?   MentorNames             { get; set; }
    public string    ProjectType             { get; set; } = "";
    public string?   CurrentMilestoneTitle   { get; set; }
    public DateTime? CurrentMilestoneDueDate { get; set; }
    public int       MilestoneTasksCompleted { get; set; }
    public int       MilestoneTasksTotal     { get; set; }
    public int       MissingSubmissionCount  { get; set; }
    /// <summary>One of MilestoneOverviewStatuses.*</summary>
    public string    Status                  { get; set; } = MilestoneOverviewStatuses.Healthy;
}
