using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  StudentProjectDetailsDto
//
//  Student-safe view of a project's full details.
//  Excludes all internal/management fields:
//    HealthStatus, Priority, InternalNotes, SourceType, AirtableRecordId,
//    TeamId, IsAssigned, assignment member counts.
//
//  Returned by GET /api/projects/my-project-details.
//  The endpoint is scoped to the requesting user's own project only.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Full project details for the student dashboard modal.</summary>
public class StudentProjectDetailsDto
{
    public int    Id            { get; set; }
    public int    ProjectNumber { get; set; }
    public string Title         { get; set; } = "";
    public string ProjectType   { get; set; } = "";
    public string AcademicYear  { get; set; } = "";

    // ── Main content ─────────────────────────────────────────────────────────
    /// <summary>Problem/need statement.</summary>
    public string? Description    { get; set; }
    public string? Goals          { get; set; }
    public string? TargetAudience { get; set; }
    /// <summary>High-level topic (Airtable-sourced; may be null for manual entries).</summary>
    public string? ProjectTopic   { get; set; }
    /// <summary>Extended content / scope description (Airtable-sourced).</summary>
    public string? Contents       { get; set; }

    // ── Organization / client contact ────────────────────────────────────────
    public string? OrganizationName { get; set; }
    public string? OrganizationType { get; set; }
    public string? ContactPerson    { get; set; }
    public string? ContactRole      { get; set; }
    public string? ContactEmail     { get; set; }
    public string? ContactPhone     { get; set; }
}
