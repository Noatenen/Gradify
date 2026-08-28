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
    // EIGHT destinations. המשימות שלי joined the seven below — see the note on
    // that entry; the other seven match the approved Motiva Lecturer design
    // (design/design-system/MotivaLecturerDesign/דשבורד מרצה 268.pdf).
    // The pill nav renders these identically to the student and mentor
    // bars — same pill track, same badge mechanism, same action cluster.
    //
    // All eight destinations from the previous side-nav remain accessible
    // at their existing routes; only this list (what appears in the chrome)
    // changed. /project-health, /management/pending-mentor-approvals and
    // the חומרי עזר side-nav entry are still reachable via /management or
    // direct URL — they were not removed, just withdrawn from the primary nav.
    private static readonly IReadOnlyList<NavItem> _adminMain = new[]
    {
        // Match.All: prevents /dashboard/lecturer/anything from keeping "בית" lit.
        new NavItem("בית",            "dashboard/lecturer",  "oi-home",            NavLinkMatch.All,    NavIcons.Home),
        // המשימות שלי — the lecturer's OWN reminders (/lecturer/tasks), in the
        // second slot the mentor's list gives the same item. It is personal
        // tasks only: the mentor's other two feeds are ProjectMentors-scoped
        // and return nothing for this role, and הגשות / בקשות already have
        // their own entries two and three rows down.
        new NavItem("המשימות שלי",    "lecturer/tasks",      "oi-task",            NavLinkMatch.Prefix, NavIcons.Tasks),
        new NavItem("הצוותים שלי",    "projects",            "oi-folder",          NavLinkMatch.Prefix, NavIcons.Projects),
        new NavItem("הגשות ובקרה",    "lecturer/submissions", "oi-target",          NavLinkMatch.Prefix, NavIcons.Assignments),
        // Badge: OpenRequests count (shown by LecturerTopNav, same as student's בקשות badge).
        new NavItem("בקשות",          "management/requests", "oi-envelope-closed", NavLinkMatch.Prefix, NavIcons.Requests),
        new NavItem("יומן",           "lecturer/calendar",   "oi-calendar",        NavLinkMatch.Prefix, NavIcons.Calendar),
        new NavItem("משאבים",         "resource-files",      "oi-book",            NavLinkMatch.Prefix, NavIcons.Knowledge),
        // Match.All: /management/* is owned by its own inner nav; this item
        // should only light up when the user is at /management exactly.
        new NavItem("ניהול",          "management",          "oi-list-rich",       NavLinkMatch.All,    NavIcons.Dashboard),
    };

    // Empty: settings is reached from the profile avatar in LecturerTopNav's
    // action cluster, exactly like the student and mentor shells. A second
    // nav row here would be a duplicate destination.
    private static readonly IReadOnlyList<NavItem> _adminBottom = Array.Empty<NavItem>();

    // ── Student ──────────────────────────────────────────────────────
    // The six destinations of the FINAL student design — every one of the
    // eight `… final.dc.html` screens draws the same pill bar:
    //
    //     בית · משימות · בקשות② · יומן · משאבים · הפרויקט שלי
    //
    // Three things changed from the previous list, all of them from the
    // design rather than from preference:
    //
    //   • "הפרויקט שלי" (/project) JOINS the primary nav. It was reachable
    //     only from the sidebar's project row, which the final design does
    //     not have — there is no sidebar at all now.
    //   • "מרכז ידע" is renamed "משאבים". Same route (/learning), same page,
    //     the design's own label.
    //   • "דשבורד" -> "בית", "המשימות שלי" -> "משימות", "יומן ותכנון" ->
    //     "יומן". The bar is centred and space-constrained; the final
    //     screens use the short forms.
    //
    // NOTHING WAS REMOVED. /notifications and /external-requests are still
    // withdrawn from the nav exactly as before (the bell popover owns
    // notifications; the innovation-team page keeps its route and webhook).
    //
    // Each entry still carries BOTH icons. The pill bar is text-only, so the
    // SVG path data is unused there today — it is kept because the icons are
    // the same six destinations and dropping them would make re-adding an
    // icon later a data change rather than a rendering one.
    private static readonly IReadOnlyList<NavItem> _studentMain = new[]
    {
        new NavItem("בית",           "dashboard",  "oi-home",            NavLinkMatch.All,    NavIcons.Home),
        new NavItem("משימות",        "tasks",      "oi-task",            NavLinkMatch.Prefix, NavIcons.Tasks),
        new NavItem("בקשות",         "requests",   "oi-envelope-closed", NavLinkMatch.Prefix, NavIcons.Requests),
        // Match.All, not Prefix: the admin/mentor "milestones-overview"
        // route would otherwise light this item up under a prefix match.
        new NavItem("יומן",          "milestones", "oi-calendar",        NavLinkMatch.All,    NavIcons.Calendar),
        new NavItem("משאבים",        "learning",   "oi-book",            NavLinkMatch.Prefix, NavIcons.Knowledge),
        new NavItem("הפרויקט שלי",   "project",    "oi-folder",          NavLinkMatch.Prefix, NavIcons.Projects),
    };

    // Empty: students reach /settings via the profile card at the bottom
    // of the sidebar (AppSideNav's .snav-profile-footer), so a duplicate
    // nav row here would be redundant.
    private static readonly IReadOnlyList<NavItem> _studentBottom = Array.Empty<NavItem>();

    // ── Mentor ───────────────────────────────────────────────────────
    // Restructured in Epic 1 to the six destinations of the mentor design
    // reference (design-reference/mentor-experience/project/Motiva Mentor
    // Home.dc.html:36-46 and every sibling screen, which all draw the same
    // rail): בית · המשימות שלי · פרויקטים בהנחייתי · בקשות · יומן ותכנון ·
    // משאבים.
    //
    // "הפרויקטים שלי" was renamed to "פרויקטים בהנחייתי" — the list is scoped
    // by ProjectMentors, i.e. the projects this mentor SUPERVISES, and a
    // possessive read as ownership. Student and lecturer terminology is
    // untouched: this list is the mentor rail only.
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
    // פרויקטים בהנחייתי and מרחב הפרויקט, milestones/roadmap inside מרחב
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
        new NavItem("פרויקטים בהנחייתי", "mentor/projects", "oi-folder",         NavLinkMatch.Prefix, NavIcons.Projects),
        new NavItem("בקשות",          "mentor-requests",  "oi-envelope-closed", NavLinkMatch.Prefix, NavIcons.Requests),
        new NavItem("יומן ותכנון",    "mentor/calendar",  "oi-calendar",        NavLinkMatch.Prefix, NavIcons.Calendar),
        // The mentor's own read-only view of the shared library.
        //
        // This pointed at /resource-files until Phase B, which was a real bug,
        // not just a styling gap: that page is [Authorize(Admin, Staff)], so a
        // mentor following their own nav item was refused. /mentor/resources
        // reads the same corpus through a mentor-authorised endpoint.
        //
        // Labelled "משאבים", matching the lecturer's and the student's entry:
        // /mentor/resources now renders the SAME screen the lecturer sees
        // (ResourcesLibrary), so "למנחים" would name a mentor-specific product
        // that no longer exists. The destination and the route are unchanged.
        new NavItem("משאבים",         "mentor/resources", "oi-book",            NavLinkMatch.Prefix, NavIcons.Knowledge),
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

        /// <summary>The Motiva lecturer experience — same visual system as
        /// <see cref="Student"/> and <see cref="Mentor"/>, different information
        /// architecture. Staff users (role = Roles.Staff) resolve here.</summary>
        Lecturer,

        /// <summary>Admin-only — the legacy shell, untouched.
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
        // Staff mode → Lecturer shell (same Motiva visual system as Mentor).
        if (UserModeService.IsDualRole(user))
            return UserModeService.EffectiveMode(user) == UserModes.Mentor
                ? Shell.Mentor
                : Shell.Lecturer;

        if (RoleService.IsMentor(user)) return Shell.Mentor;
        // Both Staff (Lecturer) and Admin land on /dashboard/lecturer as their home
        // and share the Lecturer information architecture. Admin no longer gets a
        // separate legacy sidebar shell — the Motiva top nav serves them equally.
        if (RoleService.IsStaff(user) || RoleService.IsAdmin(user)) return Shell.Lecturer;

        return Shell.Student; // safe fallback — matches GetNavItems
    }

    /// <summary>True for the three experiences that consume the Motiva System
    /// Master (Student, Mentor, and Lecturer). This is the single predicate
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
            Shell.Student  => (_studentMain, _studentBottom),
            Shell.Mentor   => (_mentorMain,  _mentorBottom),
            Shell.Lecturer => (_adminMain,   _adminBottom),
            _              => (_adminMain,   _adminBottom),
        };
}
