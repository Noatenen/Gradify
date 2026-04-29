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
    Task<AirtableSyncResultDto?>                ImportAsync(int id);
    Task<List<AirtableFieldMappingDto>?>        GetMappingsAsync(int id);
    Task<bool>                                  SaveMappingsAsync(int id, List<AirtableFieldMappingDto> mappings);
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

    public async Task<AirtableSyncResultDto?> ImportAsync(int id)
    {
        try
        {
            var res = await _http.PostAsync($"api/integrations/airtable/{id}/import", null);
            return await res.Content.ReadFromJsonAsync<AirtableSyncResultDto>();
        }
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
