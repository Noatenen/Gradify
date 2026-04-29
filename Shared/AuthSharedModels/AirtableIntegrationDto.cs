using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

public static class AirtableEntityTypes
{
    public const string Project = "Project";
    public const string Mentor  = "Mentor";
    public const string Student = "Student";
    public const string Team    = "Team";
}

/// <summary>Local field identifiers for project mappings — match AirtableFieldMap properties on the server.</summary>
public static class AirtableProjectFields
{
    public const string ProjectNumber    = "ProjectNumber";
    public const string Title            = "Title";
    public const string OrganizationName = "OrganizationName";
    public const string OrganizationType = "OrganizationType";
    public const string ProjectTopic     = "ProjectTopic";
    public const string Description      = "Description";
    public const string TargetAudience   = "TargetAudience";
    public const string Goals            = "Goals";
    public const string Contents         = "Contents";
    public const string ContactPerson    = "ContactPerson";
    public const string ContactRole      = "ContactRole";
    public const string ContactEmail     = "ContactEmail";
    public const string ContactPhone     = "ContactPhone";
    public const string IncludeInPool    = "IncludeInPool";
    public const string SubmittedAt      = "SubmittedAt";
    public const string ProjectType      = "ProjectType";
    public const string Status           = "Status";
    public const string Priority         = "Priority";
}

/// <summary>List/summary item — never carries the API token in the clear.</summary>
public class AirtableIntegrationListItemDto
{
    public int     Id                 { get; set; }
    public int     AcademicYearId     { get; set; }
    public string  AcademicYear       { get; set; } = "";
    public string  Name               { get; set; } = "";
    public string  BaseId             { get; set; } = "";
    public string  ProjectsTable      { get; set; } = "";
    public bool    IsActive           { get; set; }
    public bool    HasToken           { get; set; }
    public string  TokenMasked        { get; set; } = "";
    public string? LastTestedAt       { get; set; }
    public string? LastTestStatus     { get; set; }
    public string? LastImportAt       { get; set; }
    public string? LastImportSummary  { get; set; }
    public string  UpdatedAt          { get; set; } = "";
}

/// <summary>Full editor payload — token NEVER returned (only HasToken + masked).</summary>
public class AirtableIntegrationDetailDto
{
    public int     Id                 { get; set; }
    public int     AcademicYearId     { get; set; }
    public string  AcademicYear       { get; set; } = "";
    public string  Name               { get; set; } = "";
    public string  BaseId             { get; set; } = "";
    public string  ProjectsTable      { get; set; } = "";
    public string  ProjectsView       { get; set; } = "";
    public string  MentorsTable       { get; set; } = "";
    public string  MentorsView        { get; set; } = "";
    public string  StudentsTable      { get; set; } = "";
    public string  StudentsView       { get; set; } = "";
    public string  TeamsTable         { get; set; } = "";
    public string  TeamsView          { get; set; } = "";
    public bool    StudentVisibleOnly { get; set; } = true;
    public bool    IsActive           { get; set; }
    public bool    HasToken           { get; set; }
    public string  TokenMasked        { get; set; } = "";
    public string? LastTestedAt       { get; set; }
    public string? LastTestStatus     { get; set; }
    public string? LastImportAt       { get; set; }
    public string? LastImportSummary  { get; set; }
    public string  UpdatedAt          { get; set; } = "";
    public List<AirtableFieldMappingDto> Mappings { get; set; } = new();
}

public class AirtableFieldMappingDto
{
    public int    Id                { get; set; }
    public string EntityType        { get; set; } = AirtableEntityTypes.Project;
    public string LocalFieldName    { get; set; } = "";
    public string AirtableFieldName { get; set; } = "";
    public bool   IsRequired        { get; set; }
}

// ── Requests ─────────────────────────────────────────────────────────────────

public class SaveAirtableIntegrationRequest
{
    public int    AcademicYearId     { get; set; }
    public string Name               { get; set; } = "";
    public string ApiToken           { get; set; } = "";   // empty on PUT = keep existing
    public string BaseId             { get; set; } = "";
    public string ProjectsTable      { get; set; } = "";
    public string ProjectsView       { get; set; } = "";
    public string MentorsTable       { get; set; } = "";
    public string MentorsView        { get; set; } = "";
    public string StudentsTable      { get; set; } = "";
    public string StudentsView       { get; set; } = "";
    public string TeamsTable         { get; set; } = "";
    public string TeamsView          { get; set; } = "";
    public bool   StudentVisibleOnly { get; set; } = true;
    public bool   IsActive           { get; set; }
}

public class SaveAirtableMappingsRequest
{
    public List<AirtableFieldMappingDto> Mappings { get; set; } = new();
}

public class AirtableTestResultDto
{
    public bool    Success      { get; set; }
    public string  Message      { get; set; } = "";
    public int?    SampleCount  { get; set; }
    public string? Diagnostic   { get; set; }
}
