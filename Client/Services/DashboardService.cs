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
            return await _http.GetFromJsonAsync<DashboardDto>("api/projects/my-dashboard");
        }
        catch
        {
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
