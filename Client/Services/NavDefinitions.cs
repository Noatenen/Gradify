using AuthWithAdmin.Client.Models;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Components.Routing;

namespace AuthWithAdmin.Client.Services;

/// <summary>
/// Defines which navigation items each role sees.
/// Returns (Main, Bottom) matching the sidebar's two-section layout.
/// To change a role's nav, edit only here — AppSideNav is untouched.
/// </summary>
public static class NavDefinitions
{
    // ── Admin / Staff (Lecturer) ─────────────────────────────────────
    private static readonly IReadOnlyList<NavItem> _adminMain = new[]
    {
        new NavItem("דשבורד",            "dashboard",             "oi-dashboard",             NavLinkMatch.All),
        new NavItem("פרויקטים",          "projects",              "oi-folder",                NavLinkMatch.Prefix),
        new NavItem("שיבוצים",           "assignments",           "oi-target",                NavLinkMatch.Prefix),
        new NavItem("אבני דרך",          "milestones",            "oi-flag",                  NavLinkMatch.Prefix),
        new NavItem("הגשות",             "lecturer-submissions",  "oi-inbox",                 NavLinkMatch.Prefix),
        new NavItem("בקשות",             "management/requests",   "oi-envelope-closed",       NavLinkMatch.Prefix),
    };

    private static readonly IReadOnlyList<NavItem> _adminBottom = new[]
    {
        new NavItem("ניהול",   "management",    "oi-list-rich", NavLinkMatch.All),
        new NavItem("חומרי עזר","resource-files","oi-folder",    NavLinkMatch.Prefix),
        new NavItem("הגדרות",  "settings",      "oi-cog",       NavLinkMatch.Prefix),
    };

    // ── Student ──────────────────────────────────────────────────────
    private static readonly IReadOnlyList<NavItem> _studentMain = new[]
    {
        new NavItem("דשבורד",          "dashboard",   "oi-dashboard",       NavLinkMatch.All),
        new NavItem("משימות",          "tasks",       "oi-task",            NavLinkMatch.Prefix),
        new NavItem("הגשות",           "submissions", "oi-document",        NavLinkMatch.Prefix),
        new NavItem("יומן",            "journal",     "oi-calendar",        NavLinkMatch.Prefix),
        new NavItem("בקשות",           "requests",    "oi-envelope-closed", NavLinkMatch.Prefix),
        new NavItem("אבני דרך",        "milestones",  "oi-flag",            NavLinkMatch.Prefix),
        new NavItem("חומרי עזר",       "files",       "oi-folder",          NavLinkMatch.Prefix),
        new NavItem("חומרי למידה",     "learning",    "oi-book",            NavLinkMatch.Prefix),
    };

    // Settings for students lives in the sidebar header as a compact icon button.
    private static readonly IReadOnlyList<NavItem> _studentBottom = Array.Empty<NavItem>();

    // ── Mentor ───────────────────────────────────────────────────────
    private static readonly IReadOnlyList<NavItem> _mentorMain = new[]
    {
        new NavItem("פרויקטים", "mentor/projects",     "oi-folder",   NavLinkMatch.Prefix),
        new NavItem("הגשות",    "mentor/submissions", "oi-inbox",    NavLinkMatch.Prefix),
        new NavItem("בקשות",    "management/requests", "oi-envelope-closed", NavLinkMatch.Prefix),
    };

    private static readonly IReadOnlyList<NavItem> _mentorBottom = new[]
    {
        new NavItem("חומרי עזר", "resource-files", "oi-folder", NavLinkMatch.Prefix),
        new NavItem("הגדרות",   "settings",        "oi-cog",    NavLinkMatch.Prefix),
    };

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Returns the (Main, Bottom) nav items for the given user's role.
    /// Falls back to the student view for unrecognised roles.
    /// </summary>
    public static (IReadOnlyList<NavItem> Main, IReadOnlyList<NavItem> Bottom) GetNavItems(User? user)
    {
        if (RoleService.IsStudent(user))      return (_studentMain, _studentBottom);
        if (RoleService.IsMentor(user))       return (_mentorMain,  _mentorBottom);
        if (RoleService.IsAdminOrStaff(user)) return (_adminMain,   _adminBottom);

        return (_studentMain, _studentBottom); // safe fallback
    }
}
