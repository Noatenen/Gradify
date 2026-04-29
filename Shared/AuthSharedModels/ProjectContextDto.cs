using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>
/// Project identity and sidebar widget data for the authenticated student.
/// Fetched once per session via GET /api/projects/my-context and cached
/// in ProjectContextService for the lifetime of the browser tab.
/// </summary>
public class ProjectContextDto
{
    // ── Identity (always shown) ───────────────────────────────────────────────
    public int    ProjectId     { get; set; }
    public int    ProjectNumber { get; set; }
    public string ProjectTitle  { get; set; } = "";

    // ── Team quick-info (for the sidebar TeamQuickInfoPopover) ────────────────
    // Fetched in the same /api/projects/my-context call so the popover opens
    // instantly with no extra HTTP request. Names + emails are kept as
    // parallel lists, ordered identically.
    public string?      TeamName      { get; set; }
    public string?      TrackName     { get; set; }
    public List<string> StudentNames  { get; set; } = new();
    public List<string> StudentEmails { get; set; } = new();
    public List<string> MentorNames   { get; set; } = new();
    public List<string> MentorEmails  { get; set; } = new();

    // ── Widget card 1: current milestone ─────────────────────────────────────
    // "Current" = first InProgress → first Delayed → first NotStarted
    public string?   CurrentMilestoneTitle   { get; set; }
    public string?   CurrentMilestoneStatus  { get; set; }
    public DateTime? CurrentMilestoneDueDate { get; set; }

    // ── Widget card 2: overall progress ──────────────────────────────────────
    public int MilestonesCompleted { get; set; }
    public int MilestonesTotal     { get; set; }
    public int TasksDone           { get; set; }
    public int TasksTotal          { get; set; }

    // ── Widget card 3: next open task (null → card 3 hidden) ─────────────────
    public string?   NextTaskTitle   { get; set; }
    public DateTime? NextTaskDueDate { get; set; }
}
