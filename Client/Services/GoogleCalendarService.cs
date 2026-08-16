using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

/// <summary>
/// Google Calendar connection state for the settings screens.
///
/// Deliberately thin and stateless: connection state is ALWAYS read back from
/// GET /api/google-calendar/status, never inferred locally from "the user just
/// clicked חיבור". The browser leaves the app entirely during consent, so the
/// only thing that knows whether a grant exists is the server row.
/// </summary>
public interface IGoogleCalendarService
{
    Task<GoogleCalendarStatusDto?> GetStatusAsync();

    /// <summary>Google authorization URL to navigate to, or null when the
    /// integration is unconfigured or the request failed.</summary>
    Task<string?> GetConnectUrlAsync();

    Task<bool> DisconnectAsync();

    // ── Task scheduling ───────────────────────────────────────────────────────
    // The client sends product concepts only — a date and two times. It never
    // sees a Google event id, a calendar id or a token.

    /// <summary>Every calendar link the signed-in user owns, keyed by task id.</summary>
    Task<List<TaskCalendarScheduleDto>> GetTaskSchedulesAsync();

    /// <summary>
    /// Adds the task to the user's Google Calendar, or moves an existing event.
    /// Idempotent — the server will not create a second event for the same task.
    /// Returns null only when the call itself could not be made.
    /// </summary>
    Task<TaskCalendarScheduleResultDto?> ScheduleTaskAsync(int taskId, ScheduleTaskInCalendarRequest req);

    /// <summary>Removes the Google event and the link. The Motiva task is untouched.</summary>
    Task<TaskCalendarScheduleResultDto?> UnscheduleTaskAsync(int taskId);
}

public class GoogleCalendarService : IGoogleCalendarService
{
    private readonly HttpClient _http;

    public GoogleCalendarService(HttpClient http) => _http = http;

    public async Task<GoogleCalendarStatusDto?> GetStatusAsync()
    {
        try { return await _http.GetFromJsonAsync<GoogleCalendarStatusDto>("api/google-calendar/status"); }
        catch { return null; }
    }

    public async Task<string?> GetConnectUrlAsync()
    {
        try
        {
            var result = await _http.GetFromJsonAsync<GoogleCalendarConnectUrlDto>(
                "api/google-calendar/connect-url");

            return string.IsNullOrWhiteSpace(result?.Url) ? null : result!.Url;
        }
        catch { return null; }   // includes the 503 the server returns when unconfigured
    }

    public async Task<bool> DisconnectAsync()
    {
        try
        {
            var res = await _http.DeleteAsync("api/google-calendar/disconnect");
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Task scheduling ───────────────────────────────────────────────────────

    public async Task<List<TaskCalendarScheduleDto>> GetTaskSchedulesAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<TaskCalendarScheduleDto>>(
                       "api/google-calendar/tasks/schedules")
                   ?? new List<TaskCalendarScheduleDto>();
        }
        catch { return new List<TaskCalendarScheduleDto>(); }
    }

    public async Task<TaskCalendarScheduleResultDto?> ScheduleTaskAsync(
        int taskId, ScheduleTaskInCalendarRequest req)
    {
        try
        {
            var res = await _http.PutAsJsonAsync(
                $"api/google-calendar/tasks/{taskId}/schedule", req);

            // A Google failure comes back as 200 with Success=false — it is a
            // product outcome, not a transport error. Only a real HTTP failure
            // (validation, auth, unreachable) lands here.
            if (!res.IsSuccessStatusCode) return null;

            return await res.Content.ReadFromJsonAsync<TaskCalendarScheduleResultDto>();
        }
        catch { return null; }
    }

    public async Task<TaskCalendarScheduleResultDto?> UnscheduleTaskAsync(int taskId)
    {
        try
        {
            var res = await _http.DeleteAsync($"api/google-calendar/tasks/{taskId}/schedule");
            if (!res.IsSuccessStatusCode) return null;

            return await res.Content.ReadFromJsonAsync<TaskCalendarScheduleResultDto>();
        }
        catch { return null; }
    }
}
