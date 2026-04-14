namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Project Catalog DTOs
//
//  A "catalog project" is a proposal entry in the Projects table.
//  TeamId IS NULL  → unassigned proposal (catalog-only)
//  TeamId IS NOT NULL → assigned to a team; became an active project
//
//  Source types:
//    "Manual"   — created directly inside Gradify by admin/staff
//    "Airtable" — synced from Airtable (future sync feature)
//
//  Priority values: null | "Low" | "Medium" | "High"
//  Status values for catalog:  "Available" | "Unavailable"
//  Status values for active projects: "InProgress" | "Completed" | etc.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One row in the catalog management table.</summary>
public class CatalogProjectListDto
{
    public int      Id                      { get; set; }
    public int      ProjectNumber           { get; set; }
    public string   Title                   { get; set; } = "";
    public string   ProjectType             { get; set; } = "";
    public int      ProjectTypeId           { get; set; }
    public int      AcademicYearId          { get; set; }
    public string   AcademicYear            { get; set; } = "";
    /// <summary>"Available" | "Unavailable" for catalog projects.</summary>
    public string   Status                  { get; set; } = "Available";
    public string   SourceType              { get; set; } = "Manual";
    public string?  OrganizationName        { get; set; }
    public string?  Priority                { get; set; }
    /// <summary>True when this proposal has been assigned a team.</summary>
    public bool     IsAssigned              { get; set; }
    /// <summary>First name+last of the first active team member (null if unassigned).</summary>
    public string?  AssignedFirstMemberName { get; set; }
    /// <summary>Total active team member count (0 if unassigned).</summary>
    public int      AssignedTeamSize        { get; set; }
    public DateTime CreatedAt               { get; set; }
}

/// <summary>Full detail view for a single catalog project (used in the detail panel).</summary>
public class CatalogProjectDetailDto
{
    public int       Id               { get; set; }
    public int       ProjectNumber    { get; set; }
    public string    Title            { get; set; } = "";
    public string?   Description      { get; set; }
    public string    ProjectType      { get; set; } = "";
    public int       ProjectTypeId    { get; set; }
    public int       AcademicYearId   { get; set; }
    public string    AcademicYear     { get; set; } = "";
    public string    Status           { get; set; } = "Available";
    // ── Source ───────────────────────────────────────────────────────────────
    public string    SourceType       { get; set; } = "Manual";
    public string?   AirtableRecordId { get; set; }
    // ── Proposal / intake fields ─────────────────────────────────────────────
    public string?   OrganizationName  { get; set; }
    public string?   ContactPerson     { get; set; }
    public string?   ContactRole       { get; set; }
    public string?   Goals             { get; set; }
    public string?   TargetAudience    { get; set; }
    // ── Internal management ───────────────────────────────────────────────────
    public string?   InternalNotes    { get; set; }
    public string?   Priority         { get; set; }
    // ── Assignment ────────────────────────────────────────────────────────────
    public bool      IsAssigned       { get; set; }
    public int?      TeamId           { get; set; }
    public string?   AssignedFirstMemberName { get; set; }
    public int       AssignedTeamSize { get; set; }
    // ── Timestamps ───────────────────────────────────────────────────────────
    public DateTime  CreatedAt        { get; set; }
    public DateTime? UpdatedAt        { get; set; }
}

/// <summary>Payload for creating or updating a catalog project entry.</summary>
public class SaveCatalogProjectRequest
{
    // ── Core (required) ──────────────────────────────────────────────────────
    public int     ProjectNumber  { get; set; }
    public string  Title          { get; set; } = "";
    public int     ProjectTypeId  { get; set; }
    public int     AcademicYearId { get; set; }
    // ── Content (optional) ───────────────────────────────────────────────────
    /// <summary>Problem / need statement — description.</summary>
    public string? Description    { get; set; }
    public string? Goals          { get; set; }
    public string? TargetAudience { get; set; }
    // ── Organization / contact (optional) ────────────────────────────────────
    public string? OrganizationName { get; set; }
    public string? ContactPerson    { get; set; }
    public string? ContactRole      { get; set; }
    // ── Internal management (optional) ───────────────────────────────────────
    public string? SourceType     { get; set; }   // "Manual" | "Airtable"
    public string? Priority       { get; set; }   // null | "Low" | "Medium" | "High"
    public string? Status         { get; set; }   // "Available" | "Unavailable"
    public string? InternalNotes  { get; set; }
}
