using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IResourceFilesService
{
    /// <summary>Admin/Staff — full list including all metadata.</summary>
    Task<List<ResourceFileDto>?>   GetAllAsync();
    /// <summary>All authenticated users — same data, student-accessible endpoint.</summary>
    Task<List<ResourceFileDto>?>   GetPublicAsync();
    Task<List<MilestoneOptionDto>> GetMilestonesAsync();
    Task<List<TaskOptionDto>>      GetTasksForMilestoneAsync(int milestoneId);
    /// <summary>Returns null on success, or the server error message on failure.</summary>
    Task<string?>                  UploadAsync(UploadResourceFileRequest request);
    Task<bool>                     UpdateAsync(int id, UpdateResourceFileRequest request);
    Task<bool>                     DeleteAsync(int id);
}

public class ResourceFilesService : IResourceFilesService
{
    private readonly HttpClient _http;

    public ResourceFilesService(HttpClient http) => _http = http;

    public async Task<List<ResourceFileDto>?> GetAllAsync()
    {
        try   { return await _http.GetFromJsonAsync<List<ResourceFileDto>>("api/resourcefiles"); }
        catch { return null; }
    }

    public async Task<List<ResourceFileDto>?> GetPublicAsync()
    {
        try   { return await _http.GetFromJsonAsync<List<ResourceFileDto>>("api/student/resources"); }
        catch { return null; }
    }

    public async Task<List<MilestoneOptionDto>> GetMilestonesAsync()
    {
        try   { return await _http.GetFromJsonAsync<List<MilestoneOptionDto>>("api/resourcefiles/milestones") ?? new(); }
        catch { return new(); }
    }

    public async Task<List<TaskOptionDto>> GetTasksForMilestoneAsync(int milestoneId)
    {
        try   { return await _http.GetFromJsonAsync<List<TaskOptionDto>>($"api/resourcefiles/tasks/{milestoneId}") ?? new(); }
        catch { return new(); }
    }

    public async Task<string?> UploadAsync(UploadResourceFileRequest request)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/resourcefiles", request);
            if (resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(body) ? $"שגיאת שרת ({(int)resp.StatusCode})" : body;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task<bool> UpdateAsync(int id, UpdateResourceFileRequest request)
    {
        try   { return (await _http.PutAsJsonAsync($"api/resourcefiles/{id}", request)).IsSuccessStatusCode; }
        catch { return false; }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try   { return (await _http.DeleteAsync($"api/resourcefiles/{id}")).IsSuccessStatusCode; }
        catch { return false; }
    }
}
