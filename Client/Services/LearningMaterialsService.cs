using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface ILearningMaterialsService
{
    /// <summary>
    /// Returns the learning materials relevant to the current student
    /// (project-specific, project-type-specific, or global).
    /// Returns null on network/server error.
    /// </summary>
    Task<List<LearningMaterialDto>?> GetMyMaterialsAsync();
}

public class LearningMaterialsService : ILearningMaterialsService
{
    private readonly HttpClient _http;

    public LearningMaterialsService(HttpClient http) => _http = http;

    public async Task<List<LearningMaterialDto>?> GetMyMaterialsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<LearningMaterialDto>>(
                "api/student/learning-materials");
        }
        catch { return null; }
    }
}
