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
}
