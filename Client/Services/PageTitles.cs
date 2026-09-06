namespace AuthWithAdmin.Client.Services;

/// <summary>
/// The single source of truth for the browser tab title.
///
/// The application name is <c>Motiva</c>; a routed page reads
/// <c>Motiva | Page Name</c>. Nothing else in the client composes a title —
/// <see cref="AuthWithAdmin.Client.Shared.AppPageTitle"/> renders one
/// &lt;PageTitle&gt; for the whole app from this map, so adding a page's title
/// is a one-line entry here rather than a &lt;PageTitle&gt; per .razor file.
///
/// Titles are BROWSER METADATA and deliberately English, matching the
/// approved examples (Motiva | Dashboard, Motiva | Requests, ...). The UI
/// itself stays Hebrew/RTL; nothing here is rendered inside the page.
/// </summary>
public static class PageTitles
{
    /// <summary>The product name. Also the title when no page matches.</summary>
    public const string AppName = "Motiva";

    private const string Separator = " | ";

    /// <summary>
    /// Route (base-relative, no leading slash) -> page name.
    ///
    /// Route parameters are matched by the literal segment <c>{id}</c>: the
    /// resolver rewrites every all-numeric segment to it before looking up, so
    /// "mentor/projects/42" finds "mentor/projects/{id}". A path with no exact
    /// entry falls back to its longest matching ancestor, so an unlisted
    /// sub-route inherits its section's name instead of losing the title.
    /// </summary>
    private static readonly Dictionary<string, string> _byRoute = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Entry / account ──────────────────────────────────────────────
        ["login"]                = "Login",
        ["signup"]               = "Sign Up",
        ["forget-password"]      = "Forgot Password",
        ["reset-password"]       = "Reset Password",
        ["verify-email"]         = "Verify Email",
        ["pending"]              = "Pending Approval",
        ["redirect"]             = "Redirecting",
        ["create-team"]          = "Create Team",
        ["settings"]             = "Settings",
        ["notifications"]        = "Notifications",

        // ── Student ──────────────────────────────────────────────────────
        ["dashboard"]            = "Dashboard",
        ["tasks"]                = "Tasks",
        ["requests"]             = "Requests",
        ["submissions"]          = "Submissions",
        ["project"]              = "My Project",
        ["learning"]             = "Resources",
        ["milestones"]           = "Planning",
        ["milestones-overview"]  = "Milestones",
        ["files"]                = "Files",
        ["external-requests"]    = "External Requests",
        ["forms/{id}"]           = "Form",
        ["student/assignment"]   = "Assignment",
        ["student/catalog"]      = "Project Catalog",

        // ── Lecturer / staff ─────────────────────────────────────────────
        ["dashboard/lecturer"]   = "Dashboard",
        ["lecturer/tasks"]       = "My Tasks",
        ["lecturer/calendar"]    = "Calendar",
        ["lecturer/submissions"] = "Submissions",
        ["lecturer-submissions"] = "Submissions",
        ["projects"]             = "Teams",
        ["projects/{id}/overview"] = "Project",
        ["project-health"]       = "Project Health",
        ["assignments"]          = "Assignments",
        ["resource-files"]       = "Resources",

        // ── Mentor ───────────────────────────────────────────────────────
        ["dashboard/mentor"]     = "Dashboard",
        ["mentor/projects"]      = "Projects",
        ["mentor/projects/{id}"] = "Project",
        ["mentor/calendar"]      = "Calendar",
        ["mentor/submissions"]   = "Submissions",
        ["mentor/tasks"]         = "Tasks",
        ["mentor/resources"]     = "Resources",
        ["mentor/roadmap"]       = "Roadmap",
        ["mentor-requests"]      = "Requests",
        ["mentor-profile/{id}"]  = "Mentor Profile",

        // ── Management / admin ───────────────────────────────────────────
        ["admin"]                                = "Administration",
        ["myprojects"]                           = "Users",
        ["management"]                           = "Management",
        ["management/admin"]                     = "Management",
        ["management/catalog"]                   = "Catalog",
        ["management/cycles"]                    = "Cycles",
        ["management/cycles/{id}"]               = "Cycle",
        ["management/cycles/{id}/stages"]        = "Roadmap Stages",
        ["management/external-forms"]            = "External Forms",
        ["management/forms"]                     = "Forms",
        ["management/forms/{id}"]                = "Form Editor",
        ["management/forms/{id}/submissions"]    = "Form Submissions",
        ["management/innovation-integration"]    = "Innovation Integration",
        ["management/integrations"]              = "Integrations",
        ["management/integrations/airtable"]     = "Airtable",
        ["management/milestones"]                = "Milestones",
        ["management/pending-approval-settings"] = "Approval Settings",
        ["management/pending-mentor-approvals"]  = "Mentor Approvals",
        ["management/projects"]                  = "Projects",
        ["management/request-types"]             = "Request Types",
        ["management/requests"]                  = "Requests",
        ["management/resources"]                 = "Resources",
        ["management/role-settings"]             = "Role Settings",
        ["management/tasks"]                     = "Tasks",
        ["management/users"]                     = "Users",

        // ── Design-system gallery (developer surface) ────────────────────
        ["motiva"]                               = "Gallery · Getting Started",
        ["motiva/foundations"]                   = "Gallery · Foundations",
        ["motiva/changelog"]                     = "Gallery · Changelog",
        ["motiva/playground"]                    = "Gallery · Playground",
        ["motiva/components/button"]             = "Gallery · Button",
        ["motiva/components/card"]               = "Gallery · Card",
        ["motiva/components/modal"]              = "Gallery · Modal",
        ["motiva/components/progress-bar"]       = "Gallery · Progress Bar",
        ["motiva/components/status-badge"]       = "Gallery · Status Badge",
        ["motiva/components/student-primitives"] = "Gallery · Student Primitives",
    };

    /// <summary>
    /// "Motiva | Dashboard". Use for the handful of components that are not
    /// routed (the not-found and not-authorized views) so the prefix is never
    /// spelled out a second time.
    /// </summary>
    public static string For(string? pageName) =>
        string.IsNullOrWhiteSpace(pageName) ? AppName : AppName + Separator + pageName;

    /// <summary>
    /// Resolves a base-relative path — what
    /// <c>NavigationManager.ToBaseRelativePath(Uri)</c> returns — to the full
    /// browser title. Unknown paths fall back to the section title and, failing
    /// that, to <see cref="AppName"/>: a route with no entry is never left with
    /// a stale or empty tab.
    /// </summary>
    public static string Resolve(string? relativePath)
    {
        var path = Normalize(relativePath);
        if (path.Length == 0)
            return AppName;

        // Exact match, then each ancestor: "management/cycles/{id}/stages" ->
        // "management/cycles/{id}" -> "management/cycles" -> "management".
        while (true)
        {
            if (_byRoute.TryGetValue(path, out var name))
                return For(name);

            var cut = path.LastIndexOf('/');
            if (cut <= 0)
                return AppName;

            path = path[..cut];
        }
    }

    /// <summary>
    /// Strips the query and fragment, trims surrounding slashes and rewrites
    /// numeric route-parameter segments to the literal "{id}" used as the map key.
    /// </summary>
    private static string Normalize(string? relativePath)
    {
        var path = relativePath ?? string.Empty;

        var cut = path.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0)
            path = path[..cut];

        path = path.Trim('/');
        if (path.Length == 0)
            return string.Empty;

        var segments = path.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i].Length > 0 && segments[i].All(char.IsAsciiDigit))
                segments[i] = "{id}";
        }

        return string.Join('/', segments);
    }
}
