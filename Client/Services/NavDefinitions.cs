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
    // EIGHT destinations. שיבוצים joined the list (see its own note);
    // המשימות שלי has since LEFT it — see the block where that entry stood —
    // leaving the seven that match the approved Motiva Lecturer design
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
        // ── המשימות שלי IS GONE FROM THIS RAIL, AND ONLY FROM IT ────────
        // The lecturer no longer has a destination whose purpose is "my
        // tasks", because Home's דורש פעולה now IS that queue: it carries the
        // two system feeds that page showed (staff-owned requests, submissions
        // pending a mentor approval) and the lecturer's own PersonalTasks
        // alongside them, with creation, completion, editing and deletion all
        // running through the same IPersonalTasksService and the same
        // MentorPersonalTaskModal that page used.
        //
        // THE ROUTE ITSELF IS DELIBERATELY LEFT ALIVE at /lecturer/tasks,
        // unlinked. Deleting the page would take LecturerItemQuickView with it
        // — its only caller — and a bookmark or a stale ?returnUrl= would start
        // 404ing; neither buys anything this pass needs. It is now unreachable
        // by navigation, which is what was actually asked for, and it can be
        // removed in its own commit once nothing has looked for it.
        // ReturnUrlPolicy still resolves it through Shell("lecturer"), so a
        // captured link keeps working rather than dead-ending on PendingPage.
        new NavItem("הצוותים שלי",    "projects",            "oi-folder",          NavLinkMatch.Prefix, NavIcons.Projects),
        new NavItem("הגשות",           "lecturer/submissions", "oi-target",          NavLinkMatch.Prefix, NavIcons.Assignments),
        // Badge: OpenRequests count (shown by LecturerTopNav, same as student's בקשות badge).
        new NavItem("בקשות",          "management/requests", "oi-envelope-closed", NavLinkMatch.Prefix, NavIcons.Requests),
        new NavItem("יומן",           "lecturer/calendar",   "oi-calendar",        NavLinkMatch.Prefix, NavIcons.Calendar),
        new NavItem("משאבים",         "resource-files",      "oi-book",            NavLinkMatch.Prefix, NavIcons.Knowledge),
        // שיבוצים — the EXISTING /assignments workspace, on the primary nav
        // rather than built anew. It was reachable only through the ניהול hub
        // ("פרויקטים וצוותים" → "שיבוץ צוותים לפרויקטים"), which buried a
        // top-level lecturer responsibility two clicks deep behind a settings
        // surface. Route, page, controller and authorization are untouched;
        // this list is the only thing that has ever changed for it.
        //
        // MOVED TO SIT BESIDE ניהול. It used to sit between הצוותים שלי and
        // הגשות, on the reading that the three were a lifecycle — who → which
        // project → what they hand in. In use they are not one: הצוותים שלי and
        // הגשות are things a lecturer checks on continuously, and שיבוץ is
        // something done in bursts at the start of a cycle and then not again.
        // It belongs with ניהול, the other periodic-administration destination,
        // and the everyday cluster is left uninterrupted.
        //
        // Match.Prefix, like its siblings: /assignments has no sub-routes
        // today, and the planned per-team detail view will keep the item lit
        // instead of needing this changed. No other lecturer route starts with
        // "assignments" (the student form is /student/assignment, and students
        // resolve to _studentMain), so the prefix cannot capture anything else.
        //
        // NavIcons.Assignments is this destination's own icon — its summary
        // literally reads "שיבוצים". It is also carried by הגשות above; that
        // duplication is pre-existing and has no visual effect, because the
        // pill bar renders labels only.
        new NavItem("שיבוצים",         "assignments",         "oi-target",          NavLinkMatch.Prefix, NavIcons.Assignments),
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
        // ── המשימות שלי IS GONE FROM THIS RAIL, exactly as it is gone from
        // the lecturer's, and for the same reason: Home's דורש פעולה now IS
        // that queue. It carries the two system feeds that page showed — the
        // submissions awaiting this mentor's review and the requests awaiting
        // their recommendation, both straight off GET /api/mentor/attention —
        // and Home's משימות אישיות card carries the mentor's own reminders,
        // with creation, completion, editing and deletion all running through
        // the same IPersonalTasksService and the same MentorPersonalTaskModal
        // that page used.
        //
        // THE ROUTE ITSELF IS DELIBERATELY LEFT ALIVE at /mentor/tasks,
        // unlinked. MentorLinks.Reviews / .Requests / .PersonalTasks are
        // ?focus= deep links into it, the daily digest and several popups still
        // hand them out, and a bookmark or a stale ?returnUrl= would start
        // 404ing if the page went. It is now unreachable by NAVIGATION, which
        // is what was asked for, and it can be removed in its own commit once
        // nothing looks for it. ReturnUrlPolicy still resolves it through
        // Shell("mentor"), so a captured link keeps working rather than
        // dead-ending on PendingPage.
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

        // ── ROLE DECIDES THE SHELL. THE USER DOES NOT CHOOSE IT ──────────
        // This asked UserModeService which of their two experiences a
        // Staff+Mentor user was currently "in", and a visible מרצה/מנחה
        // control in the top bar set it. That control is gone, so this is
        // now a fact about the account rather than about a toggle:
        //
        //   Staff / Admin  →  Lecturer, whether or not they also mentor
        //   Mentor only    →  Mentor
        //
        // WHY STAFF WINS. It is the strictly larger responsibility — the
        // lecturer dashboard is course-wide and its work queue already
        // carries every request the academic side owns plus every submission
        // pending a mentor approval, so a dual-role user sees a superset of
        // what the mentor shell showed them. Choosing Mentor instead would
        // hide course-wide work from someone who is accountable for it.
        //
        // NOTHING BECAME UNREACHABLE. /dashboard/mentor and every /mentor/*
        // route still authorise the Mentor role and still resolve, and
        // ReturnUrlPolicy still lets a mentor deep link through — what
        // changed is which set of pills the bar draws by default, not who
        // may open what. Authorization is unchanged everywhere: it has always
        // been the role claim on the server and the [Authorize] attribute on
        // the page, and view mode never took part in either.
        //
        // UserModeService itself is deliberately NOT deleted here. It is
        // still referenced by LecturerTasksPage (which is unlinked but alive)
        // and it stays the obvious home for the mode concept if the product
        // brings back an explicit switch. It simply no longer decides this.
        if (RoleService.IsMentor(user) && !RoleService.IsAdminOrStaff(user)) return Shell.Mentor;
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

    /// <summary>
    /// THE ROLE'S OWN REQUESTS QUEUE — one route per shell, stated once.
    ///
    /// <para>Three pages answer "בקשות" and they are not interchangeable:
    /// <c>/requests</c> is the student's own queue and the only screen in the
    /// product that may OPEN a request, <c>/management/requests</c> is the
    /// lecturer's decision queue and <c>/mentor-requests</c> the mentor's
    /// recommendation queue. Every other role-aware route in this shell is
    /// resolved here rather than at its call site, and this one has to be for
    /// a stronger reason than symmetry: <c>/requests</c> carries only a bare
    /// <c>[Authorize]</c> — it cannot carry a role attribute, because refusing
    /// a lecturer outright lands them on PendingPage — so it is the page
    /// itself that must send a non-student to the queue that IS theirs.</para>
    ///
    /// <para>Returns the student route for the fallback shell as well, so the
    /// caller must compare before navigating rather than assume the answer is
    /// somewhere else; <c>StudentRequestsPage</c> does exactly that and
    /// therefore cannot redirect to itself.</para>
    /// </summary>
    public static string RequestsHomeFor(User? user) => GetShell(user) switch
    {
        Shell.Mentor   => "/mentor-requests",
        Shell.Lecturer => "/management/requests",
        _              => StudentRequestsRoute,
    };

    /// <summary>The student queue's own route. Named because
    /// <see cref="RequestsHomeFor"/> returns it as its fallback and the page
    /// that lives at it has to recognise its own address.</summary>
    public const string StudentRequestsRoute = "/requests";

    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Returns the (Main, Bottom) nav items for the given user's role.
    /// Students take priority (they only have one role). For users who hold
    /// *both* Staff and Mentor roles, the active view mode from
    /// <see cref="UserModeService"/> decides which nav set is rendered —
    /// flipping the toggle re-runs this method via AppSideNav's subscription.
    /// Falls back to the student view for unrecognised roles.
    /// </summary>
    /// <summary>
    /// The role's nav, or NOTHING while the role is not known yet.
    ///
    /// <para><b>The null case is the fix for a real routing bug.</b>
    /// <c>App.razor</c> cascades <c>user</c> from an async handler, so on every
    /// full page load there is a window in which the cascade is still null.
    /// <c>GetShell(null)</c> answers <c>Shell.Student</c> — a deliberate safe
    /// fallback for the SHELL, and the right answer there — but feeding that
    /// into this switch handed a mentor the STUDENT nav, whose first item is
    /// "בית" → <c>/dashboard</c>. Click it inside that window and the legacy
    /// student dashboard is what loads, because <c>Dashboard.razor</c>'s own
    /// "bounce an elevated role to their dashboard" guard is reading the same
    /// null user and falls through.</para>
    ///
    /// <para>So "בית" could point at a dashboard that is not the caller's, and
    /// only sometimes — which is exactly how it was reported. An empty list
    /// renders no pills at all for those few frames; the bar fills in when the
    /// cascade arrives (<c>OnParametersSetAsync</c> re-runs), and there is no
    /// moment at which a wrong destination is clickable.</para>
    ///
    /// <para>GetShell is deliberately NOT changed: it still answers Student for
    /// an unknown user, so <c>IsMotivaShell</c> keeps returning true and the
    /// canvas shell paints immediately instead of flashing the legacy sidebar.
    /// The unknown case is only wrong for NAVIGATION, and that is the only
    /// place it is now special-cased.</para></summary>
    public static (IReadOnlyList<NavItem> Main, IReadOnlyList<NavItem> Bottom) GetNavItems(User? user)
        => user is null
            ? (Array.Empty<NavItem>(), Array.Empty<NavItem>())
            : GetShell(user) switch
            {
                Shell.Student  => (_studentMain, _studentBottom),
                Shell.Mentor   => (_mentorMain,  _mentorBottom),
                Shell.Lecturer => (_adminMain,   _adminBottom),
                _              => (_adminMain,   _adminBottom),
            };
}
