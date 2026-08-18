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

    /// <summary>
    /// Drops the cache, re-reads the context and notifies every subscriber via
    /// <see cref="Changed"/>.
    ///
    /// <para>Called when something the context carries has just been written —
    /// today that is the project's display name, edited on /project. Without
    /// this the sidebar would keep rendering the pre-save title for the rest of
    /// the session, because the cache has no expiry.</para>
    /// </summary>
    Task<ProjectContextDto?> RefreshAsync();

    /// <summary>
    /// Raised after <see cref="RefreshAsync"/> completes, carrying the new
    /// context. Subscribers must unsubscribe when they are disposed.
    ///
    /// <para>An instance event rather than a static one (unlike
    /// UserModeService.CurrentModeChanged): this service is registered Scoped,
    /// which in a WebAssembly client is one instance for the whole app, so
    /// every consumer already shares this object.</para>
    /// </summary>
    event Action<ProjectContextDto?>? Changed;
}

public class ProjectContextService : IProjectContextService
{
    private readonly HttpClient _http;
    private ProjectContextDto? _cached;
    private bool _loaded;

    public ProjectContextService(HttpClient http) => _http = http;

    public event Action<ProjectContextDto?>? Changed;

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

    public async Task<ProjectContextDto?> RefreshAsync()
    {
        _loaded = false;
        _cached = null;

        var ctx = await GetAsync();

        Changed?.Invoke(ctx);
        return ctx;
    }
}
