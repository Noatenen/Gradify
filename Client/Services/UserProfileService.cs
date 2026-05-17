using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

// ─────────────────────────────────────────────────────────────────────────────
//  UserProfileService — talks to /api/users/me on behalf of any role.
//
//  Used by the shared /settings page so Mentors / Lecturers / Admins can load
//  and edit their own personal details. Students keep using
//  IStudentProfileService for the richer, project-aware payload.
// ─────────────────────────────────────────────────────────────────────────────

public interface IUserProfileService
{
    Task<UserProfileDto?> GetMyAsync();
    Task<bool>            UpdateMyProfileAsync(UpdateUserProfileRequest req);
}

public class UserProfileService : IUserProfileService
{
    private readonly HttpClient _http;
    public UserProfileService(HttpClient http) => _http = http;

    public async Task<UserProfileDto?> GetMyAsync()
    {
        try { return await _http.GetFromJsonAsync<UserProfileDto>("api/users/me"); }
        catch { return null; }
    }

    public async Task<bool> UpdateMyProfileAsync(UpdateUserProfileRequest req)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync("api/users/me/profile", req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
