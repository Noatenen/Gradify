using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IMilestonesService
{
    Task<MilestonesPageDto?> GetMilestonesAsync();
}

public class MilestonesService : IMilestonesService
{
    private readonly HttpClient _http;

    public MilestonesService(HttpClient http) => _http = http;

    public async Task<MilestonesPageDto?> GetMilestonesAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<MilestonesPageDto>("api/projects/my-milestones");
        }
        catch
        {
            return null;
        }
    }
}
