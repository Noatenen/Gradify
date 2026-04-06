namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>
/// Full milestones page payload returned by GET /api/projects/my-milestones.
/// Summary counts are pre-aggregated server-side; client receives ready-to-render data.
/// </summary>
public class MilestonesPageDto
{
    public int    TotalCount           { get; set; }
    public int    CompletedCount       { get; set; }
    /// <summary>Name of the current (InProgress → Delayed → first NotStarted) milestone.</summary>
    public string CurrentMilestoneName { get; set; } = "";
    public List<MilestoneItemDto> Milestones { get; set; } = new();
}

/// <summary>
/// One milestone row with pre-calculated progress values.
/// Progress % = CompletedTasks / TotalTasks * 100.
/// When TotalTasks == 0: 100 if Status=Completed, else 0.
/// IsCurrent is set by the server using the same priority rule as the sidebar widget.
/// </summary>
public class MilestoneItemDto
{
    public int       ProjectMilestoneId { get; set; }
    public string    Title              { get; set; } = "";
    /// <summary>NotStarted | InProgress | Delayed | Completed</summary>
    public string    Status             { get; set; } = "";
    public int       OrderIndex         { get; set; }
    public DateTime? DueDate            { get; set; }
    public DateTime? CompletedAt        { get; set; }
    public int       TotalTasks         { get; set; }
    public int       CompletedTasks     { get; set; }
    /// <summary>Integer 0-100 — pre-calculated server-side.</summary>
    public int       ProgressPct        { get; set; }
    /// <summary>True for exactly one milestone: the "current" one.</summary>
    public bool      IsCurrent          { get; set; }
}
