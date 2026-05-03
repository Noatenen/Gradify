using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  RoleSettings — simple feature-flag matrix per role.
//  Decoupled from the larger Permissions / RolePermissions key-based system;
//  the two coexist. Pages and components consume these flags through the
//  PermissionService surface.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Canonical flag names — keep stable, used by both client and server.</summary>
public static class RoleFeatures
{
    public const string CanManageRequests        = "CanManageRequests";
    public const string CanManageMilestones      = "CanManageMilestones";
    public const string CanManageAssignments     = "CanManageAssignments";
    public const string CanManageUsers           = "CanManageUsers";
    public const string CanManageAirtable        = "CanManageAirtable";
    public const string CanOpenRequests          = "CanOpenRequests";
    public const string CanViewTasks             = "CanViewTasks";
    public const string CanSubmitTasks           = "CanSubmitTasks";
    public const string CanViewLecturerDashboard = "CanViewLecturerDashboard";

    public static readonly IReadOnlyList<string> All = new[]
    {
        CanManageRequests, CanManageMilestones, CanManageAssignments,
        CanManageUsers, CanManageAirtable,
        CanOpenRequests, CanViewTasks, CanSubmitTasks,
        CanViewLecturerDashboard,
    };

    /// <summary>Hebrew display label for the management page.</summary>
    public static string Label(string flag) => flag switch
    {
        CanManageRequests        => "ניהול בקשות",
        CanManageMilestones      => "ניהול אבני דרך",
        CanManageAssignments     => "ניהול שיבוצים",
        CanManageUsers           => "ניהול משתמשים",
        CanManageAirtable        => "ניהול Airtable",
        CanOpenRequests          => "פתיחת בקשות",
        CanViewTasks             => "צפייה במשימות",
        CanSubmitTasks           => "הגשת משימות",
        CanViewLecturerDashboard => "צפייה בדשבורד מרצה",
        _                        => flag,
    };
}

/// <summary>One row of the role-feature matrix.</summary>
public class RoleSettingsDto
{
    public string RoleName { get; set; } = "";

    public bool CanManageRequests        { get; set; }
    public bool CanManageMilestones      { get; set; }
    public bool CanManageAssignments     { get; set; }
    public bool CanManageUsers           { get; set; }
    public bool CanManageAirtable        { get; set; }
    public bool CanOpenRequests          { get; set; }
    public bool CanViewTasks             { get; set; }
    public bool CanSubmitTasks           { get; set; }
    public bool CanViewLecturerDashboard { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Payload for PUT /api/role-settings/{roleName}.</summary>
public class SaveRoleSettingsRequest
{
    public bool CanManageRequests        { get; set; }
    public bool CanManageMilestones      { get; set; }
    public bool CanManageAssignments     { get; set; }
    public bool CanManageUsers           { get; set; }
    public bool CanManageAirtable        { get; set; }
    public bool CanOpenRequests          { get; set; }
    public bool CanViewTasks             { get; set; }
    public bool CanSubmitTasks           { get; set; }
    public bool CanViewLecturerDashboard { get; set; }
}

/// <summary>Effective per-user feature set — UNION across the user's roles.
/// Returned by GET /api/role-settings/me.</summary>
public class CurrentUserFeaturesDto
{
    public int          UserId   { get; set; }
    public List<string> Roles    { get; set; } = new();
    /// <summary>Flags that are TRUE for at least one of the user's roles.</summary>
    public List<string> Features { get; set; } = new();
}
