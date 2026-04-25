namespace AuthWithAdmin.Shared.AuthSharedModels;

public class AssignmentContextDto
{
    public StudentBasicDto             Me                { get; set; } = new();
    public bool                        HasTeam           { get; set; }
    public List<TeamMemberBasicDto>    TeamMembers       { get; set; } = new();
    public List<StudentBasicDto>       AvailableStudents { get; set; } = new();
    public List<AssignmentCatalogItemDto> Catalog        { get; set; } = new();
    public ExistingAssignmentDto?      ExistingSubmission { get; set; }
}

public class StudentBasicDto
{
    public int    Id       { get; set; }
    public string FullName { get; set; } = "";
}

public class TeamMemberBasicDto
{
    public int          UserId    { get; set; }
    public string       FullName  { get; set; } = "";
    public List<string> Strengths { get; set; } = new();
}

public class AssignmentCatalogItemDto
{
    public int     Id            { get; set; }
    public int     ProjectNumber { get; set; }
    public string  Title        { get; set; } = "";
    public string  ProjectType  { get; set; } = "";
    public string  Availability { get; set; } = "Available";
    public string? Description  { get; set; }
}

public class ExistingAssignmentDto
{
    public List<ProjectPreferenceDto> Preferences           { get; set; } = new();
    public bool                       HasOwnProject         { get; set; }
    public string                     OwnProjectDescription { get; set; } = "";
    public string                     Notes                 { get; set; } = "";
    public string                     SubmittedAt           { get; set; } = "";
}

public class ProjectPreferenceDto
{
    public int Priority  { get; set; }
    public int ProjectId { get; set; }
}

public class StudentStrengthDto
{
    public int    UserId   { get; set; }
    public string Strength { get; set; } = "";
}

public class SubmitAssignmentRequest
{
    public List<int>                  PartnerIds            { get; set; } = new();
    public List<StudentStrengthDto>   Strengths             { get; set; } = new();
    public List<ProjectPreferenceDto> Preferences           { get; set; } = new();
    public bool                       HasOwnProject         { get; set; }
    public string                     OwnProjectDescription { get; set; } = "";
    public string                     Notes                 { get; set; } = "";
}
