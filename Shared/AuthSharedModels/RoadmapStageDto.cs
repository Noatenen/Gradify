using System;
using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Roadmap stages ("ציר התקדמות פרויקט")
//
//  A thin layer above the existing milestone infrastructure. Each cycle
//  (AcademicYear) has an ordered list of stages with Hebrew names and
//  suggested dates; existing AcademicYearMilestones rows can be bound to a
//  stage via their RoadmapStageId column. The mentor view computes per-team
//  progress by aggregating ProjectMilestones rows that resolve to a stage.
// ─────────────────────────────────────────────────────────────────────────────

public class RoadmapStageDto
{
    public int       Id                 { get; set; }
    public int       AcademicYearId     { get; set; }
    public string    Code               { get; set; } = "";
    public string    Name               { get; set; } = "";
    public string    Description        { get; set; } = "";
    public DateTime? SuggestedStartDate { get; set; }
    public DateTime? SuggestedEndDate   { get; set; }
    public int       DisplayOrder       { get; set; }
    public bool      IsActive           { get; set; }
    public DateTime  CreatedAt          { get; set; }
    public DateTime  UpdatedAt          { get; set; }
    /// <summary>Milestone templates currently bound to this stage in this
    /// cycle (via AcademicYearMilestones.RoadmapStageId).</summary>
    public List<RoadmapStageMilestoneDto> LinkedMilestones { get; set; } = new();
}

/// <summary>One milestone template currently bound to a stage in a specific
/// cycle. Resolved via the AcademicYearMilestones join.</summary>
public class RoadmapStageMilestoneDto
{
    /// <summary>The AcademicYearMilestones row Id — this is what the link
    /// endpoint receives when binding/unbinding.</summary>
    public int    AcademicYearMilestoneId { get; set; }
    public int    MilestoneTemplateId     { get; set; }
    public string Title                   { get; set; } = "";
    public int    OrderIndex              { get; set; }
}

public class SaveRoadmapStageRequest
{
    /// <summary>Stable identifier (e.g., "Specification"). Required on
    /// create; ignored on update.</summary>
    public string    Code               { get; set; } = "";
    public string    Name               { get; set; } = "";
    public string    Description        { get; set; } = "";
    public DateTime? SuggestedStartDate { get; set; }
    public DateTime? SuggestedEndDate   { get; set; }
    public int       DisplayOrder       { get; set; }
    public bool      IsActive           { get; set; } = true;
}

public class ToggleRoadmapStageActiveRequest
{
    public bool IsActive { get; set; }
}

/// <summary>Replaces the full set of AcademicYearMilestone rows bound to a
/// stage in a single call. Items not in the list become unassigned (their
/// RoadmapStageId is cleared); items already bound to another stage are
/// re-bound. Only milestones in the same cycle are accepted.</summary>
public class LinkStageMilestonesRequest
{
    public List<int> AcademicYearMilestoneIds { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Project progress (mentor + admin views)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Roadmap progress for one project. Fed to the mentor project-detail
/// page and the mentor projects-list "current stage" chip.</summary>
public class ProjectRoadmapProgressDto
{
    public int                       ProjectId     { get; set; }
    public int                       AcademicYearId { get; set; }
    /// <summary>"OnTrack" | "Behind" | "Ahead" | "NoSchedule" — see
    /// RoadmapScheduleStatuses for label mapping.</summary>
    public string                    ScheduleStatus { get; set; } = RoadmapScheduleStatuses.NoSchedule;
    /// <summary>Stage code of the current (earliest-incomplete) stage. Empty
    /// when every stage is complete or no stages are configured.</summary>
    public string?                   CurrentStageCode { get; set; }
    /// <summary>Display name of the current stage, so consumers don't need to
    /// cross-reference Stages by CurrentStageCode. Null under the same
    /// conditions as CurrentStageCode.</summary>
    public string?                   CurrentStageName { get; set; }
    /// <summary>PHASE 1 — added by the business-logic consolidation epic
    /// (design/business-logic-consolidation-epic.md, Concept 2). Equal to
    /// Stages.FirstOrDefault(s =&gt; s.Status == Current)?.ProgressPct — the
    /// SAME number as that stage's own ProgressPct, exposed at the top level
    /// so no consumer has to find "the current one" in the list themselves.
    /// This is the "62%-style" number: how much of the CURRENT STAGE alone is
    /// done. Null when there is no current stage (all complete / none
    /// configured). Must always be labeled distinctly from
    /// OverallProjectProgressPct in any UI — they answer different
    /// questions and are never interchangeable.</summary>
    public int?                      CurrentStageProgressPct { get; set; }
    /// <summary>PHASE 1 — added by the business-logic consolidation epic.
    /// Average of ProgressPct across every stage with at least one linked
    /// milestone (stages with none don't contribute, so an unconfigured
    /// stage never misleadingly drags this toward 0). This is the
    /// "52%-style" whole-roadmap number — how far along the ENTIRE project
    /// is, not just the current stage. 0 when no stage has any linked
    /// milestones. Must always be labeled distinctly from
    /// CurrentStageProgressPct in any UI.</summary>
    public int                       OverallProjectProgressPct { get; set; }
    public List<StageProgressDto>    Stages         { get; set; } = new();
    /// <summary>Next milestone the team needs to deliver (earliest due-date
    /// among non-completed linked milestones in the current stage; falls back
    /// to the earliest overall non-completed milestone).</summary>
    public UpcomingMilestoneDto?     Upcoming       { get; set; }
    /// <summary>The most-overdue non-completed milestone, if any.</summary>
    public UpcomingMilestoneDto?     Overdue        { get; set; }
}

public class StageProgressDto
{
    public int       StageId            { get; set; }
    public string    Code               { get; set; } = "";
    public string    Name               { get; set; } = "";
    public string    Description        { get; set; } = "";
    public int       DisplayOrder       { get; set; }
    public DateTime? SuggestedStartDate { get; set; }
    public DateTime? SuggestedEndDate   { get; set; }
    /// <summary>"Completed" | "Current" | "Future" | "NotApplicable" — see
    /// RoadmapStageStatuses.</summary>
    public string    Status             { get; set; } = "";
    public int       LinkedMilestoneCount    { get; set; }
    public int       CompletedMilestoneCount { get; set; }
    /// <summary>0–100. Computed from CompletedMilestoneCount /
    /// LinkedMilestoneCount when linked milestones exist; otherwise 0 when
    /// the stage is Current or Future, 100 when treated as Completed.</summary>
    public int       ProgressPct        { get; set; }
    public List<StageMilestoneStateDto> Milestones { get; set; } = new();
}

public class StageMilestoneStateDto
{
    public int       ProjectMilestoneId { get; set; }
    public string    Title              { get; set; } = "";
    public string    Status             { get; set; } = "";
    public DateTime? DueDate            { get; set; }
    public int       OrderIndex         { get; set; }
}

public class UpcomingMilestoneDto
{
    public int       ProjectMilestoneId { get; set; }
    public string    Title              { get; set; } = "";
    public DateTime? DueDate            { get; set; }
    /// <summary>Days until DueDate. Negative when overdue. Null when
    /// DueDate is missing.</summary>
    public int?      DaysUntilDue       { get; set; }
}

/// <summary>Row returned by GET /api/roadmap-stages/mentor/projects-progress —
/// one entry per project the caller can see, with the full progress payload
/// inlined so the mentor overview is a single round-trip.</summary>
public class MentorRoadmapEntryDto
{
    public int     ProjectId     { get; set; }
    public int     ProjectNumber { get; set; }
    public string  ProjectTitle  { get; set; } = "";
    public string  TeamName      { get; set; } = "";
    public ProjectRoadmapProgressDto Progress { get; set; } = new();
}

// ── Controlled string constants ──────────────────────────────────────────────

public static class RoadmapStageStatuses
{
    public const string Completed     = "Completed";
    public const string Current       = "Current";
    public const string Future        = "Future";
    /// <summary>Stage has no linked milestones AND no usable date range —
    /// the calculator skips it for current-stage selection.</summary>
    public const string NotApplicable = "NotApplicable";

    public static string Label(string s) => s switch
    {
        Completed     => "הושלם",
        Current       => "שלב נוכחי",
        Future        => "עתיד",
        NotApplicable => "לא ישים",
        _             => s,
    };
}

public static class RoadmapScheduleStatuses
{
    public const string OnTrack    = "OnTrack";
    public const string Behind     = "Behind";
    public const string Ahead      = "Ahead";
    /// <summary>Returned when the current stage has no SuggestedEndDate /
    /// SuggestedStartDate, so on/behind/ahead cannot be derived.</summary>
    public const string NoSchedule = "NoSchedule";

    public static string Label(string s) => s switch
    {
        OnTrack    => "בלוח זמנים",
        Behind     => "מאחר את הלו״ז",
        Ahead      => "מקדים את הלו״ז",
        NoSchedule => "ללא לוח זמנים",
        _          => s,
    };
}