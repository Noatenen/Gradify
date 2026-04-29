using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>Canonical permission keys (string constants, kept stable).</summary>
public static class Permissions
{
    public const string ProjectCatalog_View              = "ProjectCatalog.View";
    public const string AssignmentForm_Submit            = "AssignmentForm.Submit";
    public const string AssignmentForm_Edit              = "AssignmentForm.Edit";

    public const string AssignmentManagement_ViewSubmissions = "AssignmentManagement.ViewSubmissions";
    public const string AssignmentManagement_ManageForm      = "AssignmentManagement.ManageForm";
    public const string AssignmentManagement_ViewAnalytics   = "AssignmentManagement.ViewAnalytics";
    public const string AssignmentManagement_ManualAssign    = "AssignmentManagement.ManualAssign";
    public const string AssignmentManagement_AssignMentor    = "AssignmentManagement.AssignMentor";
    public const string AssignmentManagement_Publish         = "AssignmentManagement.Publish";

    public const string ProjectManagement_View           = "ProjectManagement.View";
    public const string ProjectManagement_Create         = "ProjectManagement.Create";
    public const string ProjectManagement_Edit           = "ProjectManagement.Edit";
    public const string ProjectManagement_Delete         = "ProjectManagement.Delete";
    public const string ProjectManagement_ImportAirtable = "ProjectManagement.ImportAirtable";

    public const string FormsManagement_View   = "FormsManagement.View";
    public const string FormsManagement_Edit   = "FormsManagement.Edit";
    public const string FormsManagement_Delete = "FormsManagement.Delete";

    public const string Integrations_View              = "Integrations.View";
    public const string Integrations_ManageAirtable    = "Integrations.ManageAirtable";
    public const string Integrations_TestAirtable      = "Integrations.TestAirtable";
    public const string Integrations_RunAirtableImport = "Integrations.RunAirtableImport";

    public const string Users_View      = "Users.View";
    public const string Users_Manage    = "Users.Manage";
    public const string Teams_Manage    = "Teams.Manage";
    public const string Mentors_Manage  = "Mentors.Manage";

    public const string Permissions_Manage = "Permissions.Manage";
}

public class PermissionDto
{
    public int    Id          { get; set; }
    public string Key         { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string GroupName   { get; set; } = "";
    public string Description { get; set; } = "";
    public int    SortOrder   { get; set; }
}

public class RoleSummaryDto
{
    public string RoleName        { get; set; } = "";
    public string DisplayName     { get; set; } = "";
    public string Description     { get; set; } = "";
    public int    UserCount       { get; set; }
    public int    PermissionCount { get; set; }
}

public class RolePermissionsDto
{
    public string         RoleName      { get; set; } = "";
    public string         DisplayName   { get; set; } = "";
    public string         Description   { get; set; } = "";
    public List<string>   Permissions   { get; set; } = new();
    public List<PermissionDto> Catalog  { get; set; } = new();
}

public class SaveRolePermissionsRequest
{
    public List<string> Permissions { get; set; } = new();
}

public class CurrentUserPermissionsDto
{
    public int          UserId      { get; set; }
    public List<string> Roles       { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
}
