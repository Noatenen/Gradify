using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>
/// A project available for student assignment selection.
/// InternalNotes and admin-only fields are intentionally excluded.
/// </summary>
public class StudentCatalogProjectDto
{
    public int     Id            { get; set; }
    public int     ProjectNumber { get; set; }
    public string  Title         { get; set; } = "";
    public string  Description   { get; set; } = "";
    public string  ProjectType   { get; set; } = "";
    /// <summary>Comma-separated list of mentor full names (may be empty).</summary>
    public string  Mentors       { get; set; } = "";
    /// <summary>"Available" | "Taken" | "Full" — server-computed.</summary>
    public string  Availability      { get; set; } = "Available";
    /// <summary>True when the requesting student has bookmarked this project.</summary>
    public bool    IsFavorite        { get; set; }

    // ── Extended fields from DB (populated by Airtable sync or manual entry) ──
    public string? OrganizationName  { get; set; }
    public string? OrganizationType  { get; set; }
    public string? ContactPerson     { get; set; }
    public string? ContactRole       { get; set; }
    public string? ContactEmail      { get; set; }
    public string? ContactPhone      { get; set; }
    public string? Goals             { get; set; }
    public string? TargetAudience    { get; set; }
    public string? ProjectTopic      { get; set; }
    /// <summary>Detailed project contents / scope (separate from Description).</summary>
    public string? Contents          { get; set; }
}
