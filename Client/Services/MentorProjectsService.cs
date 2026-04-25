using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IMentorProjectsService
{
    Task<List<MentorProjectSummaryDto>>   GetProjectsAsync();
    Task<MentorProjectDetailDto?>         GetProjectDetailAsync(int projectId);
    Task<List<MentorPendingSubmissionDto>> GetPendingSubmissionsAsync();
    Task<MentorSubmissionContextDto?>     GetSubmissionContextAsync(int submissionId);
    Task<bool>                            ReviewSubmissionAsync(int submissionId, string mentorStatus, string? feedback);
}

public class MentorProjectsService : IMentorProjectsService
{
    private readonly HttpClient _http;

    public MentorProjectsService(HttpClient http) => _http = http;

    public async Task<List<MentorProjectSummaryDto>> GetProjectsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<MentorProjectSummaryDto>>("api/mentor/projects")
                   ?? new();
        }
        catch { return new(); }
    }

    public async Task<MentorProjectDetailDto?> GetProjectDetailAsync(int projectId)
    {
        try
        {
            return await _http.GetFromJsonAsync<MentorProjectDetailDto>($"api/mentor/projects/{projectId}");
        }
        catch { return null; }
    }

    public async Task<List<MentorPendingSubmissionDto>> GetPendingSubmissionsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<MentorPendingSubmissionDto>>(
                "api/mentor/submissions") ?? new();
        }
        catch { return new(); }
    }

    public async Task<MentorSubmissionContextDto?> GetSubmissionContextAsync(int submissionId)
    {
        try
        {
            return await _http.GetFromJsonAsync<MentorSubmissionContextDto>(
                $"api/mentor/submissions/{submissionId}/context");
        }
        catch { return null; }
    }

    public async Task<bool> ReviewSubmissionAsync(int submissionId, string mentorStatus, string? feedback)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"api/task-submissions/{submissionId}/mentor-review",
                new MentorReviewRequest { MentorStatus = mentorStatus, MentorFeedback = feedback });
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
