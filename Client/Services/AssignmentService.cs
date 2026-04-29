using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public record AssignmentSubmitResult(bool Ok, string? Error);

public interface IAssignmentService
{
    Task<AssignmentContextDto?>  GetContextAsync();
    Task<AssignmentSubmitResult> SubmitAsync(SubmitAssignmentRequest req);
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

    public async Task<AssignmentSubmitResult> SubmitAsync(SubmitAssignmentRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/assignment/submit", req);
            if (resp.IsSuccessStatusCode) return new AssignmentSubmitResult(true, null);

            string body = await resp.Content.ReadAsStringAsync();
            return new AssignmentSubmitResult(false, string.IsNullOrWhiteSpace(body) ? null : body);
        }
        catch (Exception ex)
        {
            return new AssignmentSubmitResult(false, ex.Message);
        }
    }
}
