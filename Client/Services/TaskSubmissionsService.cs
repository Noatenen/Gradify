using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface ITaskSubmissionsService
{
    // ── Student-facing ───────────────────────────────────────────────────────
    Task<List<StudentSubmissionTaskDto>?> GetMySubmissionTasksAsync();
    Task<StudentSubmissionTaskDto?>       GetSubmissionTaskAsync(int taskId);
    Task<List<TaskSubmissionSummaryDto>?> GetByTaskAsync(int taskId);
    /// <summary>Creates a submission. Returns the new id on success or
    /// (null, server-supplied Hebrew error) on failure. The server now runs
    /// authoritative Drive-URL validation, so its error text is what the UI
    /// should surface to the student.</summary>
    Task<(int? Id, string? Error)>        CreateSubmissionAsync(CreateSubmissionRequest req);
    /// <summary>Student edits their own latest pending submission. Returns
    /// (true, null) on success or (false, Hebrew error) on rejection. Server
    /// enforces ownership + MentorStatus='Pending' + latest-row gating.</summary>
    Task<(bool Ok, string? Error)>        UpdateMySubmissionAsync(int submissionId, UpdateMySubmissionRequest req);
    /// <summary>Student deletes their own latest pending submission. Same
    /// server-side gating as UpdateMySubmissionAsync. On success the task
    /// rolls back to "Open" / "ReturnedForRevision" automatically.</summary>
    Task<(bool Ok, string? Error)>        DeleteMySubmissionAsync(int submissionId);
    /// <summary>Pre-submit live check: format + accessibility. Same code path
    /// the server runs on POST/PATCH, so a green result here is a strong
    /// guarantee the actual submission will not be rejected by the validator.</summary>
    Task<(bool Ok, string? Error)>        ValidateDriveLinkAsync(string url);
    /// <summary>Manual student confirmation that the official submission was
    /// completed in Moodle. Idempotent on the server. Returns the persisted
    /// timestamp + acting student name on success.</summary>
    Task<(bool Ok, DateTime? MoodleSubmittedAt, string? MoodleSubmittedBy, string? Error)>
        MarkMoodleSubmittedAsync(int submissionId);
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

    public async Task<(int? Id, string? Error)> CreateSubmissionAsync(CreateSubmissionRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/task-submissions", req);
            if (!resp.IsSuccessStatusCode)
            {
                // The server emits Hebrew error messages as a JSON string body
                // (BadRequest("…")). Strip the surrounding quotes if present.
                string body = await resp.Content.ReadAsStringAsync();
                body = body?.Trim().Trim('"') ?? "";
                return (null, string.IsNullOrEmpty(body) ? null : body);
            }
            var result = await resp.Content.ReadFromJsonAsync<CreateResult>();
            return (result?.Id, null);
        }
        catch { return (null, null); }
    }

    public async Task<(bool Ok, string? Error)> UpdateMySubmissionAsync(
        int submissionId, UpdateMySubmissionRequest req)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"api/task-submissions/{submissionId}", req);
            if (resp.IsSuccessStatusCode) return (true, null);

            string body = (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"') ?? "";
            return (false, string.IsNullOrEmpty(body) ? null : body);
        }
        catch { return (false, null); }
    }

    public async Task<(bool Ok, string? Error)> DeleteMySubmissionAsync(int submissionId)
    {
        try
        {
            var resp = await _http.DeleteAsync($"api/task-submissions/{submissionId}");
            if (resp.IsSuccessStatusCode) return (true, null);

            string body = (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"') ?? "";
            return (false, string.IsNullOrEmpty(body) ? null : body);
        }
        catch { return (false, null); }
    }

    private sealed class MoodleMarkResponse
    {
        public int       SubmissionId       { get; set; }
        public bool      WasAlreadyMarked   { get; set; }
        public DateTime? MoodleSubmittedAt  { get; set; }
        public string?   MoodleSubmittedBy  { get; set; }
    }

    public async Task<(bool Ok, DateTime? MoodleSubmittedAt, string? MoodleSubmittedBy, string? Error)>
        MarkMoodleSubmittedAsync(int submissionId)
    {
        try
        {
            var resp = await _http.PostAsync(
                $"api/task-submissions/{submissionId}/mark-moodle-submitted", null);

            if (!resp.IsSuccessStatusCode)
            {
                string body = (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"') ?? "";
                return (false, null, null, string.IsNullOrEmpty(body) ? null : body);
            }

            var payload = await resp.Content.ReadFromJsonAsync<MoodleMarkResponse>();
            return (true, payload?.MoodleSubmittedAt, payload?.MoodleSubmittedBy, null);
        }
        catch { return (false, null, null, null); }
    }

    public async Task<(bool Ok, string? Error)> ValidateDriveLinkAsync(string url)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                "api/task-submissions/validate-drive-link",
                new ValidateDriveLinkRequest { DriveUrl = url });

            if (!resp.IsSuccessStatusCode)
            {
                string body = (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"') ?? "";
                return (false, string.IsNullOrEmpty(body) ? null : body);
            }

            var result = await resp.Content.ReadFromJsonAsync<ValidateDriveLinkResponse>();
            return (result?.Ok ?? false, result?.Error);
        }
        catch { return (false, null); }
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