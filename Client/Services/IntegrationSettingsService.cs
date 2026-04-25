using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IIntegrationSettingsService
{
    Task<IntegrationSettingsDto?> GetSlackSettingsAsync();
    Task<bool>                    UpdateSlackSettingsAsync(UpdateIntegrationSettingsRequest req);
}

public class IntegrationSettingsService : IIntegrationSettingsService
{
    private readonly HttpClient _http;

    public IntegrationSettingsService(HttpClient http) => _http = http;

    public async Task<IntegrationSettingsDto?> GetSlackSettingsAsync()
    {
        try { return await _http.GetFromJsonAsync<IntegrationSettingsDto>("api/admin/integrations/slack"); }
        catch { return null; }
    }

    public async Task<bool> UpdateSlackSettingsAsync(UpdateIntegrationSettingsRequest req)
    {
        try
        {
            var res = await _http.PutAsJsonAsync("api/admin/integrations/slack", req);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
