using System.Globalization;
using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AuthWithAdmin.Server.Controllers;

/// <summary>
/// Scheduling a Motiva team task onto the caller's own Google Calendar.
///
/// Separate from the task endpoints on purpose. Saving a task and putting it in a
/// calendar are two different operations with two different failure modes, and
/// keeping them apart is what makes the required behaviour fall out for free: the
/// client saves the task through the existing endpoint FIRST, then calls this
/// one. A failure here can therefore never cost the user their task — the task is
/// already committed by the time Google is contacted.
///
/// Thin by construction: it authorizes the task, parses two times, and hands
/// product concepts to GoogleCalendarEventService. No Google call, no token, no
/// event id appears in this file.
/// </summary>
[Route("api/google-calendar/tasks")]
[ApiController]
[Authorize]
[ServiceFilter(typeof(AuthCheck))]
public class GoogleCalendarTasksController : ControllerBase
{
    private readonly DbRepository               _db;
    private readonly GoogleCalendarEventService _events;
    private readonly GoogleCalendarOptions      _options;

    public GoogleCalendarTasksController(
        DbRepository db,
        GoogleCalendarEventService events,
        IOptions<GoogleCalendarOptions> options)
    {
        _db      = db;
        _events  = events;
        _options = options.Value;
    }

    /// <summary>503 + a product message when GoogleCalendar:Enabled is false.
    /// Returned by the two write endpoints, so scheduling cannot be driven from
    /// outside the UI while the integration is switched off.</summary>
    private IActionResult Unavailable() =>
        StatusCode(503, "האינטגרציה עם יומן Google אינה פעילה כרגע");

    // ── GET /api/google-calendar/tasks/schedules ──────────────────────────────
    // Every calendar link the caller owns. Personal by definition: a teammate's
    // scheduling of the same task is a different row and is never returned here.
    //
    // While the integration is disabled this answers an EMPTY list rather than
    // 503, and that asymmetry with the write endpoints is deliberate. Four
    // screens call it on every load purely to decorate rows with a "ביומן
    // Google" chip; an empty list is both true (there is nothing the user can
    // act on right now) and exactly what makes those chips disappear, whereas a
    // 503 would put four failed requests in the console of every page load to
    // achieve the same thing. The link rows themselves are untouched.
    [HttpGet("schedules")]
    public async Task<IActionResult> GetMySchedules(int authUserId)
        => _options.Enabled
            ? Ok(await _events.GetSchedulesForUserAsync(authUserId))
            : Ok(Array.Empty<TaskCalendarScheduleDto>());

    // ── PUT /api/google-calendar/tasks/{taskId}/schedule ──────────────────────
    // Creates the Google event, or moves the existing one. Idempotent — calling
    // it twice with the same values does not produce a second event.
    [HttpPut("{taskId:int}/schedule")]
    public async Task<IActionResult> Schedule(
        int taskId, [FromBody] ScheduleTaskInCalendarRequest req, int authUserId)
    {
        if (!_options.Enabled) return Unavailable();

        var task = await GetTeamTaskForUserAsync(taskId, authUserId);
        if (task is null) return NotFound("המשימה לא נמצאה");

        if (req.CalendarDate == default)
            return BadRequest("יש לבחור תאריך ליומן");

        if (!TryParseTime(req.StartTime, out var start) || !TryParseTime(req.EndTime, out var end))
            return BadRequest("יש להזין שעת התחלה ושעת סיום");

        // Server-side too, not only in the modal: the endpoint is reachable
        // without the UI, and an inverted range would become a Google event that
        // silently lasts until the previous day.
        if (end <= start)
            return BadRequest("שעת הסיום חייבת להיות אחרי שעת ההתחלה");

        var result = await _events.ScheduleTeamTaskAsync(
            authUserId, taskId, task.Title, task.Description,
            req.CalendarDate.Date, start, end);

        // 200 even for a Google failure: the outcome is a product state the
        // client has to render ("saved, but not added"), not a transport error.
        return Ok(result);
    }

    // ── DELETE /api/google-calendar/tasks/{taskId}/schedule ───────────────────
    // Removes the Google event and the local link. The Motiva task is untouched.
    [HttpDelete("{taskId:int}/schedule")]
    public async Task<IActionResult> Unschedule(int taskId, int authUserId)
    {
        if (!_options.Enabled) return Unavailable();

        // Authorized like the write path — a link is only ever removed by someone
        // who can see the task it belongs to.
        var task = await GetTeamTaskForUserAsync(taskId, authUserId);
        if (task is null) return NotFound("המשימה לא נמצאה");

        return Ok(await _events.UnscheduleTeamTaskAsync(authUserId, taskId));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the task only if the caller is an active member of its team — the
    /// same scoping rule the team-task endpoints use, so calendar access can
    /// never reach further than task access.
    /// </summary>
    private async Task<TeamTaskRow?> GetTeamTaskForUserAsync(int taskId, int userId)
    {
        var rows = await _db.GetRecordsAsync<TeamTaskRow>(@"
            SELECT tt.Id, tt.Title, tt.Description
              FROM TeamTasks   tt
              JOIN TeamMembers tm ON tm.TeamId = tt.TeamId
             WHERE tt.Id      = @TaskId
               AND tm.UserId  = @UserId
               AND tm.IsActive = 1
             LIMIT 1",
            new { TaskId = taskId, UserId = userId });

        return rows?.FirstOrDefault();
    }

    private static bool TryParseTime(string? value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        // "HH:mm" is what <input type="time"> submits; "HH:mm:ss" is accepted so a
        // non-browser caller is not tripped up by a seconds component.
        return TimeSpan.TryParseExact(value, new[] { @"hh\:mm", @"hh\:mm\:ss" },
                                      CultureInfo.InvariantCulture, out time);
    }

    private sealed class TeamTaskRow
    {
        public int     Id          { get; set; }
        public string  Title       { get; set; } = "";
        public string? Description { get; set; }
    }
}
