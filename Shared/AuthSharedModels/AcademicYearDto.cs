using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>
/// One academic year row returned by the API.
/// Lifecycle: Active → Closed → Archived (driven by the Status column).
/// IsActive is kept for backward compatibility; Status takes precedence when present.
/// </summary>
public class AcademicYearDto
{
    public int      Id           { get; set; }
    public string   Name         { get; set; } = "";
    public DateTime StartDate    { get; set; }
    public DateTime EndDate      { get; set; }
    public bool     IsActive     { get; set; }
    public bool     IsCurrent    { get; set; }
    public DateTime CreatedAt    { get; set; }

    /// <summary>
    /// Lifecycle status: "Active" | "Closed" | "Archived".
    /// Derived server-side from the Status DB column + IsActive flag.
    /// </summary>
    public string   Status       { get; set; } = "Active";

    /// <summary>
    /// Number of distinct projects that have milestones in this academic year.
    /// Computed via AcademicYearMilestones → ProjectMilestones join.
    /// Ready to display; milestone detail management is a future expansion.
    /// </summary>
    public int      ProjectCount { get; set; }
}

/// <summary>
/// Payload for both creating and updating an academic year.
/// When IsCurrent = true the server clears IsCurrent on all other rows.
/// </summary>
public class SaveAcademicYearRequest
{
    public string   Name      { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate   { get; set; }
    public bool     IsActive  { get; set; }
    public bool     IsCurrent { get; set; }
}

/// <summary>
/// Summary returned by POST /api/academic-years/{id}/apply-templates.
/// Reports how many milestones and tasks were created vs already existed.
/// </summary>
public class ApplyTemplatesResultDto
{
    public int MilestonesCreated { get; set; }
    public int MilestonesSkipped { get; set; }
    public int TasksCreated      { get; set; }
    public int TasksSkipped      { get; set; }

    /// <summary>Total projects that were processed.</summary>
    public int ProjectsProcessed { get; set; }
}