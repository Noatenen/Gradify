using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IProjectMentorsService
{
    /// <summary>Current mentors assigned to the project.</summary>
    Task<List<ProjectMentorDto>?>   GetMentorsAsync(int projectId);
    /// <summary>Eligible users not yet assigned — optionally filtered by search query.</summary>
    Task<List<MentorCandidateDto>?> GetCandidatesAsync(int projectId, string? q = null);
    Task<bool>                      AddMentorAsync(int projectId, int userId);
    Task<bool>                      RemoveMentorAsync(int projectId, int userId);
}

public class ProjectMentorsService : IProjectMentorsService
{
    private readonly HttpClient _http;
    public ProjectMentorsService(HttpClient http) => _http = http;

    public async Task<List<ProjectMentorDto>?> GetMentorsAsync(int projectId)
    {
        try   { return await _http.GetFromJsonAsync<List<ProjectMentorDto>>($"api/projects/{projectId}/mentors"); }
        catch { return null; }
    }

    public async Task<List<MentorCandidateDto>?> GetCandidatesAsync(int projectId, string? q = null)
    {
        var url = $"api/projects/{projectId}/mentors/candidates";
        if (!string.IsNullOrWhiteSpace(q)) url += $"?q={Uri.EscapeDataString(q)}";
        try   { return await _http.GetFromJsonAsync<List<MentorCandidateDto>>(url); }
        catch { return null; }
    }

    public async Task<bool> AddMentorAsync(int projectId, int userId)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"api/projects/{projectId}/mentors",
                new AddProjectMentorRequest { UserId = userId });
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> RemoveMentorAsync(int projectId, int userId)
    {
        try
        {
            var resp = await _http.DeleteAsync($"api/projects/{projectId}/mentors/{userId}");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
