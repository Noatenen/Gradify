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
            var dto = await _http.GetFromJsonAsync<MentorAttentionDto>("api/mentor/attention")
                      ?? new MentorAttentionDto();
            NormaliseHrefs(dto);
            return dto;
        }
        catch { return new MentorAttentionDto(); }
    }

    /// <summary>
    /// Rebases each item's <c>Href</c> from root-relative to base-relative.
    ///
    /// <para><b>Why the server cannot simply emit the base-relative form.</b>
    /// MentorAttentionService is shared with the daily digest, and
    /// MentorDigestComposer builds every email link as <c>root + Href</c> where
    /// root is <c>App:BaseUrl</c> with no trailing slash. That concatenation
    /// REQUIRES the leading slash. The browser requires its absence: a rooted
    /// path resolves against the origin and ignores <c>&lt;base href&gt;</c>, so
    /// under a sub-path deployment every one of these links would navigate out
    /// of the application.</para>
    ///
    /// <para>One producer, two consumers, opposite requirements — so the value
    /// stays rooted on the wire (the digest's shape) and is rebased here, at the
    /// single point where it enters the browser. This is the same split
    /// ReturnUrlPolicy uses: validate and transport rooted, navigate relative.
    /// Nothing about the ordering, counts or aging semantics is touched.</para>
    /// </summary>
    private static void NormaliseHrefs(MentorAttentionDto dto)
    {
        foreach (var item in dto.Items)
            if (!string.IsNullOrEmpty(item.Href))
                item.Href = item.Href.TrimStart('/');
    }
}
