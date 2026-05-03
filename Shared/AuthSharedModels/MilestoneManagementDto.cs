using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Milestone Management DTOs (admin area)
//
//  Applicability convention on MilestoneTemplates.ProjectTypeId:
//    NULL  → applies to BOTH project types (shared / universal)
//    1     → Technological projects only
//    2     → Methodological projects only
//
//  These DTOs cover the admin management tier only.
//  Student-facing milestone DTOs live in MilestonesPageDto.cs.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One row in the milestone templates management table.</summary>
public class MilestoneTemplateDto
{
    public int     Id            { get; set; }
    public string  Title         { get; set; } = "";
    public string? Description   { get; set; }
    public int     OrderIndex    { get; set; }
    public bool    IsRequired    { get; set; }
    public bool    IsActive      { get; set; }
    /// <summary>Null means applies to both types.</summary>
    public int?    ProjectTypeId { get; set; }
    /// <summary>
    /// Resolved display name: "שניהם" | "טכנולוגי" | "מתודולוגי".
    /// Set server-side so the client doesn't need to resolve the FK.
    /// </summary>
    public string  Applicability { get; set; } = "שניהם";

    // ── Default course-level dates (Option A) ────────────────────────────
    // These are the GLOBAL defaults a milestone has at the template level.
    // They get copied into AcademicYearMilestones when a new cycle is built,
    // and per-team adjustments still go through TeamMilestoneDueDateOverrides.
    public DateTime? OpenDate  { get; set; }
    public DateTime? DueDate   { get; set; }
    public DateTime? CloseDate { get; set; }
}

/// <summary>Payload for creating or updating a milestone template.</summary>
public class SaveMilestoneTemplateRequest
{
    public string  Title         { get; set; } = "";
    public string? Description   { get; set; }
    public int     OrderIndex    { get; set; }
    public bool    IsRequired    { get; set; } = true;
    public bool    IsActive      { get; set; } = true;
    /// <summary>Null = both types; 1 = Technological; 2 = Methodological.</summary>
    public int?    ProjectTypeId { get; set; }

    /// <summary>Default course-level open date — nullable.</summary>
    public DateTime? OpenDate  { get; set; }
    /// <summary>Default course-level due date — nullable.</summary>
    public DateTime? DueDate   { get; set; }
    /// <summary>Default course-level close date — nullable.</summary>
    public DateTime? CloseDate { get; set; }
}
