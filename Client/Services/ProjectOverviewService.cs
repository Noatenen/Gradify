using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IProjectOverviewService
{
    Task<ProjectOverviewDto?> GetAsync(int projectId);

    /// <summary>Saves the Lecturer/Admin authoring fields on ONE instantiated
    /// project task. Returns true on success. The server is Admin/Staff only and
    /// verifies the task belongs to the project, so a caller without the role —
    /// or with a task id from another project — simply gets false.</summary>
    Task<bool> UpdateTaskSubmissionSettingsAsync(
        int projectId, int taskId, UpdateProjectTaskSubmissionSettingsRequest req);
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

    public async Task<bool> UpdateTaskSubmissionSettingsAsync(
        int projectId, int taskId, UpdateProjectTaskSubmissionSettingsRequest req)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"api/projects/{projectId}/tasks/{taskId}/submission-settings", req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
