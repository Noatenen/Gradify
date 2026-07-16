using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IRoadmapStagesService
{
    // Admin / Lecturer config
    Task<List<RoadmapStageDto>?>          GetForCycleAsync(int academicYearId);
    Task<List<AvailableMilestoneRowDto>?> GetAvailableMilestonesAsync(int academicYearId);
    Task<(RoadmapStageDto? Item, string? Error)> CreateAsync(int academicYearId, SaveRoadmapStageRequest req);
    Task<(RoadmapStageDto? Item, string? Error)> UpdateAsync(int id, SaveRoadmapStageRequest req);
    Task<string?> ToggleActiveAsync(int id, bool isActive);
    Task<(RoadmapStageDto? Item, string? Error)> LinkMilestonesAsync(int id, List<int> aymIds);
    Task<string?> DeleteAsync(int id);

    // Mentor / Admin progress
    Task<ProjectRoadmapProgressDto?>      GetProjectProgressAsync(int projectId);
    Task<List<MentorRoadmapEntryDto>?>    GetMentorProjectsProgressAsync();

    // Student progress (own project)
    Task<ProjectRoadmapProgressDto?>      GetMyProgressAsync();
}

/// <summary>Row returned by GET /api/roadmap-stages/cycle/{id}/available-milestones.
/// Lives in the client service because it isn't reused from server-side DTOs.</summary>
public class AvailableMilestoneRowDto
{
    public int    AcademicYearMilestoneId { get; set; }
    public int    MilestoneTemplateId     { get; set; }
    public string Title                   { get; set; } = "";
    public int    OrderIndex              { get; set; }
    public int?   CurrentStageId          { get; set; }
}

public class RoadmapStagesService : IRoadmapStagesService
{
    private readonly HttpClient _http;
    public RoadmapStagesService(HttpClient http) => _http = http;

    public async Task<List<RoadmapStageDto>?> GetForCycleAsync(int academicYearId)
    {
        try { return await _http.GetFromJsonAsync<List<RoadmapStageDto>>($"api/roadmap-stages/cycle/{academicYearId}"); }
        catch { return null; }
    }

    public async Task<List<AvailableMilestoneRowDto>?> GetAvailableMilestonesAsync(int academicYearId)
    {
        try { return await _http.GetFromJsonAsync<List<AvailableMilestoneRowDto>>($"api/roadmap-stages/cycle/{academicYearId}/available-milestones"); }
        catch { return null; }
    }

    public async Task<(RoadmapStageDto? Item, string? Error)> CreateAsync(int academicYearId, SaveRoadmapStageRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"api/roadmap-stages/cycle/{academicYearId}", req);
            if (!resp.IsSuccessStatusCode)
                return (null, (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"'));
            return (await resp.Content.ReadFromJsonAsync<RoadmapStageDto>(), null);
        }
        catch { return (null, "שגיאת תקשורת"); }
    }

    public async Task<(RoadmapStageDto? Item, string? Error)> UpdateAsync(int id, SaveRoadmapStageRequest req)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"api/roadmap-stages/{id}", req);
            if (!resp.IsSuccessStatusCode)
                return (null, (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"'));
            return (await resp.Content.ReadFromJsonAsync<RoadmapStageDto>(), null);
        }
        catch { return (null, "שגיאת תקשורת"); }
    }

    public async Task<string?> ToggleActiveAsync(int id, bool isActive)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"api/roadmap-stages/{id}/active",
                new ToggleRoadmapStageActiveRequest { IsActive = isActive });
            if (resp.IsSuccessStatusCode) return null;
            return (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"') ?? "שגיאה בעדכון מצב השלב";
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<(RoadmapStageDto? Item, string? Error)> LinkMilestonesAsync(int id, List<int> aymIds)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync(
                $"api/roadmap-stages/{id}/milestones",
                new LinkStageMilestonesRequest { AcademicYearMilestoneIds = aymIds });
            if (!resp.IsSuccessStatusCode)
                return (null, (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"'));
            return (await resp.Content.ReadFromJsonAsync<RoadmapStageDto>(), null);
        }
        catch { return (null, "שגיאת תקשורת"); }
    }

    public async Task<string?> DeleteAsync(int id)
    {
        try
        {
            var resp = await _http.DeleteAsync($"api/roadmap-stages/{id}");
            if (resp.IsSuccessStatusCode) return null;
            return (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"') ?? "שגיאה במחיקת השלב";
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<ProjectRoadmapProgressDto?> GetProjectProgressAsync(int projectId)
    {
        try { return await _http.GetFromJsonAsync<ProjectRoadmapProgressDto>($"api/roadmap-stages/projects/{projectId}/progress"); }
        catch { return null; }
    }

    public async Task<List<MentorRoadmapEntryDto>?> GetMentorProjectsProgressAsync()
    {
        try { return await _http.GetFromJsonAsync<List<MentorRoadmapEntryDto>>("api/roadmap-stages/mentor/projects-progress"); }
        catch { return null; }
    }

    public async Task<ProjectRoadmapProgressDto?> GetMyProgressAsync()
    {
        try { return await _http.GetFromJsonAsync<ProjectRoadmapProgressDto>("api/roadmap-stages/my-progress"); }
        catch { return null; }
    }
}