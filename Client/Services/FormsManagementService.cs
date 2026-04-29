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

    private sealed class IdResponse { public int Id { get; set; } }
}
