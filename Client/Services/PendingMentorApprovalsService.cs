using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

// ─────────────────────────────────────────────────────────────────────────────
//  PendingMentorApprovalsService — drives the lecturer "משימות ממתינות לאישור
//  מנחה" inbox + reminder/override actions. Returns null on transport error
//  and a non-null error string ("שגיאה …") on action failures so the page can
//  surface the server's Hebrew message verbatim.
// ─────────────────────────────────────────────────────────────────────────────

public interface IPendingMentorApprovalsService
{
    Task<List<PendingMentorApprovalRowDto>?> GetPendingAsync();
    Task<List<LecturerSubmissionOverviewRowDto>?> GetOverviewAsync();
    Task<string?>                            RemindMentorAsync(int submissionId);
    Task<string?>                            OverrideApproveAsync(int submissionId, string overrideReason);
}

public class PendingMentorApprovalsService : IPendingMentorApprovalsService
{
    private readonly HttpClient _http;
    public PendingMentorApprovalsService(HttpClient http) => _http = http;

    public async Task<List<PendingMentorApprovalRowDto>?> GetPendingAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<PendingMentorApprovalRowDto>>(
                "api/task-submissions/pending-mentor-approvals");
        }
        catch { return null; }
    }

    public async Task<List<LecturerSubmissionOverviewRowDto>?> GetOverviewAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<LecturerSubmissionOverviewRowDto>>(
                "api/task-submissions/lecturer-overview");
        }
        catch { return null; }
    }

    public async Task<string?> RemindMentorAsync(int submissionId)
    {
        try
        {
            var resp = await _http.PostAsync(
                $"api/task-submissions/{submissionId}/remind-mentor", null);
            if (resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(body)
                ? "שגיאה בשליחת התזכורת"
                : body.Trim('"');
        }
        catch { return "שגיאת תקשורת"; }
    }

    public async Task<string?> OverrideApproveAsync(int submissionId, string overrideReason)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync(
                $"api/task-submissions/{submissionId}/lecturer-override-approve",
                new LecturerOverrideApproveRequest { OverrideReason = overrideReason });
            if (resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(body)
                ? "שגיאה באישור החריג"
                : body.Trim('"');
        }
        catch { return "שגיאת תקשורת"; }
    }
}
