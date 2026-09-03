using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IFormsManagementService
{
    Task<List<FormListItemDto>?> GetFormsAsync();
    Task<FormDetailDto?>         GetFormAsync(int id);
    Task<int?>                   CreateFormAsync(SaveFormRequest req);
    Task<bool>                   UpdateFormAsync(int id, SaveFormRequest req);
    Task<bool>                   DeleteFormAsync(int id);
    Task<bool>                   ToggleOpenAsync(int id);

    Task<int?>                   AddBlockAsync(int formId, SaveBlockRequest req);
    Task<bool>                   UpdateBlockAsync(int blockId, SaveBlockRequest req);
    Task<bool>                   DeleteBlockAsync(int blockId);

    Task<int?>                   AddOptionAsync(int blockId, SaveOptionRequest req);
    Task<bool>                   UpdateOptionAsync(int optionId, SaveOptionRequest req);
    Task<bool>                   DeleteOptionAsync(int optionId);

    /// <summary>
    /// Returns (newFormId, errorMessage). On success errorMessage is null.
    /// On HTTP 409 (target year already has a form of the same type) the
    /// server's Hebrew message is surfaced so the modal can show it directly.
    /// </summary>
    Task<(int? NewFormId, string? Error)> DuplicateFormAsync(int formId, DuplicateFormRequest req);

    /// <summary>
    /// Saves the whole question list in one request and returns the form as it
    /// now stands, so the editor rebinds to server truth (real Ids for newly
    /// added blocks, server-assigned SortOrder) instead of trusting its own
    /// optimistic copy. Error is null on success.
    /// </summary>
    Task<(FormDetailDto? Form, string? Error)> SaveStructureAsync(int formId, SaveFormStructureRequest req);

    Task<List<FormSubmissionListItemDto>?> GetSubmissionsAsync(int formId);
    Task<FormSubmissionDetailDto?>         GetSubmissionAsync(int submissionId);

    /// <summary>Create that surfaces the server's message (409 on a duplicate type).</summary>
    Task<(int? NewFormId, string? Error)> CreateFormDetailedAsync(SaveFormRequest req);

    /// <summary>Update that surfaces the server's validation message.</summary>
    Task<string?> UpdateFormDetailedAsync(int id, SaveFormRequest req);
}

public class FormsManagementService : IFormsManagementService
{
    private readonly HttpClient _http;

    public FormsManagementService(HttpClient http) => _http = http;

    public async Task<List<FormListItemDto>?> GetFormsAsync()
    {
        try { return await _http.GetFromJsonAsync<List<FormListItemDto>>("api/forms"); }
        catch { return null; }
    }

    public async Task<FormDetailDto?> GetFormAsync(int id)
    {
        try { return await _http.GetFromJsonAsync<FormDetailDto>($"api/forms/{id}"); }
        catch { return null; }
    }

    public async Task<int?> CreateFormAsync(SaveFormRequest req)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("api/forms", req);
            if (!res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadFromJsonAsync<IdResponse>();
            return body?.Id;
        }
        catch { return null; }
    }

    public async Task<bool> UpdateFormAsync(int id, SaveFormRequest req)
    {
        try
        {
            var res = await _http.PutAsJsonAsync($"api/forms/{id}", req);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteFormAsync(int id)
    {
        try
        {
            var res = await _http.DeleteAsync($"api/forms/{id}");
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> ToggleOpenAsync(int id)
    {
        try
        {
            var res = await _http.PostAsync($"api/forms/{id}/toggle-open", null);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<int?> AddBlockAsync(int formId, SaveBlockRequest req)
    {
        try
        {
            var res = await _http.PostAsJsonAsync($"api/forms/{formId}/blocks", req);
            if (!res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadFromJsonAsync<IdResponse>();
            return body?.Id;
        }
        catch { return null; }
    }

    public async Task<bool> UpdateBlockAsync(int blockId, SaveBlockRequest req)
    {
        try
        {
            var res = await _http.PutAsJsonAsync($"api/forms/blocks/{blockId}", req);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteBlockAsync(int blockId)
    {
        try
        {
            var res = await _http.DeleteAsync($"api/forms/blocks/{blockId}");
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<int?> AddOptionAsync(int blockId, SaveOptionRequest req)
    {
        try
        {
            var res = await _http.PostAsJsonAsync($"api/forms/blocks/{blockId}/options", req);
            if (!res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadFromJsonAsync<IdResponse>();
            return body?.Id;
        }
        catch { return null; }
    }

    public async Task<bool> UpdateOptionAsync(int optionId, SaveOptionRequest req)
    {
        try
        {
            var res = await _http.PutAsJsonAsync($"api/forms/options/{optionId}", req);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteOptionAsync(int optionId)
    {
        try
        {
            var res = await _http.DeleteAsync($"api/forms/options/{optionId}");
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<(int? NewFormId, string? Error)> DuplicateFormAsync(
        int formId, DuplicateFormRequest req)
    {
        try
        {
            var res = await _http.PostAsJsonAsync($"api/forms/{formId}/duplicate", req);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<DuplicateFormResponse>();
                return (body?.NewFormId, null);
            }
            string err = await res.Content.ReadAsStringAsync();
            return (null, string.IsNullOrWhiteSpace(err)
                              ? "שגיאה בשכפול הטופס"
                              : err.Trim('"'));
        }
        catch { return (null, "שגיאת תקשורת"); }
    }

    public async Task<(FormDetailDto? Form, string? Error)> SaveStructureAsync(
        int formId, SaveFormStructureRequest req)
    {
        try
        {
            var res = await _http.PutAsJsonAsync($"api/forms/{formId}/structure", req);
            if (res.IsSuccessStatusCode)
                return (await res.Content.ReadFromJsonAsync<FormDetailDto>(), null);

            return (null, await ReadErrorAsync(res, "שגיאה בשמירת השאלות"));
        }
        catch { return (null, "שגיאת תקשורת"); }
    }

    public async Task<List<FormSubmissionListItemDto>?> GetSubmissionsAsync(int formId)
    {
        try { return await _http.GetFromJsonAsync<List<FormSubmissionListItemDto>>($"api/forms/{formId}/submissions"); }
        catch { return null; }
    }

    public async Task<FormSubmissionDetailDto?> GetSubmissionAsync(int submissionId)
    {
        try { return await _http.GetFromJsonAsync<FormSubmissionDetailDto>($"api/forms/submissions/{submissionId}"); }
        catch { return null; }
    }

    public async Task<(int? NewFormId, string? Error)> CreateFormDetailedAsync(SaveFormRequest req)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("api/forms", req);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadFromJsonAsync<IdResponse>();
                return (body?.Id, null);
            }
            return (null, await ReadErrorAsync(res, "שגיאה ביצירת הטופס"));
        }
        catch { return (null, "שגיאת תקשורת"); }
    }

    public async Task<string?> UpdateFormDetailedAsync(int id, SaveFormRequest req)
    {
        try
        {
            var res = await _http.PutAsJsonAsync($"api/forms/{id}", req);
            return res.IsSuccessStatusCode ? null : await ReadErrorAsync(res, "שגיאה בשמירת ההגדרות");
        }
        catch { return "שגיאת תקשורת"; }
    }

    /// <summary>
    /// The API returns its errors as a bare JSON string, so the quotes have to
    /// come off before the message reaches a toast.
    /// </summary>
    private static async Task<string> ReadErrorAsync(HttpResponseMessage res, string fallback)
    {
        try
        {
            var body = (await res.Content.ReadAsStringAsync()).Trim().Trim('"');
            return string.IsNullOrWhiteSpace(body) ? fallback : body;
        }
        catch { return fallback; }
    }

    private sealed class IdResponse { public int Id { get; set; } }
}
