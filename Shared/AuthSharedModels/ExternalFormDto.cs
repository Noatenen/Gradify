using System;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  External Forms — Innovation Team iframe-embedded form metadata.
//
//  The form bodies themselves live outside Gradify; we only store the URL +
//  metadata so admins/lecturers can swap the iframe link per cycle without
//  touching code. Students render the active form for their academic year
//  inside an <iframe>.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Read shape returned by /api/external-forms (admin/lecturer list) and
/// /api/external-forms/active (student-facing list). Includes the resolved
/// academic-year name when one is associated.
/// </summary>
public class ExternalFormDto
{
    public int      Id            { get; set; }
    public string   Name          { get; set; } = "";
    public string   Description   { get; set; } = "";
    /// <summary>Free-text category — e.g. "InnovationRequest", "Feedback".
    /// Treated as a label, not an enum, to stay flexible.</summary>
    public string   FormType      { get; set; } = "";
    public string   IframeUrl     { get; set; } = "";
    public bool     IsActive      { get; set; }
    /// <summary>Null = global (not tied to a specific academic year).</summary>
    public int?     AcademicYearId   { get; set; }
    /// <summary>Display name of the resolved academic year. Empty when global.</summary>
    public string   AcademicYearName { get; set; } = "";
    public DateTime? CreatedAt    { get; set; }
    public DateTime? UpdatedAt    { get; set; }
}

/// <summary>
/// Write shape for POST /api/external-forms and PUT /api/external-forms/{id}.
/// Name + IframeUrl are required; the rest are optional.
/// </summary>
public class ExternalFormSaveRequest
{
    public string  Name           { get; set; } = "";
    public string? Description    { get; set; }
    public string? FormType       { get; set; }
    public string  IframeUrl      { get; set; } = "";
    public bool    IsActive       { get; set; } = true;
    /// <summary>Null = global (not tied to a specific year).</summary>
    public int?    AcademicYearId { get; set; }
}