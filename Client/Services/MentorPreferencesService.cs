using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

// ─────────────────────────────────────────────────────────────────────────────
//  MentorPreferencesService — talks to /api/mentor-preferences/me on behalf of
//  the mentor card on the shared /settings page.
// ─────────────────────────────────────────────────────────────────────────────

public interface IMentorPreferencesService
{
    Task<MentorPreferencesDto?> GetMineAsync();
    Task<string?>               SaveMineAsync(MentorPreferencesDto req);
    /// <summary>Fetches the read-only public profile for a mentor — what
    /// students / teammates see when they click a mentor name.</summary>
    Task<MentorProfileViewDto?> GetProfileAsync(int mentorUserId);
}

public class MentorPreferencesService : IMentorPreferencesService
{
    private readonly HttpClient _http;
    public MentorPreferencesService(HttpClient http) => _http = http;

    public async Task<MentorPreferencesDto?> GetMineAsync()
    {
        try { return await _http.GetFromJsonAsync<MentorPreferencesDto>("api/mentor-preferences/me"); }
        catch { return null; }
    }

    public async Task<string?> SaveMineAsync(MentorPreferencesDto req)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync("api/mentor-preferences/me", req);
            if (resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(body)
                ? "שגיאה בשמירת ההעדפות"
                : body.Trim('"');
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<MentorProfileViewDto?> GetProfileAsync(int mentorUserId)
    {
        if (mentorUserId <= 0) return null;
        try { return await _http.GetFromJsonAsync<MentorProfileViewDto>($"api/mentor-profile/{mentorUserId}"); }
        catch { return null; }
    }
}
