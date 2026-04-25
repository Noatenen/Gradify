using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IStudentProfileService
{
    Task<StudentProfileDto?>      GetProfileAsync();
    Task<bool>                    UpdatePhoneAsync(string phone);
    Task<bool>                    UpdatePreferencesAsync(StudentPreferencesDto prefs);
    Task<bool>                    UploadAvatarAsync(string imageBase64, string extension);
    Task<bool>                    RemoveAvatarAsync();
    Task<IntegrationStatusDto?>   GetSlackSystemStatusAsync();
    Task<string?>                 GetSlackConnectUrlAsync();
    Task<bool>                    DisconnectSlackAsync();
}

public class StudentProfileService : IStudentProfileService
{
    private readonly HttpClient _http;

    public StudentProfileService(HttpClient http) => _http = http;

    public async Task<StudentProfileDto?> GetProfileAsync()
    {
        try { return await _http.GetFromJsonAsync<StudentProfileDto>("api/student/me"); }
        catch { return null; }
    }

    public async Task<bool> UpdatePhoneAsync(string phone)
    {
        try
        {
            var res = await _http.PutAsJsonAsync(
                "api/student/me",
                new UpdateStudentProfileRequest { Phone = phone });
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> UpdatePreferencesAsync(StudentPreferencesDto prefs)
    {
        try
        {
            var res = await _http.PutAsJsonAsync("api/student/me/preferences", prefs);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> UploadAvatarAsync(string imageBase64, string extension)
    {
        try
        {
            var res = await _http.PutAsJsonAsync(
                "api/student/me/avatar",
                new UploadAvatarRequest { ImageBase64 = imageBase64, Extension = extension });
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> RemoveAvatarAsync()
    {
        try
        {
            var res = await _http.DeleteAsync("api/student/me/avatar");
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<IntegrationStatusDto?> GetSlackSystemStatusAsync()
    {
        try { return await _http.GetFromJsonAsync<IntegrationStatusDto>("api/slack/system-status"); }
        catch { return null; }
    }

    public async Task<string?> GetSlackConnectUrlAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<SlackConnectUrlResponse>("api/slack/connect-url");
            return result?.Url;
        }
        catch { return null; }
    }

    public async Task<bool> DisconnectSlackAsync()
    {
        try
        {
            var res = await _http.DeleteAsync("api/slack/disconnect");
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private sealed class SlackConnectUrlResponse
    {
        public string? Url { get; set; }
    }
}
