using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IMilestonesOverviewService
{
    /// <summary>scope = "lecturer" | "mentor" — server enforces what the user is allowed to see.</summary>
    Task<MilestonesOverviewDto?> GetAsync(string scope);
}

public class MilestonesOverviewService : IMilestonesOverviewService
{
    private readonly HttpClient _http;
    public MilestonesOverviewService(HttpClient http) => _http = http;

    public async Task<MilestonesOverviewDto?> GetAsync(string scope)
    {
        try
        {
            return await _http.GetFromJsonAsync<MilestonesOverviewDto>(
                $"api/milestones/overview?scope={Uri.EscapeDataString(scope)}");
        }
        catch { return null; }
    }
}
