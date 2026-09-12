using System.Net.Http.Json;
using AuthWithAdmin.Client.Pages.ProjectWorkspace;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

/// <summary>
/// The faculty's תוצרי הגשה catalog, from the server.
///
/// <para>This replaced <c>SubmissionDeliverablesCatalog.All</c>, a hardcoded
/// static list whose own header described it as placeholder content. The record
/// it materialises — <see cref="SubmissionDeliverable"/> — is unchanged, so the
/// three student components render exactly what they rendered before; only the
/// source moved from a C# literal to a managed table.</para>
///
/// <para><b>Cached for the lifetime of the session.</b> The catalog is global
/// course content, it is read by two components on the same page, and it does
/// not change while a student is looking at it. <see cref="Invalidate"/> exists
/// for the management page, which is the one place that edits it.</para>
/// </summary>
public interface IDeliverablesService
{
    /// <summary>Active deliverables in display order — the student catalog.</summary>
    Task<IReadOnlyList<SubmissionDeliverable>> GetCatalogAsync();

    /// <summary>Every deliverable including deactivated ones. Admin/Staff.</summary>
    Task<List<DeliverableAdminDto>> GetForManagementAsync();

    Task<bool> CreateAsync(SaveDeliverableRequest req);
    Task<bool> UpdateAsync(int id, SaveDeliverableRequest req);
    Task<bool> SetActiveAsync(int id, bool value);
    Task<bool> ReorderAsync(IEnumerable<int> orderedIds);
    Task<bool> DeleteAsync(int id);

    /// <summary>Drops the cached student catalog after an edit.</summary>
    void Invalidate();
}

public class DeliverablesService : IDeliverablesService
{
    private readonly HttpClient _http;
    private IReadOnlyList<SubmissionDeliverable>? _cache;

    public DeliverablesService(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<SubmissionDeliverable>> GetCatalogAsync()
    {
        if (_cache is not null) return _cache;

        try
        {
            var dtos = await _http.GetFromJsonAsync<List<DeliverableAdminDto>>("api/deliverables")
                       ?? new();

            // ResourceTitles stays empty: it is the catalog's hook for Knowledge
            // Center documents, no ResourceFiles row reliably maps to a
            // graduation deliverable today, and wiring it is explicitly out of
            // scope. The section omits the documents block when it is empty,
            // which is exactly what it does now.
            _cache = dtos.Select(d => new SubmissionDeliverable(
                Key:               d.Key,
                Title:             d.Title,
                IconPath:          d.IconPath,
                Intro:             d.Intro,
                RequirementsLabel: d.RequirementsLabel,
                Requirements:      d.Requirements,
                Notes:             d.Notes,
                ResourceTitles:    Array.Empty<string>())).ToList();

            return _cache;
        }
        catch
        {
            // Never null and never throws: an empty catalog renders the section
            // with nothing in it rather than breaking the whole workspace page.
            return Array.Empty<SubmissionDeliverable>();
        }
    }

    public async Task<List<DeliverableAdminDto>> GetForManagementAsync()
    {
        try { return await _http.GetFromJsonAsync<List<DeliverableAdminDto>>("api/deliverables/manage") ?? new(); }
        catch { return new(); }
    }

    public async Task<bool> CreateAsync(SaveDeliverableRequest req) =>
        await SendAsync(() => _http.PostAsJsonAsync("api/deliverables", req));

    public async Task<bool> UpdateAsync(int id, SaveDeliverableRequest req) =>
        await SendAsync(() => _http.PutAsJsonAsync($"api/deliverables/{id}", req));

    public async Task<bool> SetActiveAsync(int id, bool value) =>
        await SendAsync(() => _http.PatchAsync($"api/deliverables/{id}/active?value={value}", null));

    public async Task<bool> ReorderAsync(IEnumerable<int> orderedIds) =>
        await SendAsync(() => _http.PutAsJsonAsync("api/deliverables/order",
            new ReorderDeliverablesRequest { OrderedIds = orderedIds.ToList() }));

    public async Task<bool> DeleteAsync(int id) =>
        await SendAsync(() => _http.DeleteAsync($"api/deliverables/{id}"));

    private async Task<bool> SendAsync(Func<Task<HttpResponseMessage>> call)
    {
        try
        {
            var resp = await call();
            if (resp.IsSuccessStatusCode) Invalidate();
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public void Invalidate() => _cache = null;
}
