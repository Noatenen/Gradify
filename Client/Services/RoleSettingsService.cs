using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

// ─────────────────────────────────────────────────────────────────────────────
//  RoleSettings client surface — two separate services:
//
//   IRoleFeatureService              — every signed-in user. Exposes the
//                                      effective UNION-of-roles flag set for
//                                      "can I do X" checks throughout the app.
//                                      Cached for the session; .Reset() after
//                                      login/logout.
//
//   IRoleSettingsManagementService   — Admin only. Backs the
//                                      "ניהול → הרשאות לפי תפקיד" page.
// ─────────────────────────────────────────────────────────────────────────────

public interface IRoleFeatureService
{
    /// <summary>All flags currently enabled for the calling user (UNION of roles).</summary>
    Task<HashSet<string>> GetMyFeaturesAsync();
    /// <summary>True when the flag is enabled for the caller.</summary>
    Task<bool>            IsAllowedAsync(string flag);
    void                  Reset();
}

public class RoleFeatureService : IRoleFeatureService
{
    private readonly HttpClient _http;
    private HashSet<string>? _cache;
    private Task<HashSet<string>>? _inflight;

    public RoleFeatureService(HttpClient http) => _http = http;

    public async Task<HashSet<string>> GetMyFeaturesAsync()
    {
        if (_cache is not null) return _cache;

        _inflight ??= LoadAsync();
        var result = await _inflight;
        _cache    = result;
        _inflight = null;
        return result;
    }

    public async Task<bool> IsAllowedAsync(string flag)
    {
        if (string.IsNullOrWhiteSpace(flag)) return false;
        var all = await GetMyFeaturesAsync();
        return all.Contains(flag);
    }

    public void Reset()
    {
        _cache    = null;
        _inflight = null;
    }

    private async Task<HashSet<string>> LoadAsync()
    {
        try
        {
            var dto = await _http.GetFromJsonAsync<CurrentUserFeaturesDto>("api/role-settings/me");
            return dto is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(dto.Features, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

public interface IRoleSettingsManagementService
{
    Task<List<RoleSettingsDto>?> GetAllAsync();
    Task<bool>                   SaveAsync(string roleName, SaveRoleSettingsRequest req);
}

public class RoleSettingsManagementService : IRoleSettingsManagementService
{
    private readonly HttpClient _http;
    public RoleSettingsManagementService(HttpClient http) => _http = http;

    public async Task<List<RoleSettingsDto>?> GetAllAsync()
    {
        try { return await _http.GetFromJsonAsync<List<RoleSettingsDto>>("api/role-settings"); }
        catch { return null; }
    }

    public async Task<bool> SaveAsync(string roleName, SaveRoleSettingsRequest req)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync(
                $"api/role-settings/{Uri.EscapeDataString(roleName)}", req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
