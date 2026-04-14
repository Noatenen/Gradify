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
}
