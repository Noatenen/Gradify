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

    /// <summary>
    /// A user is a Student if they hold the explicit Student role, OR they are a
    /// plain "User" with no elevated role. In Gradify a regular account (role
    /// "User") is a student by default — students are not always tagged with an
    /// explicit "Student" role, but they always at least have "User". They stop
    /// being treated as a student only once explicitly elevated to Mentor, Staff
    /// (lecturer) or Admin.
    /// </summary>
    public static bool IsStudent(User? user) =>
        HasRole(user, Roles.Student)
        || (HasRole(user, Roles.User)
            && !HasRole(user, Roles.Mentor)
            && !HasRole(user, Roles.Staff)
            && !HasRole(user, Roles.Admin));
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
        // AWAITING APPROVAL — signed in, but carrying no role at all.
        //
        // Public self-registration is approval-gated (UserManagement:
        // NeedApproval), so AuthRepository.Signup and RegisterGoogleUser
        // create the account and stop: no "User" approval row, no identity
        // role, nothing for any check below to match. Such an account used to
        // fall through every branch to the /dashboard fallback and render the
        // STUDENT dashboard, whose API calls then 401 against
        // [Authorize(Roles = Student)] — a blank, quietly broken screen
        // instead of an explanation.
        //
        // Zero roles means exactly this and nothing else: every account the
        // system creates through any other path is given at least the "User"
        // approval role in the same operation (team registration adds User +
        // Student, AddUserByAdmin approves, admin edit keeps User). Verified
        // against the deployment database — no existing account has zero roles.
        //
        // Checked FIRST so it cannot be shadowed: an empty role set makes
        // IsStudent() false, but only because every one of its terms is false,
        // which is indistinguishable from "not a student" further down.
        if (user is not null && (user.Roles is null || user.Roles.Count == 0))
            return PageRoutes.Pending;

        // Students keep the legacy /dashboard route — that page already
        // handles the "team-without-project" → catalog redirect internally.
        if (IsStudent(user)) return PageRoutes.Dashboard;

        // ONE CANONICAL HOME PER ROLE, and the order of these two lines is
        // what makes it canonical. This used to ask UserModeService for a
        // dual-role (Staff + Mentor) user's currently-active view mode, so the
        // SAME account could land on either dashboard depending on a toggle
        // stored in localStorage — which is exactly how an overlapping-role
        // user "randomly" fell into the other experience. Staff/Admin is
        // tested first and wins outright, matching NavDefinitions.GetShell so
        // the landing route and the nav rail can never disagree about which
        // Home a user has. See the long note there for why Staff wins.
        if (IsAdminOrStaff(user)) return "dashboard/lecturer";
        if (IsMentor(user))       return "dashboard/mentor";
        return PageRoutes.Dashboard;
    }
}
