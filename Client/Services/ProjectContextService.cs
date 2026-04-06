using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IProjectContextService
{
    /// <summary>
    /// Returns the current user's project context (number + title).
    /// The result is cached after the first call — no repeated HTTP requests.
    /// Returns null when the user has no active project.
    /// </summary>
    Task<ProjectContextDto?> GetAsync();
}

public class ProjectContextService : IProjectContextService
{
    private readonly HttpClient _http;
    private ProjectContextDto? _cached;
    private bool _loaded;

    public ProjectContextService(HttpClient http) => _http = http;

    public async Task<ProjectContextDto?> GetAsync()
    {
        if (_loaded) return _cached;
        try
        {
            _cached = await _http.GetFromJsonAsync<ProjectContextDto>("api/projects/my-context");
        }
        catch
        {
            _cached = null;
        }
        _loaded = true;
        return _cached;
    }
}
