using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IAssignmentService
{
    Task<AssignmentContextDto?> GetContextAsync();
    Task<bool>                  SubmitAsync(SubmitAssignmentRequest req);
}

public class AssignmentService : IAssignmentService
{
    private readonly HttpClient _http;

    public AssignmentService(HttpClient http) => _http = http;

    public async Task<AssignmentContextDto?> GetContextAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<AssignmentContextDto>("api/assignment/context");
        }
        catch { return null; }
    }

    public async Task<bool> SubmitAsync(SubmitAssignmentRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/assignment/submit", req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
