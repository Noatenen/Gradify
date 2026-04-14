using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IResourceFilesService
{
    Task<List<ResourceFileDto>?>     GetAllAsync();
    Task<List<MilestoneOptionDto>>   GetMilestonesAsync();
    Task<List<TaskOptionDto>>        GetTasksForMilestoneAsync(int milestoneId);
    Task<bool>                       UploadAsync(UploadResourceFileRequest request);
    Task<bool>                       DeleteAsync(int id);
}

public class ResourceFilesService : IResourceFilesService
{
    private readonly HttpClient _http;

    public ResourceFilesService(HttpClient http) => _http = http;

    public async Task<List<ResourceFileDto>?> GetAllAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ResourceFileDto>>("api/resourcefiles");
        }
        catch { return null; }
    }

    public async Task<List<MilestoneOptionDto>> GetMilestonesAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<MilestoneOptionDto>>("api/resourcefiles/milestones")
                   ?? new List<MilestoneOptionDto>();
        }
        catch { return new List<MilestoneOptionDto>(); }
    }

    public async Task<List<TaskOptionDto>> GetTasksForMilestoneAsync(int milestoneId)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<TaskOptionDto>>($"api/resourcefiles/tasks/{milestoneId}")
                   ?? new List<TaskOptionDto>();
        }
        catch { return new List<TaskOptionDto>(); }
    }

    public async Task<bool> UploadAsync(UploadResourceFileRequest request)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/resourcefiles", request);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var resp = await _http.DeleteAsync($"api/resourcefiles/{id}");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
