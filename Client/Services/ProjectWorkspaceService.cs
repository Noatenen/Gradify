using System.Net;
using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

/// <summary>
/// How a project-workspace load ended. The three cases are visually different
/// screens and must not be collapsed into "null": a student who has not been
/// placed on a team yet is not an error, and an error must not be dressed up
/// as "you have no project".
/// </summary>
public enum ProjectLoadState
{
    /// <summary>The project was returned.</summary>
    Loaded,

    /// <summary>The signed-in student is not on an active team (the endpoint
    /// answers 404). Not an error.</summary>
    NoProject,

    /// <summary>Network failure or a non-success status.</summary>
    Error
}

public sealed record ProjectLoadResult(ProjectLoadState State, StudentProjectDetailsDto? Project);

public interface IProjectWorkspaceService
{
    /// <summary>Reads the student-safe details of the caller's own project
    /// (GET api/projects/my-project-details). Never throws.</summary>
    Task<ProjectLoadResult> GetProjectAsync();

    /// <summary>
    /// Writes the team's display name + description
    /// (PUT api/projects/my-project). Returns true on success.
    ///
    /// <para>The caller is responsible for refreshing
    /// <see cref="IProjectContextService"/> afterwards, so the sidebar and every
    /// other screen that names the project agree with what was just saved.</para>
    /// </summary>
    Task<bool> UpdateProjectAsync(string title, string description);

    /// <summary>Uploads or replaces the team's project logo
    /// (PUT api/projects/my-project/logo). Returns the new public URL on
    /// success, null on any failure — the caller re-reads the project rather
    /// than trusting a locally-composed path.</summary>
    Task<string?> UploadLogoAsync(string imageBase64, string extension);

    /// <summary>Removes the team's project logo
    /// (DELETE api/projects/my-project/logo). Returns true on success.</summary>
    Task<bool> RemoveLogoAsync();

    /// <summary>The team's own links. Returns null on failure, an empty list
    /// when the team simply has none yet — the two are different screens.</summary>
    Task<List<ProjectResourceDto>?> GetResourcesAsync();

    /// <summary>Adds a link. Returns the created resource, or null on failure
    /// (including a URL the server refused).</summary>
    /// <param name="deliverableKey">Optional catalog key of the submission
    /// deliverable this resource belongs to; null for none.</param>
    Task<ProjectResourceDto?> AddResourceAsync(string label, string url, string? deliverableKey);

    /// <summary>Edits a link the caller's team owns. Returns the updated
    /// resource, or null on failure (including a URL the server refused).</summary>
    /// <param name="deliverableKey">The deliverable association after the edit —
    /// null CLEARS it, so this always states the intended end state.</param>
    Task<ProjectResourceDto?> UpdateResourceAsync(int id, string label, string url, string? deliverableKey);

    /// <summary>Removes a link the caller's team owns.</summary>
    Task<bool> DeleteResourceAsync(int id);

    /// <summary>The team's status per submission category. Categories the team
    /// has never touched are simply absent — the caller treats absence as
    /// "not started". Returns null on failure.</summary>
    Task<List<SubmissionStatusDto>?> GetSubmissionProgressAsync();

    /// <summary>Sets one category's status.</summary>
    Task<bool> SetSubmissionStatusAsync(string deliverableKey, string status);
}

/// <summary>
/// Client for the student's own project workspace (/project).
///
/// Follows the same shape as StudentProfileService — thin HttpClient wrapper,
/// every call swallowing its exception and reporting the outcome in the return
/// value, so no page has to handle a transport exception.
///
/// Unlike ProjectContextService this holds NO cache: the workspace page is the
/// screen where the project's own fields are edited, so a stale copy would be
/// visible immediately.
/// </summary>
public class ProjectWorkspaceService : IProjectWorkspaceService
{
    private readonly HttpClient _http;

    public ProjectWorkspaceService(HttpClient http) => _http = http;

    public async Task<ProjectLoadResult> GetProjectAsync()
    {
        try
        {
            var res = await _http.GetAsync("api/projects/my-project-details");

            if (res.StatusCode == HttpStatusCode.NotFound)
                return new ProjectLoadResult(ProjectLoadState.NoProject, null);

            if (!res.IsSuccessStatusCode)
                return new ProjectLoadResult(ProjectLoadState.Error, null);

            var dto = await res.Content.ReadFromJsonAsync<StudentProjectDetailsDto>();

            return dto is null
                ? new ProjectLoadResult(ProjectLoadState.Error, null)
                : new ProjectLoadResult(ProjectLoadState.Loaded, dto);
        }
        catch
        {
            return new ProjectLoadResult(ProjectLoadState.Error, null);
        }
    }

    public async Task<bool> UpdateProjectAsync(string title, string description)
    {
        try
        {
            var res = await _http.PutAsJsonAsync(
                "api/projects/my-project",
                new UpdateMyProjectRequest { Title = title, Description = description });

            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> UploadLogoAsync(string imageBase64, string extension)
    {
        try
        {
            var res = await _http.PutAsJsonAsync(
                "api/projects/my-project/logo",
                new UploadProjectLogoRequest
                {
                    ImageBase64 = imageBase64,
                    Extension   = extension,
                });

            if (!res.IsSuccessStatusCode) return null;

            var body = await res.Content.ReadFromJsonAsync<LogoUploadResult>();
            return string.IsNullOrWhiteSpace(body?.Url) ? null : body.Url;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> RemoveLogoAsync()
    {
        try
        {
            var res = await _http.DeleteAsync("api/projects/my-project/logo");
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private sealed class LogoUploadResult
    {
        public string? Url { get; set; }
    }

    public async Task<List<ProjectResourceDto>?> GetResourcesAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ProjectResourceDto>>("api/projects/my-resources");
        }
        catch
        {
            return null;
        }
    }

    public async Task<ProjectResourceDto?> AddResourceAsync(string label, string url, string? deliverableKey)
    {
        try
        {
            var res = await _http.PostAsJsonAsync(
                "api/projects/my-resources",
                new CreateProjectResourceRequest
                {
                    Label          = label,
                    Url            = url,
                    DeliverableKey = deliverableKey,
                });

            if (!res.IsSuccessStatusCode) return null;

            return await res.Content.ReadFromJsonAsync<ProjectResourceDto>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<ProjectResourceDto?> UpdateResourceAsync(int id, string label, string url, string? deliverableKey)
    {
        try
        {
            var res = await _http.PutAsJsonAsync(
                $"api/projects/my-resources/{id}",
                new CreateProjectResourceRequest
                {
                    Label          = label,
                    Url            = url,
                    DeliverableKey = deliverableKey,
                });

            if (!res.IsSuccessStatusCode) return null;

            return await res.Content.ReadFromJsonAsync<ProjectResourceDto>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> DeleteResourceAsync(int id)
    {
        try
        {
            var res = await _http.DeleteAsync($"api/projects/my-resources/{id}");
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<SubmissionStatusDto>?> GetSubmissionProgressAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<SubmissionStatusDto>>(
                "api/projects/my-submission-progress");
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SetSubmissionStatusAsync(string deliverableKey, string status)
    {
        try
        {
            var res = await _http.PutAsJsonAsync(
                $"api/projects/my-submission-progress/{Uri.EscapeDataString(deliverableKey)}",
                new UpdateDeliverableStatusRequest { Status = status });

            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
