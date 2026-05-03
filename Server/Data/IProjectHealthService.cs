namespace AuthWithAdmin.Server.Data;

// ─────────────────────────────────────────────────────────────────────────────
//  IProjectHealthService — surface stub for the future project-health layer.
//
//  Purpose: define the read-side inputs the health computation will use, so
//  later wiring is one DI registration away. No implementation in this round.
//
//  Inputs the implementation will consume:
//    - Per-team milestone dates (default course-level dates from
//      MilestoneTemplates, copied into AcademicYearMilestones at cycle creation,
//      with TeamMilestoneDueDateOverrides applied per team).
//    - Per-team task overrides (TeamTaskDueDateOverrides).
//    - The IsUrgent SQL fragment used by GetMyTasks — overdue mandatory tasks
//      that are still open (a project with many of these is "at risk").
//    - Completed milestones (ProjectMilestones.Status = 'Completed').
//
//  This file deliberately stays empty of behavior. The contract below is the
//  promise to future-implementer code.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Future-facing project-health surface. Implementations will compute a
/// per-project health snapshot from milestone dates, overrides, and overdue
/// required tasks. Not yet wired — this interface exists so the API surface
/// is stable and DI registration is a one-liner when implementation lands.
/// </summary>
public interface IProjectHealthService
{
    /// <summary>
    /// Returns a snapshot describing the project's progress relative to
    /// effective milestone dates, the overdue-required-task count, and how
    /// many milestones are completed. Per-team overrides are applied internally.
    /// </summary>
    Task<ProjectHealthSnapshot?> ComputeAsync(int projectId);
}

/// <summary>Read-only result returned by IProjectHealthService.ComputeAsync.</summary>
public sealed class ProjectHealthSnapshot
{
    public int       ProjectId                 { get; init; }
    public int       MilestonesTotal           { get; init; }
    public int       MilestonesCompleted       { get; init; }
    /// <summary>Required tasks past their effective milestone due date that are still open.</summary>
    public int       OverdueRequiredTaskCount  { get; init; }
    /// <summary>The earliest still-active milestone effective due date (per team), or null when none remain.</summary>
    public DateTime? NextMilestoneEffectiveDue { get; init; }
    /// <summary>"Healthy" | "AtRisk" | "Delayed" — final string label the UI renders.</summary>
    public string    HealthLabel               { get; init; } = "Healthy";
}
