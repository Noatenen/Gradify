using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface ITeamTasksService
{
    Task<List<TeamTaskDto>>                          GetAsync();
    Task<(TeamTaskDto? Task, string? Error)>         CreateAsync(CreateTeamTaskRequest req);
    Task<string?>                                    UpdateAsync(int id, UpdateTeamTaskRequest req);
    Task<bool>                                       ToggleAsync(int id);
    Task<bool>                                       DeleteAsync(int id);
}

public class TeamTasksService : ITeamTasksService
{
    private readonly HttpClient _http;

    public TeamTasksService(HttpClient http) => _http = http;

    public async Task<List<TeamTaskDto>> GetAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<TeamTaskDto>>("api/projects/team-tasks")
                   ?? new List<TeamTaskDto>();
        }
        catch { return new List<TeamTaskDto>(); }
    }

    public async Task<(TeamTaskDto? Task, string? Error)> CreateAsync(CreateTeamTaskRequest req)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("api/projects/team-tasks", req);
            if (res.IsSuccessStatusCode)
                return (await res.Content.ReadFromJsonAsync<TeamTaskDto>(), null);
            var err = await res.Content.ReadAsStringAsync();
            return (null, string.IsNullOrWhiteSpace(err) ? "שגיאה ביצירת המשימה" : err.Trim('"'));
        }
        catch (Exception ex) { return (null, ex.Message); }
    }

    public async Task<string?> UpdateAsync(int id, UpdateTeamTaskRequest req)
    {
        try
        {
            var res = await _http.PutAsJsonAsync($"api/projects/team-tasks/{id}", req);
            if (res.IsSuccessStatusCode) return null;
            var err = await res.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(err) ? "שגיאה בעדכון המשימה" : err.Trim('"');
        }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task<bool> ToggleAsync(int id)
    {
        try
        {
            var res = await _http.PatchAsync($"api/projects/team-tasks/{id}/toggle", null);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var res = await _http.DeleteAsync($"api/projects/team-tasks/{id}");
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}