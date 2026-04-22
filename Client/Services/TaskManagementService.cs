using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface ITaskManagementService
{
    Task<List<TaskTemplateDto>?>         GetTemplatesAsync();
    Task<List<MilestoneTemplateDto>?>    GetMilestoneTemplatesAsync();
    Task<List<OperationalTaskAdminDto>?> GetOperationalTasksAsync();
    Task<string?>                        CreateAsync(SaveTaskTemplateRequest request);
    Task<string?>                        UpdateAsync(int id, SaveTaskTemplateRequest request);
    Task<bool>                           DeleteAsync(int id);
    Task<bool>                           ToggleActiveAsync(int id);
}

public class TaskManagementService : ITaskManagementService
{
    private readonly HttpClient _http;

    public TaskManagementService(HttpClient http) => _http = http;

    public async Task<List<TaskTemplateDto>?> GetTemplatesAsync()
    {
        try { return await _http.GetFromJsonAsync<List<TaskTemplateDto>>("api/task-templates"); }
        catch { return null; }
    }

    public async Task<List<MilestoneTemplateDto>?> GetMilestoneTemplatesAsync()
    {
        try { return await _http.GetFromJsonAsync<List<MilestoneTemplateDto>>("api/milestone-templates"); }
        catch { return null; }
    }

    /// <summary>
    /// Returns ALL operational tasks regardless of TaskType.
    /// The caller is responsible for splitting by TaskType client-side.
    /// </summary>
    public async Task<List<OperationalTaskAdminDto>?> GetOperationalTasksAsync()
    {
        try { return await _http.GetFromJsonAsync<List<OperationalTaskAdminDto>>("api/task-templates/operational-tasks"); }
        catch { return null; }
    }

    public async Task<string?> CreateAsync(SaveTaskTemplateRequest request)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/task-templates", request);
            if (resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<string?> UpdateAsync(int id, SaveTaskTemplateRequest request)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"api/task-templates/{id}", request);
            if (resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var resp = await _http.DeleteAsync($"api/task-templates/{id}");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> ToggleActiveAsync(int id)
    {
        try
        {
            var resp = await _http.PatchAsync($"api/task-templates/{id}/toggle-active", null);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
