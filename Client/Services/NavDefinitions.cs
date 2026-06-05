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
    // Restructured 2026-05-21: side nav holds *operational, daily-work*
    // items only. System configuration lives in the "ניהול" area below.
    // The /dashboard legacy route still resolves so old bookmarks survive.
    private static readonly IReadOnlyList<NavItem> _adminMain = new[]
    {
        new NavItem("דשבורד מרצה",       "dashboard/lecturer",            "oi-pulse",           NavLinkMatch.Prefix),
        new NavItem("פרויקטים",         "projects",                       "oi-folder",          NavLinkMatch.Prefix),
        new NavItem("בריאות פרויקטים",   "project-health",                 "oi-heart",           NavLinkMatch.Prefix),
        new NavItem("שיבוצים",           "assignments",                    "oi-target",          NavLinkMatch.Prefix),
        // Cross-project milestones overview (not the student-style /milestones page).
        // Milestone TEMPLATE editing lives under /management/milestones.
        new NavItem("אבני דרך",          "milestones-overview",            "oi-flag",            NavLinkMatch.Prefix),
        // Operational request inbox. The /management/requests URL is the
        // existing page — kept so deep links keep working — but it now
        // surfaces as a primary side-nav item, not a management card.
        new NavItem("בקשות",             "management/requests",            "oi-envelope-closed", NavLinkMatch.Prefix),
        // Pending mentor approvals as a daily work-queue surface.
        new NavItem("אישורי מנחה",       "management/pending-mentor-approvals", "oi-shield",     NavLinkMatch.Prefix),
        // Learning Materials — promoted from the management area so lecturers
        // can upload / categorize without an extra click through "ניהול".
        new NavItem("חומרי עזר",         "resource-files",                 "oi-book",            NavLinkMatch.Prefix),
    };

    private static readonly IReadOnlyList<NavItem> _adminBottom = new[]
    {
        new NavItem("ניהול",   "management", "oi-list-rich", NavLinkMatch.All),
        new NavItem("הגדרות",  "settings",   "oi-cog",       NavLinkMatch.Prefix),
    };

    // ── Student ──────────────────────────────────────────────────────
    private static readonly IReadOnlyList<NavItem> _studentMain = new[]
    {
        new NavItem("דשבורד",          "dashboard",         "oi-dashboard",       NavLinkMatch.All),
        new NavItem("משימות",          "tasks",             "oi-task",            NavLinkMatch.Prefix),
        new NavItem("הגשות",           "submissions",       "oi-document",        NavLinkMatch.Prefix),
        new NavItem("יומן",            "journal",           "oi-calendar",        NavLinkMatch.Prefix),
        new NavItem("בקשות",           "requests",          "oi-envelope-closed", NavLinkMatch.Prefix),
        new NavItem("צוות חדשנות",     "external-requests", "oi-lightbulb",       NavLinkMatch.Prefix),
        new NavItem("אבני דרך",        "milestones",        "oi-flag",            NavLinkMatch.Prefix),
        new NavItem("חומרי עזר",       "files",             "oi-folder",          NavLinkMatch.Prefix),
        new NavItem("חומרי למידה",     "learning",          "oi-book",            NavLinkMatch.Prefix),
    };

    // Sidebar settings entry — same row position as Mentor / Staff so the
    // הגדרות nav item appears in a consistent place for every role.
    private static readonly IReadOnlyList<NavItem> _studentBottom = new[]
    {
        new NavItem("הגדרות", "settings", "oi-cog", NavLinkMatch.Prefix),
    };

    // ── Mentor ───────────────────────────────────────────────────────
    private static readonly IReadOnlyList<NavItem> _mentorMain = new[]
    {
        new NavItem("דשבורד מנחה", "dashboard/mentor",     "oi-pulse",            NavLinkMatch.Prefix),
        new NavItem("פרויקטים",    "mentor/projects",      "oi-folder",           NavLinkMatch.Prefix),
        new NavItem("ציר התקדמות", "mentor/roadmap",       "oi-graph",            NavLinkMatch.Prefix),
        new NavItem("בריאות פרויקטים", "project-health",   "oi-heart",            NavLinkMatch.Prefix),
        new NavItem("אבני דרך",    "milestones-overview",  "oi-flag",             NavLinkMatch.Prefix),
        new NavItem("הגשות",       "mentor/submissions",   "oi-inbox",            NavLinkMatch.Prefix),
        new NavItem("בקשות",       "mentor-requests",      "oi-envelope-closed",  NavLinkMatch.Prefix),
    };

    private static readonly IReadOnlyList<NavItem> _mentorBottom = new[]
    {
        new NavItem("חומרי עזר", "resource-files", "oi-folder", NavLinkMatch.Prefix),
        new NavItem("הגדרות",   "settings",        "oi-cog",    NavLinkMatch.Prefix),
    };

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Returns the (Main, Bottom) nav items for the given user's role.
    /// Students take priority (they only have one role). For users who hold
    /// *both* Staff and Mentor roles, the active view mode from
    /// <see cref="UserModeService"/> decides which nav set is rendered —
    /// flipping the toggle re-runs this method via AppSideNav's subscription.
    /// Falls back to the student view for unrecognised roles.
    /// </summary>
    public static (IReadOnlyList<NavItem> Main, IReadOnlyList<NavItem> Bottom) GetNavItems(User? user)
    {
        if (RoleService.IsStudent(user)) return (_studentMain, _studentBottom);

        // Dual-role users (Staff + Mentor): the active mode picks.
        if (UserModeService.IsDualRole(user))
        {
            return UserModeService.EffectiveMode(user) == UserModes.Mentor
                ? (_mentorMain, _mentorBottom)
                : (_adminMain,  _adminBottom);
        }

        if (RoleService.IsMentor(user))       return (_mentorMain,  _mentorBottom);
        if (RoleService.IsAdminOrStaff(user)) return (_adminMain,   _adminBottom);

        return (_studentMain, _studentBottom); // safe fallback
    }
}
