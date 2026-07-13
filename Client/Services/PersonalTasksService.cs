using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IPersonalTasksService
{
    Task<List<PersonalTaskDto>>                          GetAsync();
    Task<(PersonalTaskDto? Task, string? Error)>         CreateAsync(CreatePersonalTaskRequest req);
    Task<bool>                                           ToggleAsync(int id);
}

public class PersonalTasksService : IPersonalTasksService
{
    private readonly HttpClient _http;

    public PersonalTasksService(HttpClient http) => _http = http;

    public async Task<List<PersonalTaskDto>> GetAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<PersonalTaskDto>>("api/projects/personal-tasks")
                   ?? new List<PersonalTaskDto>();
        }
        catch { return new List<PersonalTaskDto>(); }
    }

    public async Task<(PersonalTaskDto? Task, string? Error)> CreateAsync(CreatePersonalTaskRequest req)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("api/projects/personal-tasks", req);
            if (res.IsSuccessStatusCode)
                return (await res.Content.ReadFromJsonAsync<PersonalTaskDto>(), null);
            var err = await res.Content.ReadAsStringAsync();
            return (null, string.IsNullOrWhiteSpace(err) ? "שגיאה בשמירת המשימה" : err.Trim('"'));
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<bool> ToggleAsync(int id)
    {
        try
        {
            var res = await _http.PatchAsync($"api/projects/personal-tasks/{id}/toggle", null);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}