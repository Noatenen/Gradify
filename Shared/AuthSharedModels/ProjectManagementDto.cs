namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>One project row for the admin Projects Management table.</summary>
public class ProjectManagementDto
{
    public int     Id            { get; set; }
    public int     ProjectNumber { get; set; }
    public string  Title         { get; set; } = "";
    public string? Description   { get; set; }
    /// <summary>Raw status string from DB (e.g. "Active", "Inactive", "Archived").</summary>
    public string  Status        { get; set; } = "";
    public string? HealthStatus  { get; set; }
    public string  ProjectType   { get; set; } = "";
    public int     ProjectTypeId { get; set; }
    public int     TeamId        { get; set; }
    /// <summary>Count of active team members.</summary>
    public int     TeamSize      { get; set; }
    /// <summary>Academic year derived from the team's active members (e.g. "2025-2026").</summary>
    public string  AcademicYear  { get; set; } = "";
}

/// <summary>Payload for creating a new project (server auto-creates the team).</summary>
public class CreateProjectRequest
{
    public int     ProjectNumber { get; set; }
    public string  Title         { get; set; } = "";
    public string? Description   { get; set; }
    public int     ProjectTypeId { get; set; }
}

/// <summary>Slim project-type option for dropdowns.</summary>
public class ProjectTypeOptionDto
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Payload for PATCH status endpoint.</summary>
public class UpdateProjectStatusRequest
{
    public string Status { get; set; } = "";
}
