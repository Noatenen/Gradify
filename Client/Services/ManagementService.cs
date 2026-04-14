using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IManagementService
{
    // ── Projects ──────────────────────────────────────────────────────────────
    Task<List<ProjectManagementDto>?> GetProjectsAsync();
    Task<List<ProjectTypeOptionDto>>  GetProjectTypesAsync();
    Task<bool>                        CreateProjectAsync(CreateProjectRequest request);
    Task<bool>                        UpdateProjectStatusAsync(int id, string status);

    // ── Academic Years (Cycles) ───────────────────────────────────────────────
    Task<List<AcademicYearDto>?>      GetAcademicYearsAsync();
    Task<bool>                        CreateAcademicYearAsync(SaveAcademicYearRequest request);
    Task<bool>                        UpdateAcademicYearAsync(int id, SaveAcademicYearRequest request);
    Task<bool>                        SetCurrentYearAsync(int id);
    Task<bool>                        ToggleYearActiveAsync(int id);
    Task<bool>                        CloseYearAsync(int id);
    Task<bool>                        ArchiveYearAsync(int id);
}

public class ManagementService : IManagementService
{
    private readonly HttpClient _http;

    public ManagementService(HttpClient http) => _http = http;

    public async Task<List<ProjectManagementDto>?> GetProjectsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ProjectManagementDto>>("api/management/projects");
        }
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

    public async Task<bool> CreateProjectAsync(CreateProjectRequest request)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/management/projects", request);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateProjectStatusAsync(int id, string status)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"api/management/projects/{id}/status",
                new UpdateProjectStatusRequest { Status = status });
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Academic Years (Cycles) ───────────────────────────────────────────────

    public async Task<List<AcademicYearDto>?> GetAcademicYearsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<AcademicYearDto>>("api/management/academic-years");
        }
        catch { return null; }
    }

    public async Task<bool> CreateAcademicYearAsync(SaveAcademicYearRequest request)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/management/academic-years", request);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateAcademicYearAsync(int id, SaveAcademicYearRequest request)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"api/management/academic-years/{id}", request);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> SetCurrentYearAsync(int id)
    {
        try
        {
            var resp = await _http.PatchAsync($"api/management/academic-years/{id}/set-current", null);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> ToggleYearActiveAsync(int id)
    {
        try
        {
            var resp = await _http.PatchAsync($"api/management/academic-years/{id}/toggle-active", null);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> CloseYearAsync(int id)
    {
        try
        {
            var resp = await _http.PostAsync($"api/management/academic-years/{id}/close", null);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> ArchiveYearAsync(int id)
    {
        try
        {
            var resp = await _http.PostAsync($"api/management/academic-years/{id}/archive", null);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
