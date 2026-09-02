using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

/// <summary>
/// Student-side access to forms built in the admin form editor.
///
/// The project-assignment form is deliberately NOT reachable through here — it
/// is answered at /student/assignment against its own domain tables, and the
/// server rejects it on these routes rather than rendering it generically.
/// </summary>
public interface IFormResponseService
{
    Task<List<StudentFormListItemDto>?> GetAvailableAsync();
    Task<FormFillDto?>                  GetFormAsync(int formId);

    /// <summary>Returns null on success, or the server's Hebrew message.</summary>
    Task<string?> SubmitAsync(int formId, SubmitFormResponseRequest req);
}

public class FormResponseService : IFormResponseService
{
    private readonly HttpClient _http;

    public FormResponseService(HttpClient http) => _http = http;

    public async Task<List<StudentFormListItemDto>?> GetAvailableAsync()
    {
        try { return await _http.GetFromJsonAsync<List<StudentFormListItemDto>>("api/form-responses/available"); }
        catch { return null; }
    }

    public async Task<FormFillDto?> GetFormAsync(int formId)
    {
        try { return await _http.GetFromJsonAsync<FormFillDto>($"api/form-responses/{formId}"); }
        catch { return null; }
    }

    public async Task<string?> SubmitAsync(int formId, SubmitFormResponseRequest req)
    {
        try
        {
            var res = await _http.PostAsJsonAsync($"api/form-responses/{formId}", req);
            if (res.IsSuccessStatusCode) return null;

            var body = (await res.Content.ReadAsStringAsync()).Trim().Trim('"');
            return string.IsNullOrWhiteSpace(body) ? "שגיאה בשליחת הטופס" : body;
        }
        catch { return "שגיאת תקשורת"; }
    }
}
