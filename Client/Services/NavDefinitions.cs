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
    // Aligned in Phase 4D with the System Master's student nav, which lists
    // exactly five destinations (Motiva Student Dashboard V4.dc.html:68-72):
    // דשבורד · המשימות שלי · יומן ותכנון · בקשות · מרכז ידע.
    //
    // Removed here (routes, pages, services and APIs all left intact):
    //   • "התראות ועדכונים" (/notifications) — the notification bell in the
    //     sidebar header is the live notifications surface; the page it
    //     pointed at is a self-declared placeholder, so this was a duplicate
    //     destination for something the bell already owns.
    //   • "צוות החדשנות" (/external-requests) — not in the Master's nav. The
    //     page, its webhook integration and api/external-requests stay exactly
    //     as they are; only this entry point is withdrawn, to be re-surfaced
    //     from the Requests experience in a later phase.
    //
    // Added: "יומן ותכנון" → the EXISTING /milestones page. This is a nav
    // entry for a route that already shipped, not a new screen. It also
    // replaces the calendar shortcut that AppTopBar used to provide, which
    // disappears for students now that the bar is hidden on /dashboard.
    // Each entry carries BOTH icons: the open-iconic class, which is what it
    // always had, and the stroked SVG the student rail draws instead (see
    // NavIcons for why). The open-iconic value is deliberately left in place
    // rather than deleted — it is the fallback the rail falls back to for any
    // item without path data, and removing it would make this list role-
    // specific when it is not.
    private static readonly IReadOnlyList<NavItem> _studentMain = new[]
    {
        new NavItem("דשבורד",      "dashboard",  "oi-dashboard",       NavLinkMatch.All,    NavIcons.Dashboard),
        new NavItem("המשימות שלי", "tasks",      "oi-task",            NavLinkMatch.Prefix, NavIcons.Tasks),
        // Match.All, not Prefix: the admin/mentor "milestones-overview" route
        // would otherwise light this item up as active under a prefix match.
        new NavItem("יומן ותכנון", "milestones", "oi-calendar",        NavLinkMatch.All,    NavIcons.Calendar),
        new NavItem("בקשות",       "requests",   "oi-envelope-closed", NavLinkMatch.Prefix, NavIcons.Requests),
        new NavItem("מרכז ידע",    "learning",   "oi-book",            NavLinkMatch.Prefix, NavIcons.Knowledge),
    };

    // Empty: students reach /settings via the profile card at the bottom
    // of the sidebar (AppSideNav's .snav-profile-footer), so a duplicate
    // nav row here would be redundant.
    private static readonly IReadOnlyList<NavItem> _studentBottom = Array.Empty<NavItem>();

    // ── Mentor ───────────────────────────────────────────────────────
    // Restructured in Epic 1 to the six destinations of the mentor design
    // reference (design-reference/mentor-experience/project/Motiva Mentor
    // Home.dc.html:36-46 and every sibling screen, which all draw the same
    // rail): בית · המשימות שלי · הפרויקטים שלי · בקשות · יומן ותכנון ·
    // משאבים למנחים.
    //
    // WITHDRAWN FROM THE PRIMARY NAV — NOT DELETED
    //   ציר התקדמות     (/mentor/roadmap)
    //   בריאות פרויקטים  (/project-health)
    //   אבני דרך         (/milestones-overview)
    //   הגשות            (/mentor/submissions)
    //
    // Every one of those routes, pages, services and APIs is untouched and
    // still resolves — this list is the only thing that changed. Their
    // information is planned to return contextually: project health inside
    // הפרויקטים שלי and מרחב הפרויקט, milestones/roadmap inside מרחב
    // הפרויקט, submissions awaiting review inside המשימות שלי and מרחב
    // הפרויקט. None of those integrations exists yet.
    //
    // Same convention as the student list: every entry carries BOTH icons —
    // the open-iconic class it always had (the rail's fallback for an item
    // with no path data) and the stroked SVG the Motiva rail actually draws.
    private static readonly IReadOnlyList<NavItem> _mentorMain = new[]
    {
        // Match.Prefix, so /dashboard/mentor stays lit. The route belongs to
        // MentorHomePage as of Phase A; OverviewDashboardPage kept
        // /dashboard/lecturer and is no longer reachable from this rail.
        new NavItem("בית",            "dashboard/mentor", "oi-home",            NavLinkMatch.Prefix, NavIcons.Home),
        new NavItem("המשימות שלי",    "mentor/tasks",     "oi-task",            NavLinkMatch.Prefix, NavIcons.Tasks),
        new NavItem("הפרויקטים שלי",  "mentor/projects",  "oi-folder",          NavLinkMatch.Prefix, NavIcons.Projects),
        new NavItem("בקשות",          "mentor-requests",  "oi-envelope-closed", NavLinkMatch.Prefix, NavIcons.Requests),
        new NavItem("יומן ותכנון",    "mentor/calendar",  "oi-calendar",        NavLinkMatch.Prefix, NavIcons.Calendar),
        // The mentor's own read-only view of the shared library.
        //
        // This pointed at /resource-files until Phase B, which was a real bug,
        // not just a styling gap: that page is [Authorize(Admin, Staff)], so a
        // mentor following their own nav item was refused. /mentor/resources
        // reads the same corpus through a mentor-authorised endpoint.
        new NavItem("משאבים למנחים",  "mentor/resources", "oi-book",            NavLinkMatch.Prefix, NavIcons.Knowledge),
    };

    // Empty, like the student's: הגדרות is reached from the profile card at
    // the foot of the rail (AppSideNav's .snav-profile-footer), which is what
    // the mentor design draws too — a second row here would be a duplicate
    // destination for something the footer already owns.
    private static readonly IReadOnlyList<NavItem> _mentorBottom = Array.Empty<NavItem>();

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Which of the three shells a user gets. Declared here, next to the nav
    /// lists themselves, because the shell and the navigation must resolve the
    /// same way for the same user — including the dual-role case, where the
    /// active view mode decides. Splitting that logic across AppLayout,
    /// AppSideNav and AppTopBar is what would let them disagree.
    /// </summary>
    public enum Shell
    {
        /// <summary>The finalized Motiva student experience.</summary>
        Student,

        /// <summary>The Motiva mentor experience — same visual system as
        /// <see cref="Student"/>, different information architecture.</summary>
        Mentor,

        /// <summary>Lecturer / Staff / Admin — the legacy shell, untouched.
        /// Resolves every token at its :root value.</summary>
        Staff
    }

    /// <summary>
    /// Resolves the shell for a user. Mirrors <see cref="GetNavItems"/>
    /// exactly — the two share this method rather than re-deriving the answer.
    /// </summary>
    public static Shell GetShell(User? user)
    {
        if (RoleService.IsStudent(user)) return Shell.Student;

        // Dual-role users (Staff + Mentor): the active mode picks.
        if (UserModeService.IsDualRole(user))
            return UserModeService.EffectiveMode(user) == UserModes.Mentor
                ? Shell.Mentor
                : Shell.Staff;

        if (RoleService.IsMentor(user))       return Shell.Mentor;
        if (RoleService.IsAdminOrStaff(user)) return Shell.Staff;

        return Shell.Student; // safe fallback — matches GetNavItems
    }

    /// <summary>True for the two experiences that consume the Motiva System
    /// Master (Student and Mentor). This is the single predicate behind the
    /// `.motiva` token scope, the `.snav-motiva` rail, and the decision of
    /// where the notification bell is mounted.</summary>
    public static bool IsMotivaShell(User? user) => GetShell(user) != Shell.Staff;

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
        => GetShell(user) switch
        {
            Shell.Student => (_studentMain, _studentBottom),
            Shell.Mentor  => (_mentorMain,  _mentorBottom),
            _             => (_adminMain,   _adminBottom),
        };
}
