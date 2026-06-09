using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IDashboardService
{
    /// <summary>
    /// Fetches the full dashboard payload for the current authenticated user.
    /// Returns null on network/auth error; returns a DashboardDto with a null
    /// Project when the user is not yet assigned to a team.
    /// </summary>
    Task<DashboardDto?> GetDashboardAsync();

    /// <summary>
    /// Fetches the student-safe full project details for the current user's project.
    /// Returns null on error or when the user has no project.
    /// Used to lazily populate the project-details modal on the student dashboard.
    /// </summary>
    Task<StudentProjectDetailsDto?> GetMyProjectDetailsAsync();
}

public class DashboardService : IDashboardService
{
    private readonly HttpClient _http;

    public DashboardService(HttpClient http) => _http = http;

    public async Task<DashboardDto?> GetDashboardAsync()
    {
        try
        {
            // [DashboardDiag] temporary — log the outgoing auth header state so we
            // can confirm whether the request carries a bearer token at send time.
            var auth = _http.DefaultRequestHeaders.Authorization;
            var authPreview = auth is null
                ? "none"
                : $"{auth.Scheme} {(auth.Parameter is { Length: > 12 } p ? p[..12] + "…" : auth.Parameter)}";
            Console.WriteLine($"[DashboardDiag] GET api/projects/my-dashboard (authHeader={authPreview})");

            // Use GetAsync (not GetFromJsonAsync) so we can read the EXACT status
            // code and body on failure instead of a generic deserialization throw.
            var resp = await _http.GetAsync("api/projects/my-dashboard");
            Console.WriteLine($"[DashboardDiag] my-dashboard status={(int)resp.StatusCode} ({resp.StatusCode}) contentType={resp.Content.Headers.ContentType?.MediaType ?? "?"}");

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                if (body.Length > 300) body = body[..300] + "…";
                Console.WriteLine($"[DashboardDiag] my-dashboard FAILED body: {body}");
                return null;
            }

            return await resp.Content.ReadFromJsonAsync<DashboardDto>();
        }
        catch (Exception ex)
        {
            // [DashboardDiag] temporary — surface the real failure instead of
            // silently returning null (which the page renders as a generic error).
            Console.WriteLine($"[DashboardDiag] my-dashboard EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public async Task<StudentProjectDetailsDto?> GetMyProjectDetailsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<StudentProjectDetailsDto>(
                "api/projects/my-project-details");
        }
        catch
        {
            return null;
        }
    }
}
