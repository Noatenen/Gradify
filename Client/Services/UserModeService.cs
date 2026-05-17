using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

// ─────────────────────────────────────────────────────────────────────────────
//  UserModeService — global "view mode" for users who hold both Staff and
//  Mentor roles. Lets a single account flip between the Lecturer dashboard /
//  navigation and the Mentor dashboard / navigation without re-logging in.
//
//  Design notes
//  ────────────
//  • Static-on-purpose. View mode is a process-singleton, not a request- or
//    component-scoped concept; subscribers attach across many components.
//    Keeps the change footprint tight — no Program.cs registrations.
//  • Persistence is done by the *caller* (the toggle component injects
//    ILocalStorageService and writes the key). The service itself never
//    touches JS interop, so it's trivial to unit test.
//  • Auth boundaries are unchanged. Mode is a UI/UX layer — the server still
//    relies on real role claims for every protected endpoint.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Mode constants. Treated as opaque strings — they double as the
/// persisted value in localStorage.</summary>
public static class UserModes
{
    public const string Lecturer = "lecturer";
    public const string Mentor   = "mentor";

    /// <summary>localStorage key used for persistence.</summary>
    public const string StorageKey = "gradify.userMode";

    public static string Label(string mode) =>
        mode switch
        {
            Lecturer => "מצב מרצה",
            Mentor   => "מצב מנחה",
            _        => mode,
        };
}

public static class UserModeService
{
    /// <summary>The current active mode. Initialised lazily on first access
    /// to <see cref="Lecturer"/> — components should call
    /// <see cref="InitializeFor"/> with the cascading user once it's loaded
    /// to pick the right default (and apply any localStorage choice).</summary>
    public static string CurrentMode { get; private set; } = UserModes.Lecturer;

    /// <summary>Fires whenever <see cref="CurrentMode"/> changes. Subscribers
    /// typically call StateHasChanged() so their UI reflects the new mode.</summary>
    public static event Action? CurrentModeChanged;

    /// <summary>True only for users who hold *both* Staff/Admin and Mentor
    /// roles — the toggle is shown only for them.</summary>
    public static bool IsDualRole(User? user) =>
        RoleService.IsAdminOrStaff(user) && RoleService.IsMentor(user);

    /// <summary>The set of modes the user can switch between, in display order.
    /// Empty for single-role users (no toggle shown).</summary>
    public static IReadOnlyList<string> AvailableModes(User? user)
    {
        if (user is null) return Array.Empty<string>();
        if (!IsDualRole(user)) return Array.Empty<string>();
        return new[] { UserModes.Lecturer, UserModes.Mentor };
    }

    /// <summary>Default mode for a user. Single-role users get their only
    /// option; dual-role users default to Lecturer.</summary>
    public static string DefaultModeFor(User? user)
    {
        if (RoleService.IsAdminOrStaff(user) && !RoleService.IsMentor(user))
            return UserModes.Lecturer;
        if (RoleService.IsMentor(user) && !RoleService.IsAdminOrStaff(user))
            return UserModes.Mentor;
        // Dual-role default.
        return UserModes.Lecturer;
    }

    /// <summary>
    /// Initialises CurrentMode from the (optional) persisted choice. Falls
    /// back to <see cref="DefaultModeFor"/> when no persisted value, or when
    /// the persisted value is no longer valid for this user's roles.
    /// Idempotent — fires CurrentModeChanged only when the resolved mode
    /// differs from the existing one.
    /// </summary>
    public static void InitializeFor(User? user, string? persisted)
    {
        var resolved = ResolveMode(user, persisted);
        if (resolved == CurrentMode) return;
        CurrentMode = resolved;
        CurrentModeChanged?.Invoke();
    }

    /// <summary>Imperatively set the mode. Rejected when the mode isn't a
    /// valid option for the user's roles. Returns the actual effective mode
    /// after the call (caller can then persist that value).</summary>
    public static string SetMode(User? user, string requested)
    {
        var resolved = ResolveMode(user, requested);
        if (resolved == CurrentMode) return resolved;
        CurrentMode = resolved;
        CurrentModeChanged?.Invoke();
        return resolved;
    }

    /// <summary>True when this user is actively in mentor mode and *should*
    /// see the mentor navigation. Centralised so consumers don't have to
    /// special-case dual-role users themselves.</summary>
    public static bool IsMentorViewActive(User? user) =>
        EffectiveMode(user) == UserModes.Mentor;

    /// <summary>Returns the mode that should drive nav/dashboard for this
    /// user *right now*. Lecturer-only and mentor-only users are pinned to
    /// their single mode regardless of <see cref="CurrentMode"/>.</summary>
    public static string EffectiveMode(User? user)
    {
        if (user is null) return CurrentMode;
        if (!IsDualRole(user)) return DefaultModeFor(user);
        return CurrentMode;
    }

    /// <summary>Where the mode-toggle should send the user when they flip.
    /// Mirrors the existing per-role default routes so deep-link bookmarks
    /// keep working.</summary>
    public static string DashboardRouteFor(string mode) =>
        mode switch
        {
            UserModes.Mentor => "/dashboard/mentor",
            _                => "/dashboard/lecturer",
        };

    // ── internals ─────────────────────────────────────────────────────────

    private static string ResolveMode(User? user, string? requested)
    {
        // Single-role users get pinned to their single option.
        if (user is not null && !IsDualRole(user))
            return DefaultModeFor(user);

        if (requested == UserModes.Lecturer || requested == UserModes.Mentor)
            return requested;

        return DefaultModeFor(user);
    }
}