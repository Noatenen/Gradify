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
    Task<(bool ok, string? error)>    UpdateProjectAsync(int id, UpdateProjectRequest request);

    // ── Academic Years (Cycles) ───────────────────────────────────────────────
    Task<List<AcademicYearDto>?>      GetAcademicYearsAsync();
    Task<bool>                        CreateAcademicYearAsync(SaveAcademicYearRequest request);
    Task<bool>                        UpdateAcademicYearAsync(int id, SaveAcademicYearRequest request);
    Task<bool>                        SetCurrentYearAsync(int id);
    Task<bool>                        ToggleYearActiveAsync(int id);
    Task<bool>                        CloseYearAsync(int id);
    Task<bool>                        ArchiveYearAsync(int id);
    Task<ApplyTemplatesResultDto?>    ApplyTemplatesAsync(int id);

    // ── Cycle Program — the milestones configured for ONE cycle ───────────────
    // Backed by /api/academic-years/{yearId}/program. Every row is an
    // AcademicYearMilestones row; see CycleProgramDto for the template-vs-cycle
    // field split these calls write across.
    Task<AcademicYearDto?>                    GetAcademicYearAsync(int id);
    Task<List<CycleMilestoneDto>?>            GetCycleProgramAsync(int yearId);
    Task<List<AvailableMilestoneTemplateDto>?> GetAvailableMilestoneTemplatesAsync(int yearId);
    Task<(bool ok, string? error)>            CreateCycleMilestoneAsync(int yearId, SaveCycleMilestoneRequest request);
    Task<(bool ok, string? error)>            UpdateCycleMilestoneAsync(int yearId, int aymId, SaveCycleMilestoneRequest request);
    Task<bool>                                ToggleCycleMilestoneActiveAsync(int yearId, int aymId);
    Task<bool>                                MoveCycleMilestoneAsync(int yearId, int aymId, int direction);
    Task<(bool ok, string? error)>            DeleteCycleMilestoneAsync(int yearId, int aymId);
    Task<ApplyMilestoneTemplatesResultDto?>   ApplyMilestoneTemplatesAsync(int yearId, List<int> templateIds);

    // Airtable sync method intentionally removed from this interface.
    // The only legitimate import path is now:
    //   IAirtableIntegrationService.PreviewAsync(id)
    //     ↓ admin reviews + confirms
    //   IAirtableIntegrationService.ImportAsync(id, skipRecordIds)
    // Callers that used to invoke ManagementService.SyncAirtableProjectsAsync()
    // bypassed the preview gate; the catalog button (the only known caller)
    // now navigates to /management/integrations/airtable?triggerPreviewForActive=1
    // which routes through the preview→confirm flow.
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

    public async Task<(bool ok, string? error)> UpdateProjectAsync(int id, UpdateProjectRequest request)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"api/management/projects/{id}", request);
            if (resp.IsSuccessStatusCode) return (true, null);
            var msg = await resp.Content.ReadAsStringAsync();
            return (false, msg);
        }
        catch { return (false, null); }
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

    public async Task<ApplyTemplatesResultDto?> ApplyTemplatesAsync(int id)
    {
        try
        {
            var resp = await _http.PostAsync($"api/academic-years/{id}/apply-templates", null);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<ApplyTemplatesResultDto>();
        }
        catch { return null; }
    }

    // ── Cycle Program ─────────────────────────────────────────────────────────
    //
    // The write calls return the SERVER'S message rather than a bare bool. These
    // endpoints refuse work for reasons the admin has to be told about and
    // cannot guess — a milestone already held by N projects, a closed cycle, a
    // due date before the start date — so swallowing the body into `false` would
    // strand them. Reads keep the nullable-list convention used above.

    public async Task<AcademicYearDto?> GetAcademicYearAsync(int id)
    {
        try { return await _http.GetFromJsonAsync<AcademicYearDto>($"api/academic-years/{id}"); }
        catch { return null; }
    }

    public async Task<List<CycleMilestoneDto>?> GetCycleProgramAsync(int yearId)
    {
        try { return await _http.GetFromJsonAsync<List<CycleMilestoneDto>>($"api/academic-years/{yearId}/program"); }
        catch { return null; }
    }

    public async Task<List<AvailableMilestoneTemplateDto>?> GetAvailableMilestoneTemplatesAsync(int yearId)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<AvailableMilestoneTemplateDto>>(
                $"api/academic-years/{yearId}/available-templates");
        }
        catch { return null; }
    }

    public async Task<(bool ok, string? error)> CreateCycleMilestoneAsync(
        int yearId, SaveCycleMilestoneRequest request)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"api/academic-years/{yearId}/program", request);
            return await ReadResultAsync(resp);
        }
        catch { return (false, null); }
    }

    public async Task<(bool ok, string? error)> UpdateCycleMilestoneAsync(
        int yearId, int aymId, SaveCycleMilestoneRequest request)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"api/academic-years/{yearId}/program/{aymId}", request);
            return await ReadResultAsync(resp);
        }
        catch { return (false, null); }
    }

    public async Task<bool> ToggleCycleMilestoneActiveAsync(int yearId, int aymId)
    {
        try
        {
            var resp = await _http.PatchAsync(
                $"api/academic-years/{yearId}/program/{aymId}/toggle-active", null);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> MoveCycleMilestoneAsync(int yearId, int aymId, int direction)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"api/academic-years/{yearId}/program/{aymId}/move",
                new MoveCycleMilestoneRequest { Direction = direction });
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<(bool ok, string? error)> DeleteCycleMilestoneAsync(int yearId, int aymId)
    {
        try
        {
            var resp = await _http.DeleteAsync($"api/academic-years/{yearId}/program/{aymId}");
            return await ReadResultAsync(resp);
        }
        catch { return (false, null); }
    }

    public async Task<ApplyMilestoneTemplatesResultDto?> ApplyMilestoneTemplatesAsync(
        int yearId, List<int> templateIds)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"api/academic-years/{yearId}/program/apply-templates",
                new ApplyMilestoneTemplatesRequest { TemplateIds = templateIds });

            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<ApplyMilestoneTemplatesResultDto>();
        }
        catch { return null; }
    }

    /// <summary>
    /// Success, or the server's own Hebrew explanation. The controllers return
    /// their reasons as a plain-string body (BadRequest/Conflict/NotFound), so
    /// the body IS the message — no envelope to unwrap.
    /// </summary>
    private static async Task<(bool ok, string? error)> ReadResultAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return (true, null);

        var body = await resp.Content.ReadAsStringAsync();
        return (false, string.IsNullOrWhiteSpace(body) ? null : body.Trim('"'));
    }

    // SyncAirtableProjectsAsync intentionally removed — see interface comment.
}
