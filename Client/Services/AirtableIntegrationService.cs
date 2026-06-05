using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IAirtableIntegrationService
{
    Task<List<AirtableIntegrationListItemDto>?> GetAllAsync();
    Task<AirtableIntegrationDetailDto?>         GetAsync(int id);
    Task<int?>                                  CreateAsync(SaveAirtableIntegrationRequest req);
    Task<bool>                                  UpdateAsync(int id, SaveAirtableIntegrationRequest req);
    Task<AirtableTestResultDto?>                TestAsync(int id);
    Task<AirtablePreviewResultDto?>             PreviewAsync(int id);
    /// <summary>Runs the actual import. <paramref name="skipRecordIds"/>
    /// is the admin's per-row opt-out list from the preview UI; null or
    /// empty means "import everything fetched".</summary>
    Task<AirtableSyncResultDto?>                ImportAsync(int id, List<string>? skipRecordIds = null);
    Task<List<AirtableFieldMappingDto>?>        GetMappingsAsync(int id);
    Task<bool>                                  SaveMappingsAsync(int id, List<AirtableFieldMappingDto> mappings);
    Task<List<AirtableImportRunDto>?>           GetImportRunsAsync(int id);
}

public class AirtableIntegrationService : IAirtableIntegrationService
{
    private readonly HttpClient _http;

    public AirtableIntegrationService(HttpClient http) => _http = http;

    public async Task<List<AirtableIntegrationListItemDto>?> GetAllAsync()
    {
        try { return await _http.GetFromJsonAsync<List<AirtableIntegrationListItemDto>>("api/integrations/airtable"); }
        catch { return null; }
    }

    public async Task<AirtableIntegrationDetailDto?> GetAsync(int id)
    {
        try { return await _http.GetFromJsonAsync<AirtableIntegrationDetailDto>($"api/integrations/airtable/{id}"); }
        catch { return null; }
    }

    public async Task<int?> CreateAsync(SaveAirtableIntegrationRequest req)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("api/integrations/airtable", req);
            if (!res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadFromJsonAsync<IdResponse>();
            return body?.Id;
        }
        catch { return null; }
    }

    public async Task<bool> UpdateAsync(int id, SaveAirtableIntegrationRequest req)
    {
        try
        {
            var res = await _http.PutAsJsonAsync($"api/integrations/airtable/{id}", req);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<AirtableTestResultDto?> TestAsync(int id)
    {
        try
        {
            var res = await _http.PostAsync($"api/integrations/airtable/{id}/test", null);
            return await res.Content.ReadFromJsonAsync<AirtableTestResultDto>();
        }
        catch { return null; }
    }

    public async Task<AirtablePreviewResultDto?> PreviewAsync(int id)
    {
        try
        {
            var res = await _http.PostAsync($"api/integrations/airtable/{id}/preview", null);
            if (!res.IsSuccessStatusCode)
            {
                string body = await res.Content.ReadAsStringAsync();
                return new AirtablePreviewResultDto { PreviewError = body.Trim('"') };
            }
            return await res.Content.ReadFromJsonAsync<AirtablePreviewResultDto>();
        }
        catch { return null; }
    }

    public async Task<AirtableSyncResultDto?> ImportAsync(int id, List<string>? skipRecordIds = null)
    {
        try
        {
            // Always POST a body (even an empty SkipRecordIds list) so the
            // server-side model binder is happy and the audit row records
            // an explicit skip-count of zero.
            var body = new AirtableImportRequest
            {
                SkipRecordIds = skipRecordIds ?? new()
            };
            var res = await _http.PostAsJsonAsync($"api/integrations/airtable/{id}/import", body);
            return await res.Content.ReadFromJsonAsync<AirtableSyncResultDto>();
        }
        catch { return null; }
    }

    public async Task<List<AirtableImportRunDto>?> GetImportRunsAsync(int id)
    {
        try { return await _http.GetFromJsonAsync<List<AirtableImportRunDto>>($"api/integrations/airtable/{id}/import-runs"); }
        catch { return null; }
    }

    public async Task<List<AirtableFieldMappingDto>?> GetMappingsAsync(int id)
    {
        try { return await _http.GetFromJsonAsync<List<AirtableFieldMappingDto>>($"api/integrations/airtable/{id}/mappings"); }
        catch { return null; }
    }

    public async Task<bool> SaveMappingsAsync(int id, List<AirtableFieldMappingDto> mappings)
    {
        try
        {
            var res = await _http.PutAsJsonAsync(
                $"api/integrations/airtable/{id}/mappings",
                new SaveAirtableMappingsRequest { Mappings = mappings });
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private sealed class IdResponse { public int Id { get; set; } }
}
