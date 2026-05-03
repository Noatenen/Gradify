using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IProjectRequestsService
{
    Task<List<ProjectRequestRowDto>?>    GetAllAsync();
    Task<ProjectRequestDetailDto?>       GetByIdAsync(int id);
    Task<List<StudentOwnRequestDto>?>    GetMyRequestsAsync();
    Task<List<AssignableUserDto>?>       GetAssignableUsersAsync();
    Task<int?>                           CreateAsync(CreateProjectRequestRequest req);
    Task<string?>                        HandleAsync(int id, HandleProjectRequestRequest req);
    Task<string?>                        ReplyAsync(int id, string comment, List<RequestAttachmentUploadRequest>? attachments = null);
    Task<string?>                        UpdateAsync(int id, UpdateProjectRequestRequest req);
    Task<string?>                        SubmitExtensionDecisionAsync(int id, ExtensionDecisionRequest req);
    Task<List<ExtensionTargetDto>?>      GetExtensionTargetsAsync();
}

public class ProjectRequestsService : IProjectRequestsService
{
    private readonly HttpClient _http;

    public ProjectRequestsService(HttpClient http) => _http = http;

    public async Task<List<ProjectRequestRowDto>?> GetAllAsync()
    {
        try { return await _http.GetFromJsonAsync<List<ProjectRequestRowDto>>("api/project-requests"); }
        catch { return null; }
    }

    public async Task<ProjectRequestDetailDto?> GetByIdAsync(int id)
    {
        try { return await _http.GetFromJsonAsync<ProjectRequestDetailDto>($"api/project-requests/{id}"); }
        catch { return null; }
    }

    public async Task<List<StudentOwnRequestDto>?> GetMyRequestsAsync()
    {
        try { return await _http.GetFromJsonAsync<List<StudentOwnRequestDto>>("api/project-requests/my"); }
        catch { return null; }
    }

    public async Task<List<AssignableUserDto>?> GetAssignableUsersAsync()
    {
        try { return await _http.GetFromJsonAsync<List<AssignableUserDto>>("api/project-requests/assignable-users"); }
        catch { return null; }
    }

    public async Task<int?> CreateAsync(CreateProjectRequestRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/project-requests", req);
            if (!resp.IsSuccessStatusCode) return null;
            var result = await resp.Content.ReadFromJsonAsync<CreateResult>();
            return result?.Id;
        }
        catch { return null; }
    }

    /// <summary>
    /// Unified handling action — updates status / priority / assignee and records events.
    /// Returns null on success, or an error message string on failure.
    /// </summary>
    public async Task<string?> HandleAsync(int id, HandleProjectRequestRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"api/project-requests/{id}/handle", req);
            if (resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<string?> ReplyAsync(int id, string comment, List<RequestAttachmentUploadRequest>? attachments = null)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"api/project-requests/{id}/reply",
                new StudentReplyRequest { Comment = comment, Attachments = attachments ?? new() });
            if (resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<string?> UpdateAsync(int id, UpdateProjectRequestRequest req)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync($"api/project-requests/{id}", req);
            if (resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<string?> SubmitExtensionDecisionAsync(int id, ExtensionDecisionRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"api/project-requests/{id}/extension/decision", req);
            if (resp.IsSuccessStatusCode) return null;
            string body = await resp.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(body) ? "שגיאה בשליחת ההחלטה" : body.Trim('"');
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<List<ExtensionTargetDto>?> GetExtensionTargetsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ExtensionTargetDto>>(
                "api/project-requests/extension-targets");
        }
        catch { return null; }
    }

    private sealed class CreateResult { public int Id { get; set; } }
}
