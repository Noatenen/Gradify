using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IProjectOverviewService
{
    Task<ProjectOverviewDto?> GetAsync(int projectId);
}

public class ProjectOverviewService : IProjectOverviewService
{
    private readonly HttpClient _http;
    public ProjectOverviewService(HttpClient http) => _http = http;

    public async Task<ProjectOverviewDto?> GetAsync(int projectId)
    {
        try { return await _http.GetFromJsonAsync<ProjectOverviewDto>($"api/projects/{projectId}/overview"); }
        catch { return null; }
    }
}
