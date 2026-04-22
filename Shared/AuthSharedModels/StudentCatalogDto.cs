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
    public string  Availability  { get; set; } = "Available";
}
