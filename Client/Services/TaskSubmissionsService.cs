using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface ITaskSubmissionsService
{
    // ── Student-facing ───────────────────────────────────────────────────────
    Task<List<StudentSubmissionTaskDto>?> GetMySubmissionTasksAsync();
    Task<StudentSubmissionTaskDto?>       GetSubmissionTaskAsync(int taskId);
    Task<List<TaskSubmissionSummaryDto>?> GetByTaskAsync(int taskId);
    Task<int?>                            CreateSubmissionAsync(CreateSubmissionRequest req);

    // ── Lecturer / admin-facing ──────────────────────────────────────────────
    Task<List<LecturerSubmissionRowDto>?> GetAllForLecturerAsync();
    Task<TaskSubmissionDto?>              GetSubmissionDetailAsync(int submissionId);
    Task<bool>                            UpdateSubmissionStatusAsync(int submissionId, string status);
}

public class TaskSubmissionsService : ITaskSubmissionsService
{
    private readonly HttpClient _http;

    public TaskSubmissionsService(HttpClient http) => _http = http;

    public async Task<List<StudentSubmissionTaskDto>?> GetMySubmissionTasksAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<StudentSubmissionTaskDto>>(
                "api/projects/my-submission-tasks");
        }
        catch { return null; }
    }

    public async Task<StudentSubmissionTaskDto?> GetSubmissionTaskAsync(int taskId)
    {
        try
        {
            return await _http.GetFromJsonAsync<StudentSubmissionTaskDto>(
                $"api/projects/my-submission-tasks/{taskId}");
        }
        catch { return null; }
    }

    public async Task<List<TaskSubmissionSummaryDto>?> GetByTaskAsync(int taskId)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<TaskSubmissionSummaryDto>>(
                $"api/task-submissions?taskId={taskId}");
        }
        catch { return null; }
    }

    public async Task<int?> CreateSubmissionAsync(CreateSubmissionRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/task-submissions", req);
            if (!resp.IsSuccessStatusCode) return null;
            var result = await resp.Content.ReadFromJsonAsync<CreateResult>();
            return result?.Id;
        }
        catch { return null; }
    }

    // ── Lecturer / admin-facing ──────────────────────────────────────────────

    public async Task<List<LecturerSubmissionRowDto>?> GetAllForLecturerAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<LecturerSubmissionRowDto>>(
                "api/task-submissions/all");
        }
        catch { return null; }
    }

    public async Task<TaskSubmissionDto?> GetSubmissionDetailAsync(int submissionId)
    {
        try
        {
            return await _http.GetFromJsonAsync<TaskSubmissionDto>(
                $"api/task-submissions/{submissionId}");
        }
        catch { return null; }
    }

    public async Task<bool> UpdateSubmissionStatusAsync(int submissionId, string status)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"api/task-submissions/{submissionId}/status",
                new UpdateSubmissionStatusRequest { Status = status });
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private sealed class CreateResult { public int Id { get; set; } }
}