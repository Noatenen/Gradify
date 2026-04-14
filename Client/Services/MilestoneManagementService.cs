using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IMilestoneManagementService
{
    Task<List<MilestoneTemplateDto>?>  GetTemplatesAsync(int? projectTypeId = null);
    Task<MilestoneTemplateDto?>        GetTemplateAsync(int id);
    Task<List<ProjectTypeOptionDto>>   GetProjectTypesAsync();
    Task<string?>                      CreateTemplateAsync(SaveMilestoneTemplateRequest request);
    Task<string?>                      UpdateTemplateAsync(int id, SaveMilestoneTemplateRequest request);
    Task<bool>                         ToggleActiveAsync(int id);
}

public class MilestoneManagementService : IMilestoneManagementService
{
    private readonly HttpClient _http;

    public MilestoneManagementService(HttpClient http) => _http = http;

    public async Task<List<MilestoneTemplateDto>?> GetTemplatesAsync(int? projectTypeId = null)
    {
        try
        {
            var url = projectTypeId.HasValue
                ? $"api/milestone-templates?projectTypeId={projectTypeId}"
                : "api/milestone-templates";
            return await _http.GetFromJsonAsync<List<MilestoneTemplateDto>>(url);
        }
        catch { return null; }
    }

    public async Task<MilestoneTemplateDto?> GetTemplateAsync(int id)
    {
        try { return await _http.GetFromJsonAsync<MilestoneTemplateDto>($"api/milestone-templates/{id}"); }
        catch { return null; }
    }

    public async Task<List<ProjectTypeOptionDto>> GetProjectTypesAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ProjectTypeOptionDto>>("api/management/project-types")
                   ?? new List<ProjectTypeOptionDto>();
        }
        catch { return new List<ProjectTypeOptionDto>(); }
    }

    public async Task<string?> CreateTemplateAsync(SaveMilestoneTemplateRequest request)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/milestone-templates", request);
            if (resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<string?> UpdateTemplateAsync(int id, SaveMilestoneTemplateRequest request)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"api/milestone-templates/{id}", request);
            if (resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<bool> ToggleActiveAsync(int id)
    {
        try
        {
            var resp = await _http.PatchAsync($"api/milestone-templates/{id}/toggle-active", null);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
