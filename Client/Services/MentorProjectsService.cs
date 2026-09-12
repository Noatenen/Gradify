using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IMentorProjectsService
{
    Task<List<MentorProjectSummaryDto>>   GetProjectsAsync();
    Task<MentorProjectDetailDto?>         GetProjectDetailAsync(int projectId);
    Task<List<MentorPendingSubmissionDto>> GetPendingSubmissionsAsync();

    /// <summary>Submissions on this mentor's projects that they have already
    /// approved — <c>MentorStatus = 'Approved'</c>, newest decision first. Same
    /// endpoint, same DTO and same mentor scoping as
    /// <see cref="GetPendingSubmissionsAsync"/>; only the status filter
    /// differs.</summary>
    Task<List<MentorPendingSubmissionDto>> GetApprovedSubmissionsAsync();
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

    public Task<List<MentorPendingSubmissionDto>> GetPendingSubmissionsAsync() =>
        FetchSubmissionsAsync(null);

    public Task<List<MentorPendingSubmissionDto>> GetApprovedSubmissionsAsync() =>
        FetchSubmissionsAsync("Approved");

    /// <summary>The one call behind both submission lists. GET
    /// /api/mentor/submissions is already scoped to the caller's own projects
    /// server-side; <paramref name="mentorStatus"/> omitted is the endpoint's
    /// default — the pending queue — so the existing caller is byte-identical.
    /// Swallows transport errors into an empty list, like every other method
    /// here, so one failing section degrades rather than blanking a page.</summary>
    private async Task<List<MentorPendingSubmissionDto>> FetchSubmissionsAsync(string? mentorStatus)
    {
        var url = mentorStatus is null
            ? "api/mentor/submissions"
            : $"api/mentor/submissions?mentorStatus={Uri.EscapeDataString(mentorStatus)}";

        try
        {
            return await _http.GetFromJsonAsync<List<MentorPendingSubmissionDto>>(url) ?? new();
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
