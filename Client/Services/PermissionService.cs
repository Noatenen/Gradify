using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IPermissionService
{
    Task<HashSet<string>> GetAllAsync();
    Task<bool>            HasAsync(string key);
    Task<bool>            HasAnyAsync(params string[] keys);
    Task<bool>            HasAllAsync(params string[] keys);
    void                  Reset();
}

/// <summary>
/// Caches the current user's permission keys for the duration of the Blazor
/// session. Call <see cref="Reset"/> after login/logout so the next read
/// reloads from the server.
/// </summary>
public class PermissionService : IPermissionService
{
    private readonly HttpClient _http;
    private HashSet<string>?    _cache;
    private Task<HashSet<string>>? _inflight;

    public PermissionService(HttpClient http) => _http = http;

    public async Task<HashSet<string>> GetAllAsync()
    {
        if (_cache is not null) return _cache;

        // Single-flight: concurrent callers share one HTTP request.
        _inflight ??= LoadAsync();
        var result = await _inflight;
        _cache    = result;
        _inflight = null;
        return result;
    }

    public async Task<bool> HasAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var all = await GetAllAsync();
        return all.Contains(key);
    }

    public async Task<bool> HasAnyAsync(params string[] keys)
    {
        if (keys is null || keys.Length == 0) return false;
        var all = await GetAllAsync();
        foreach (var k in keys)
            if (!string.IsNullOrWhiteSpace(k) && all.Contains(k)) return true;
        return false;
    }

    public async Task<bool> HasAllAsync(params string[] keys)
    {
        if (keys is null || keys.Length == 0) return true;
        var all = await GetAllAsync();
        foreach (var k in keys)
            if (string.IsNullOrWhiteSpace(k) || !all.Contains(k)) return false;
        return true;
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
            var dto = await _http.GetFromJsonAsync<CurrentUserPermissionsDto>("api/permissions/current-user");
            return dto is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(dto.Permissions, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

/// <summary>Admin-only writes against /api/permissions.</summary>
public interface IPermissionsManagementService
{
    Task<List<PermissionDto>?>  GetCatalogAsync();
    Task<List<RoleSummaryDto>?> GetRolesAsync();
    Task<RolePermissionsDto?>   GetRoleAsync(string roleName);
    Task<bool>                  SaveRoleAsync(string roleName, List<string> permissions);
}

public class PermissionsManagementService : IPermissionsManagementService
{
    private readonly HttpClient _http;
    public PermissionsManagementService(HttpClient http) => _http = http;

    public async Task<List<PermissionDto>?> GetCatalogAsync()
    {
        try { return await _http.GetFromJsonAsync<List<PermissionDto>>("api/permissions"); }
        catch { return null; }
    }

    public async Task<List<RoleSummaryDto>?> GetRolesAsync()
    {
        try { return await _http.GetFromJsonAsync<List<RoleSummaryDto>>("api/permissions/roles"); }
        catch { return null; }
    }

    public async Task<RolePermissionsDto?> GetRoleAsync(string roleName)
    {
        try { return await _http.GetFromJsonAsync<RolePermissionsDto>($"api/permissions/roles/{Uri.EscapeDataString(roleName)}"); }
        catch { return null; }
    }

    public async Task<bool> SaveRoleAsync(string roleName, List<string> permissions)
    {
        try
        {
            var res = await _http.PutAsJsonAsync(
                $"api/permissions/roles/{Uri.EscapeDataString(roleName)}",
                new SaveRolePermissionsRequest { Permissions = permissions });
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
