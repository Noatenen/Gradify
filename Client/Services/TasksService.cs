using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface ITasksService
{
    /// <summary>
    /// Fetches the full tasks page payload for the current authenticated user.
    /// Returns null on network/auth error.
    /// Returns a TasksPageDto with empty groups when the user has no project.
    /// </summary>
    Task<TasksPageDto?> GetTasksAsync();

    /// <summary>
    /// Fetches the full task detail + submission history for a single task.
    /// Returns null on error or when the task does not belong to the student's project.
    /// </summary>
    Task<TaskDetailDto?> GetTaskDetailAsync(int taskId);

    /// <summary>
    /// Updates the student's own progress status for a task.
    /// Valid values: "Open" | "InProgress" | "SubmittedToMentor" |
    ///               "ReturnedForRevision" | "RevisionSubmitted" |
    ///               "ApprovedForSubmission" | "Done"
    /// Returns true on success.
    /// </summary>
    Task<bool> UpdateTaskProgressAsync(int taskId, string status);

    // ── Student sub-task CRUD ─────────────────────────────────────────────────

    /// <summary>Returns the team's internal sub-tasks for a given parent task.</summary>
    Task<List<StudentSubTaskDto>> GetSubTasksAsync(int taskId);

    /// <summary>Creates a new internal sub-task. Returns the created item, or null on error.</summary>
    Task<StudentSubTaskDto?> CreateSubTaskAsync(int taskId, CreateSubTaskRequest req);

    /// <summary>Toggles the IsDone flag on a sub-task. Returns true on success.</summary>
    Task<bool> ToggleSubTaskAsync(int subTaskId);

    /// <summary>Updates title, status, due date and notes on a sub-task. Returns true on success.</summary>
    Task<bool> UpdateSubTaskAsync(int subTaskId, UpdateSubTaskRequest req);

    /// <summary>Deletes a sub-task. Returns true on success.</summary>
    Task<bool> DeleteSubTaskAsync(int subTaskId);
}

public class TasksService : ITasksService
{
    private readonly HttpClient _http;

    public TasksService(HttpClient http) => _http = http;

    public async Task<TasksPageDto?> GetTasksAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<TasksPageDto>("api/projects/my-tasks");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Task detail, or null when it could not be fetched.
    ///
    /// <para><b>Null still means "show the error state" — but it is no longer
    /// silent.</b> This used to be a bare <c>catch { return null; }</c>: a 401,
    /// a 404, a 500 and a malformed body all collapsed into the same null with
    /// nothing written down, so the modal's "אירעה שגיאה בטעינת פרטי המשימה"
    /// was the only trace any failure ever left and there was no way to tell
    /// which of the four had happened. The request is now sent explicitly so a
    /// non-success status can be reported with its code and URL before the null
    /// is returned.</para>
    ///
    /// <para>The CONTRACT is unchanged — every caller still reads null as "no
    /// task", and the four pages that host TaskDetailModal render exactly what
    /// they rendered before. Nothing is swallowed that was not already the
    /// documented return value; what changed is that the failure now says what
    /// it was.</para>
    /// </summary>
    public async Task<TaskDetailDto?> GetTaskDetailAsync(int taskId)
    {
        var url = $"api/projects/tasks/{taskId}/detail";
        try
        {
            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                Console.Error.WriteLine(
                    $"[TasksService] GET {url} failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
                return null;
            }

            return await response.Content.ReadFromJsonAsync<TaskDetailDto>();
        }
        catch (Exception ex)
        {
            // Transport failure or a body that would not deserialise. Reported
            // rather than discarded, for the same reason as above.
            Console.Error.WriteLine($"[TasksService] GET {url} threw: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> UpdateTaskProgressAsync(int taskId, string status)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"api/projects/tasks/{taskId}/progress",
                new UpdateTaskProgressRequest { Status = status });
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<StudentSubTaskDto>> GetSubTasksAsync(int taskId)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<StudentSubTaskDto>>(
                $"api/projects/tasks/{taskId}/subtasks") ?? new();
        }
        catch
        {
            return new();
        }
    }

    public async Task<StudentSubTaskDto?> CreateSubTaskAsync(int taskId, CreateSubTaskRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"api/projects/tasks/{taskId}/subtasks", req);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<StudentSubTaskDto>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> ToggleSubTaskAsync(int subTaskId)
    {
        try
        {
            var resp = await _http.PatchAsync(
                $"api/projects/subtasks/{subTaskId}/toggle", null);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateSubTaskAsync(int subTaskId, UpdateSubTaskRequest req)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"api/projects/subtasks/{subTaskId}", req);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteSubTaskAsync(int subTaskId)
    {
        try
        {
            var resp = await _http.DeleteAsync($"api/projects/subtasks/{subTaskId}");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
