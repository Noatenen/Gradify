using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace AuthWithAdmin.Client.ClientHelpers;

/// <summary>
/// The login form's own memory: which address was last signed in with, and
/// whether "זכור אותי" was on.
///
/// <para><b>This is a form preference, not a credential and not authentication
/// state.</b> It holds an email and a boolean — nothing else. The password is
/// never seen, never passed here and never written anywhere by this app; the
/// browser's password manager stays solely responsible for it, which is why the
/// form keeps its standard <c>username</c> / <c>current-password</c> autocomplete
/// attributes.</para>
///
/// <para>Kept deliberately separate from the auth token, because the two have
/// different lifetimes: logging out must destroy the session yet leave the
/// preference alone, exactly as every other site that remembers who you are but
/// still makes you sign in.</para>
/// </summary>
public interface IRememberedLoginService
{
    /// <summary>The remembered address, or null when nothing is remembered.</summary>
    Task<string?> GetRememberedEmailAsync();

    /// <summary>
    /// Records the preference after a SUCCESSFUL sign-in. <paramref name="rememberMe"/>
    /// false clears whatever was remembered before, which is how unticking the
    /// box takes effect.
    /// </summary>
    Task SaveAsync(string email, bool rememberMe);
}

public class RememberedLoginService : IRememberedLoginService
{
    private readonly ILocalStorageService        _localStorage;
    private readonly AuthenticationStateProvider _authStateProvider;

    public RememberedLoginService(
        ILocalStorageService localStorage, AuthenticationStateProvider authStateProvider)
    {
        _localStorage      = localStorage;
        _authStateProvider = authStateProvider;
    }

    /// <summary>
    /// Same per-deployment scoping as the auth token — "rememberedLogin_" plus
    /// the router base path. Taken from AuthStateProvider rather than recomputed,
    /// so the two keys can never drift apart when several Motiva builds share one
    /// origin under different sub-paths.
    /// </summary>
    private string Key =>
        $"rememberedLogin_{((AuthStateProvider)_authStateProvider).GetProjectScope()}";

    public async Task<string?> GetRememberedEmailAsync()
    {
        try
        {
            var raw = await _localStorage.GetItemAsync(Key);
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var stored = JsonSerializer.Deserialize<RememberedLogin>(raw);

            return stored is { RememberMe: true } && !string.IsNullOrWhiteSpace(stored.Email)
                ? stored.Email
                : null;
        }
        catch
        {
            // Malformed or hand-edited value: behave as if nothing was remembered
            // rather than breaking the login page over a convenience feature.
            return null;
        }
    }

    public async Task SaveAsync(string email, bool rememberMe)
    {
        if (!rememberMe || string.IsNullOrWhiteSpace(email))
        {
            await _localStorage.RemoveItemAsync(Key);
            return;
        }

        var stored = new RememberedLogin { Email = email.Trim().ToLower(), RememberMe = true };
        await _localStorage.SetItemAsync(Key, JsonSerializer.Serialize(stored));
    }

    /// <summary>Everything that is ever written. Note what is absent.</summary>
    private class RememberedLogin
    {
        public string Email      { get; set; } = "";
        public bool   RememberMe { get; set; }
    }
}
