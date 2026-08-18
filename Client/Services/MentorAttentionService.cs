using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

/// <summary>
/// Client access to GET /api/mentor/attention — the server's single answer to
/// "what currently requires this mentor's attention".
///
/// <para>Deliberately a thin fetch with no logic. Every judgement the mentor UI
/// used to make for itself — is this waiting on me, how many days has it waited,
/// is that long enough to escalate, what order should the queue be in — is made
/// once on the server and read here. A method that recomputed any of them would
/// reintroduce exactly the drift this endpoint removed.</para>
///
/// <para>Separate from IMentorWorkspaceService so a screen that needs only the
/// attention snapshot (בקשות) pays for one GET rather than four.</para>
/// </summary>
public interface IMentorAttentionService
{
    Task<MentorAttentionDto> GetAsync();
}

public class MentorAttentionService : IMentorAttentionService
{
    private readonly HttpClient _http;

    public MentorAttentionService(HttpClient http) => _http = http;

    /// <summary>Never throws and never returns null — an empty snapshot degrades
    /// one section of a page instead of blanking the screen, matching how every
    /// other mentor service handles a transport failure.</summary>
    public async Task<MentorAttentionDto> GetAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<MentorAttentionDto>("api/mentor/attention")
                   ?? new MentorAttentionDto();
        }
        catch { return new MentorAttentionDto(); }
    }
}
