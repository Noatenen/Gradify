using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IDashboardOverviewService
{
    /// <summary>scope = "lecturer" | "mentor" — server enforces what the user is actually allowed to see.</summary>
    Task<DashboardOverviewDto?> GetAsync(string scope);
}

public class DashboardOverviewService : IDashboardOverviewService
{
    private readonly HttpClient _http;
    public DashboardOverviewService(HttpClient http) => _http = http;

    public async Task<DashboardOverviewDto?> GetAsync(string scope)
    {
        try
        {
            return await _http.GetFromJsonAsync<DashboardOverviewDto>(
                $"api/dashboard?scope={Uri.EscapeDataString(scope)}");
        }
        catch { return null; }
    }
}
