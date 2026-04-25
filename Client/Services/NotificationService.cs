using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface INotificationService
{
    Task<List<NotificationDto>> GetRecentAsync();
    Task<int>                   GetUnreadCountAsync();
    Task<bool>                  MarkReadAsync(int id);
    Task<bool>                  MarkAllReadAsync();
}

public class NotificationService : INotificationService
{
    private readonly HttpClient _http;

    public NotificationService(HttpClient http) => _http = http;

    public async Task<List<NotificationDto>> GetRecentAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<NotificationDto>>("api/notifications")
                   ?? new();
        }
        catch { return new(); }
    }

    public async Task<int> GetUnreadCountAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<int>("api/notifications/unread-count");
        }
        catch { return 0; }
    }

    public async Task<bool> MarkReadAsync(int id)
    {
        try
        {
            var resp = await _http.PostAsync($"api/notifications/{id}/mark-read", null);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> MarkAllReadAsync()
    {
        try
        {
            var resp = await _http.PostAsync("api/notifications/mark-all-read", null);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
