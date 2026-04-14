using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface ICatalogService
{
    Task<List<CatalogProjectListDto>?>  GetCatalogAsync();
    Task<CatalogProjectDetailDto?>      GetProjectDetailAsync(int id);
    Task<List<ProjectTypeOptionDto>>    GetProjectTypesAsync();
    Task<List<AcademicYearDto>?>        GetAcademicYearsAsync();
    Task<bool>                          CreateProjectAsync(SaveCatalogProjectRequest request);
    Task<bool>                          UpdateProjectAsync(int id, SaveCatalogProjectRequest request);
    Task<bool>                          ToggleAvailabilityAsync(int id);
    Task<string?>                       CreateProjectWithErrorAsync(SaveCatalogProjectRequest request);
    Task<string?>                       UpdateProjectWithErrorAsync(int id, SaveCatalogProjectRequest request);
}

public class CatalogService : ICatalogService
{
    private readonly HttpClient _http;

    public CatalogService(HttpClient http) => _http = http;

    public async Task<List<CatalogProjectListDto>?> GetCatalogAsync()
    {
        try { return await _http.GetFromJsonAsync<List<CatalogProjectListDto>>("api/catalog"); }
        catch { return null; }
    }

    public async Task<CatalogProjectDetailDto?> GetProjectDetailAsync(int id)
    {
        try { return await _http.GetFromJsonAsync<CatalogProjectDetailDto>($"api/catalog/{id}"); }
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

    public async Task<List<AcademicYearDto>?> GetAcademicYearsAsync()
    {
        try { return await _http.GetFromJsonAsync<List<AcademicYearDto>>("api/management/academic-years"); }
        catch { return null; }
    }

    public async Task<bool> CreateProjectAsync(SaveCatalogProjectRequest request)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/catalog", request);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateProjectAsync(int id, SaveCatalogProjectRequest request)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"api/catalog/{id}", request);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> ToggleAvailabilityAsync(int id)
    {
        try
        {
            var resp = await _http.PatchAsync($"api/catalog/{id}/toggle-available", null);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Returns the error message from a failed save, or null on success.</summary>
    public async Task<string?> CreateProjectWithErrorAsync(SaveCatalogProjectRequest request)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/catalog", request);
            if (resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<string?> UpdateProjectWithErrorAsync(int id, SaveCatalogProjectRequest request)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"api/catalog/{id}", request);
            if (resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch { return "שגיאת תקשורת"; }
    }
}