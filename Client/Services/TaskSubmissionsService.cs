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
    Task<bool>                            SubmitToCourseAsync(int submissionId);

    // ── Lecturer / admin-facing ──────────────────────────────────────────────
    Task<List<LecturerSubmissionRowDto>?>     GetAllForLecturerAsync();
    Task<TaskSubmissionDto?>                  GetSubmissionDetailAsync(int submissionId);
    Task<LecturerSubmissionDetailDto?>        GetLecturerDetailAsync(int submissionId);
    Task<bool>                                UpdateSubmissionStatusAsync(int submissionId, string status);
    Task<bool>                                SaveLecturerReviewAsync(int submissionId, string reviewStatus, string? feedback);
    Task<(bool Ok, string? Error)>            PublishFeedbackAsync(int submissionId);
    Task<(bool Ok, string? Error, int Saved)> UploadLecturerFilesAsync(int submissionId, List<SubmissionFileRequest> files);
    Task<(bool Ok, string? Error)>            DeleteLecturerFileAsync(int submissionId, int fileId);
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

    public async Task<bool> SubmitToCourseAsync(int submissionId)
    {
        try
        {
            var resp = await _http.PostAsync(
                $"api/task-submissions/{submissionId}/submit-to-course", null);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
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

    public async Task<LecturerSubmissionDetailDto?> GetLecturerDetailAsync(int submissionId)
    {
        try
        {
            return await _http.GetFromJsonAsync<LecturerSubmissionDetailDto>(
                $"api/task-submissions/{submissionId}/lecturer-detail");
        }
        catch { return null; }
    }

    public async Task<bool> SaveLecturerReviewAsync(int submissionId, string reviewStatus, string? feedback)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"api/task-submissions/{submissionId}/lecturer-review",
                new SaveLecturerReviewRequest
                {
                    ReviewStatus     = reviewStatus,
                    ReviewerFeedback = feedback,
                });
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<(bool Ok, string? Error)> PublishFeedbackAsync(int submissionId)
    {
        try
        {
            var resp = await _http.PostAsync(
                $"api/task-submissions/{submissionId}/publish-feedback", null);
            if (resp.IsSuccessStatusCode) return (true, null);
            string body = await resp.Content.ReadAsStringAsync();
            return (false, string.IsNullOrWhiteSpace(body) ? "שגיאה בפרסום המשוב" : body.Trim('"'));
        }
        catch { return (false, "שגיאה בפרסום המשוב"); }
    }

    public async Task<(bool Ok, string? Error, int Saved)> UploadLecturerFilesAsync(
        int submissionId, List<SubmissionFileRequest> files)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"api/task-submissions/{submissionId}/lecturer-files",
                new UploadLecturerFilesRequest { Files = files });
            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<UploadLecturerFilesResult>();
                return (true, null, result?.Saved ?? 0);
            }
            string body = await resp.Content.ReadAsStringAsync();
            return (false, string.IsNullOrWhiteSpace(body) ? "שגיאה בהעלאת הקבצים" : body.Trim('"'), 0);
        }
        catch { return (false, "שגיאה בהעלאת הקבצים", 0); }
    }

    public async Task<(bool Ok, string? Error)> DeleteLecturerFileAsync(int submissionId, int fileId)
    {
        try
        {
            var resp = await _http.DeleteAsync(
                $"api/task-submissions/{submissionId}/lecturer-files/{fileId}");
            if (resp.IsSuccessStatusCode) return (true, null);
            string body = await resp.Content.ReadAsStringAsync();
            return (false, string.IsNullOrWhiteSpace(body) ? "שגיאה במחיקת הקובץ" : body.Trim('"'));
        }
        catch { return (false, "שגיאה במחיקת הקובץ"); }
    }

    private sealed class CreateResult              { public int Id    { get; set; } }
    private sealed class UploadLecturerFilesResult { public int Saved { get; set; } }
}