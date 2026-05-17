using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface INotificationPreferencesService
{
    Task<NotificationPreferencesDto?> GetMineAsync();
    Task<bool>                        SaveMineAsync(SaveNotificationPreferencesRequest req);
}

public class NotificationPreferencesService : INotificationPreferencesService
{
    private readonly HttpClient _http;
    public NotificationPreferencesService(HttpClient http) => _http = http;

    public async Task<NotificationPreferencesDto?> GetMineAsync()
    {
        try { return await _http.GetFromJsonAsync<NotificationPreferencesDto>("api/users/me/notification-preferences"); }
        catch { return null; }
    }

    public async Task<bool> SaveMineAsync(SaveNotificationPreferencesRequest req)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync("api/users/me/notification-preferences", req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
