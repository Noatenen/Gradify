using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

/// <summary>
/// Single source of truth for all role checks across the client.
/// Always use this instead of raw Roles string comparisons in components.
/// </summary>
public static class RoleService
{
    public static bool HasRole(User? user, string role) =>
        user?.Roles?.Contains(role, StringComparer.OrdinalIgnoreCase) ?? false;

    public static bool IsStudent(User? user)      => HasRole(user, Roles.Student);
    public static bool IsMentor(User? user)       => HasRole(user, Roles.Mentor);
    public static bool IsAdmin(User? user)        => HasRole(user, Roles.Admin);
    public static bool IsStaff(User? user)        => HasRole(user, Roles.Staff);

    /// <summary>Admin or Staff (Lecturer) — has full management access.</summary>
    public static bool IsAdminOrStaff(User? user) => IsAdmin(user) || IsStaff(user);

    /// <summary>Hebrew display label for the user's primary role.</summary>
    public static string GetRoleLabel(User? user)
    {
        if (IsAdmin(user))   return "מנהל";
        if (IsStaff(user))   return "מרצה";
        if (IsMentor(user))  return "מנטור";
        if (IsStudent(user)) return "סטודנט";
        return "";
    }

    /// <summary>
    /// Default landing route after login / when the app starts at "/".
    /// Single source of truth — every redirect site (Index, LoginPage,
    /// SignupPage, APIRedirect) calls this so role-based routing stays
    /// consistent. Server-side [Authorize] gates remain the actual access
    /// boundary; this helper only chooses a sensible starting page.
    /// </summary>
    public static string GetDefaultLandingRoute(User? user)
    {
        if (IsAdminOrStaff(user)) return "/dashboard/lecturer";
        if (IsMentor(user))       return "/dashboard/mentor";
        // Students keep the legacy /dashboard route — that page already
        // handles the "team-without-project" → catalog redirect internally.
        return PageRoutes.Dashboard;
    }
}
