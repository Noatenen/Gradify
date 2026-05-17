using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

// ─────────────────────────────────────────────────────────────────────────────
//  ProjectHealthService — drives the internal "Project Health" page.
//  Server scopes results by role (Admin/Staff → all, Mentor → own projects);
//  the client just renders whatever it receives.
// ─────────────────────────────────────────────────────────────────────────────

public interface IProjectHealthService
{
    Task<List<ProjectHealthRowDto>?> GetAllAsync();
}

public class ProjectHealthService : IProjectHealthService
{
    private readonly HttpClient _http;
    public ProjectHealthService(HttpClient http) => _http = http;

    public async Task<List<ProjectHealthRowDto>?> GetAllAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ProjectHealthRowDto>>("api/project-health");
        }
        catch { return null; }
    }
}
