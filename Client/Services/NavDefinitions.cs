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
    // ── Admin (legacy shell, untouched) ─────────────────────────────────
    // Admin users keep the existing shell. No Motiva visual layer.
    private static readonly IReadOnlyList<NavItem> _adminMain = new[]
    {
        new NavItem("בית",               "lecturer/home",                 "oi-home",            NavLinkMatch.Prefix),
        new NavItem("פרויקטים",         "projects",                       "oi-folder",          NavLinkMatch.Prefix),
        new NavItem("בריאות פרויקטים",   "project-health",                 "oi-heart",           NavLinkMatch.Prefix),
        new NavItem("שיבוצים",           "assignments",                    "oi-target",          NavLinkMatch.Prefix),
        new NavItem("אבני דרך",          "milestones-overview",            "oi-flag",            NavLinkMatch.Prefix),
        new NavItem("בקשות",             "management/requests",            "oi-envelope-closed", NavLinkMatch.Prefix),
        new NavItem("אישורי מנחה",       "management/pending-mentor-approvals", "oi-shield",     NavLinkMatch.Prefix),
        new NavItem("חומרי עזר",         "resource-files",                 "oi-book",            NavLinkMatch.Prefix),
    };

    private static readonly IReadOnlyList<NavItem> _adminBottom = new[]
    {
        new NavItem("ניהול",   "management", "oi-list-rich", NavLinkMatch.All),
        new NavItem("הגדרות",  "settings",   "oi-cog",       NavLinkMatch.Prefix),
    };

    // ── Staff / Lecturer (Motiva shell) ──────────────────────────────────
    // Migrated in the Lecturer phase. Seven destinations matching the
    // Motiva Lecturer Home design reference nav:
    //   בית · פרויקטים · הגשות ובקרה · בקשות · יומן ותכנון · משאבים · ניהול
    //
    // Routes that keep working but are no longer in the primary nav:
    //   /dashboard/lecturer  → OverviewDashboardPage (legacy cohort view,
    //                          still reachable via /management or direct URL)
    //   /project-health      → still resolves
    //   /assignments         → still resolves
    //   /milestones-overview → still resolves
    //   /management/pending-mentor-approvals → accessible from the body
    //                          of LecturerHomePage and from /management
    //
    // /lecturer/submissions and /lecturer/calendar are not yet implemented.
    // Their nav items wire the routes consistently so future pages land in
    // the right place without a nav change.
    //
    // Same icon convention as Student/Mentor: open-iconic class kept as
    // the fallback; NavIcons path data is what the Motiva rail actually draws.
    private static readonly IReadOnlyList<NavItem> _lecturerMain = new[]
    {
        new NavItem("בית",              "lecturer/home",             "oi-home",            NavLinkMatch.Prefix, NavIcons.Home),
        new NavItem("פרויקטים",        "lecturer/projects",         "oi-folder",          NavLinkMatch.Prefix, NavIcons.Projects),
        new NavItem("הגשות ובקרה",     "lecturer/submissions",      "oi-task",            NavLinkMatch.Prefix, NavIcons.Submissions),
        new NavItem("בקשות",           "lecturer/requests",         "oi-envelope-closed", NavLinkMatch.Prefix, NavIcons.Requests),
        new NavItem("יומן ותכנון",     "lecturer/calendar",         "oi-calendar",        NavLinkMatch.Prefix, NavIcons.Calendar),
        new NavItem("משאבים",          "lecturer/resources",        "oi-book",            NavLinkMatch.Prefix, NavIcons.Knowledge),
        new NavItem("ניהול",           "management",                "oi-list-rich",       NavLinkMatch.All,    NavIcons.Management),
    };

    // Empty — הגדרות is reached from the profile card at the foot of the
    // rail, consistent with Student and Mentor.
    private static readonly IReadOnlyList<NavItem> _lecturerBottom = Array.Empty<NavItem>();

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

        /// <summary>Staff / Lecturer — the Motiva lecturer experience.
        /// Same token scope and visual system as Student and Mentor.</summary>
        Lecturer,

        /// <summary>Admin only — the legacy shell, untouched.
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
                : Shell.Lecturer;

        if (RoleService.IsMentor(user)) return Shell.Mentor;

        // Staff and Admin both land on the Lecturer shell.
        //
        // UserModeService.DefaultModeFor already returns UserModes.Lecturer for
        // any Admin- or Staff-only user (no Mentor role), so EffectiveMode for
        // an Admin-only account is "lecturer" out-of-the-box. Checking it here
        // rather than hard-coding the role means the shell follows the active
        // mode: if a future Admin-mode UX adds an "admin" mode to UserModes, the
        // gate below flips to Shell.Staff automatically without touching this
        // method. Today it always resolves to Shell.Lecturer for both roles.
        if (RoleService.IsStaff(user) || RoleService.IsAdmin(user))
            return UserModeService.EffectiveMode(user) == UserModes.Lecturer
                ? Shell.Lecturer
                : Shell.Staff;

        return Shell.Student; // safe fallback — matches GetNavItems
    }

    /// <summary>True for the three experiences that consume the Motiva System
    /// Master (Student, Mentor and Lecturer). This is the single predicate
    /// behind the `.motiva` token scope, the `.snav-motiva` rail, and the
    /// decision of where the notification bell is mounted.</summary>
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
            Shell.Student  => (_studentMain,  _studentBottom),
            Shell.Mentor   => (_mentorMain,   _mentorBottom),
            Shell.Lecturer => (_lecturerMain, _lecturerBottom),
            _              => (_adminMain,    _adminBottom),
        };
}
