using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IRequestTypesService
{
    Task<List<RequestTypeDefinitionDto>?> GetAllAsync();
    Task<List<RequestTypeDefinitionDto>?> GetActiveAsync();
    Task<RequestTypeDefinitionDto?>       GetByIdAsync(int id);
    Task<(RequestTypeDefinitionDto? Item, string? Error)> CreateAsync(SaveRequestTypeRequest req);
    Task<(RequestTypeDefinitionDto? Item, string? Error)> UpdateAsync(int id, SaveRequestTypeRequest req);
    Task<string?>                         ToggleActiveAsync(int id, bool isActive);
    Task<string?>                         DeleteAsync(int id);
}

public class RequestTypesService : IRequestTypesService
{
    private readonly HttpClient _http;
    public RequestTypesService(HttpClient http) => _http = http;

    public async Task<List<RequestTypeDefinitionDto>?> GetAllAsync()
    {
        try { return await _http.GetFromJsonAsync<List<RequestTypeDefinitionDto>>("api/request-types"); }
        catch { return null; }
    }

    public async Task<List<RequestTypeDefinitionDto>?> GetActiveAsync()
    {
        try { return await _http.GetFromJsonAsync<List<RequestTypeDefinitionDto>>("api/request-types/active"); }
        catch { return null; }
    }

    public async Task<RequestTypeDefinitionDto?> GetByIdAsync(int id)
    {
        try { return await _http.GetFromJsonAsync<RequestTypeDefinitionDto>($"api/request-types/{id}"); }
        catch { return null; }
    }

    public async Task<(RequestTypeDefinitionDto? Item, string? Error)> CreateAsync(SaveRequestTypeRequest req)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/request-types", req);
            if (!resp.IsSuccessStatusCode)
            {
                string body = (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"') ?? "";
                return (null, string.IsNullOrEmpty(body) ? "שגיאה ביצירת סוג הבקשה" : body);
            }
            var item = await resp.Content.ReadFromJsonAsync<RequestTypeDefinitionDto>();
            return (item, null);
        }
        catch { return (null, "שגיאת תקשורת"); }
    }

    public async Task<(RequestTypeDefinitionDto? Item, string? Error)> UpdateAsync(int id, SaveRequestTypeRequest req)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"api/request-types/{id}", req);
            if (!resp.IsSuccessStatusCode)
            {
                string body = (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"') ?? "";
                return (null, string.IsNullOrEmpty(body) ? "שגיאה בעדכון סוג הבקשה" : body);
            }
            var item = await resp.Content.ReadFromJsonAsync<RequestTypeDefinitionDto>();
            return (item, null);
        }
        catch { return (null, "שגיאת תקשורת"); }
    }

    public async Task<string?> ToggleActiveAsync(int id, bool isActive)
    {
        try
        {
            var resp = await _http.PatchAsJsonAsync(
                $"api/request-types/{id}/active",
                new ToggleRequestTypeActiveRequest { IsActive = isActive });
            if (resp.IsSuccessStatusCode) return null;
            string body = (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"') ?? "";
            return string.IsNullOrEmpty(body) ? "שגיאה בעדכון מצב סוג הבקשה" : body;
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<string?> DeleteAsync(int id)
    {
        try
        {
            var resp = await _http.DeleteAsync($"api/request-types/{id}");
            if (resp.IsSuccessStatusCode) return null;
            string body = (await resp.Content.ReadAsStringAsync())?.Trim().Trim('"') ?? "";
            return string.IsNullOrEmpty(body) ? "לא ניתן למחוק סוג בקשה זה" : body;
        }
        catch { return "שגיאת תקשורת"; }
    }
}