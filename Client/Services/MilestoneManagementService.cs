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

    /// <summary>Copies the template and its task templates as a new inactive
    /// draft. Returns the new template's title, or an error message.</summary>
    Task<(string? title, string? error)> DuplicateTemplateAsync(int id);

    /// <summary>Physically deletes a template. Refused by the server (409) when
    /// any cycle or project depends on it; the message names the counts.</summary>
    Task<string?>                      DeleteTemplateAsync(int id);
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

    public async Task<(string? title, string? error)> DuplicateTemplateAsync(int id)
    {
        try
        {
            var resp = await _http.PostAsync($"api/milestone-templates/{id}/duplicate", null);

            if (!resp.IsSuccessStatusCode)
                return (null, await ReadErrorAsync(resp));

            var body = await resp.Content.ReadFromJsonAsync<DuplicateResult>();
            return (body?.Title, null);
        }
        catch { return (null, "שגיאת תקשורת"); }
    }

    public async Task<string?> DeleteTemplateAsync(int id)
    {
        try
        {
            var resp = await _http.DeleteAsync($"api/milestone-templates/{id}");
            return resp.IsSuccessStatusCode ? null : await ReadErrorAsync(resp);
        }
        catch { return "שגיאת תקשורת"; }
    }

    /// <summary>
    /// The server's own sentence. These endpoints refuse for reasons the admin
    /// cannot guess — a template held by N cycles and M project milestones — so
    /// the body IS the message. It is returned as a bare string, hence the trim.
    /// </summary>
    private static async Task<string> ReadErrorAsync(HttpResponseMessage resp)
    {
        var body = await resp.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(body) ? "הפעולה נכשלה" : body.Trim().Trim('"');
    }

    private sealed class DuplicateResult
    {
        public int    Id                  { get; set; }
        public string Title               { get; set; } = "";
        public int    TaskTemplatesCopied { get; set; }
    }
}
