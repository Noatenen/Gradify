using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

/// <summary>
/// Putting a mentor's own personal task on their own Google Calendar.
///
/// The sibling of <see cref="GoogleCalendarTasksController"/>, and deliberately a
/// separate controller rather than three more routes on that one: its base route
/// is <c>api/google-calendar/tasks</c>, and widening it to
/// <c>api/google-calendar</c> to fit personal tasks in would re-address every
/// endpoint the student flow already depends on. A second thin controller costs
/// nothing and touches nothing.
///
/// ── THE TASK IS THE SOURCE OF TRUTH ──────────────────────────────────────────
/// The write endpoint takes NO BODY. Date, times, title and description are read
/// from the PersonalTasks row the caller just saved, so there is no second date
/// to keep in sync and no way for the calendar to disagree with the task. Editing
/// the task and re-calling this endpoint is what MOVES the existing event —
/// same link row, same Google event id, patched in place.
///
/// That inverts the team-task contract, where the times are calendar-only and
/// arrive in the request. A mentor personal task carries StartTime/EndTime as
/// real columns because Motiva's own hour grid renders them, so asking the client
/// to re-send them would create a second place they could be wrong.
///
/// ── OWNERSHIP ────────────────────────────────────────────────────────────────
/// Every route loads the task with <c>AND UserId = @UserId</c> before doing
/// anything, exactly as the personal-task endpoints themselves do. Another
/// mentor's task id answers 404 and never reaches the event service — so no one
/// can create, move or delete a Google event against a task that is not theirs,
/// and the link rows stay user-scoped by construction.
/// </summary>
[Route("api/google-calendar/personal-tasks")]
[ApiController]
[Authorize]
[ServiceFilter(typeof(AuthCheck))]
public class GoogleCalendarPersonalTasksController : ControllerBase
{
    private readonly DbRepository               _db;
    private readonly GoogleCalendarEventService _events;

    public GoogleCalendarPersonalTasksController(DbRepository db, GoogleCalendarEventService events)
    {
        _db     = db;
        _events = events;
    }

    // ── GET /api/google-calendar/personal-tasks/schedules ─────────────────────
    // Every personal-task calendar link the caller owns. Kept apart from the
    // team-task list because PersonalTasks.Id and TeamTasks.Id are independent
    // sequences — one combined list keyed by TaskId would be ambiguous.
    [HttpGet("schedules")]
    public async Task<IActionResult> GetMySchedules(int authUserId)
        => Ok(await _events.GetSchedulesForUserAsync(
                  authUserId, GoogleCalendarEventService.PersonalTaskType));

    // ── PUT /api/google-calendar/personal-tasks/{taskId}/schedule ─────────────
    // Creates the Google event, or moves/retitles the existing one. Idempotent:
    // calling it twice never produces a second event.
    [HttpPut("{taskId:int}/schedule")]
    public async Task<IActionResult> Schedule(int taskId, int authUserId)
    {
        var task = await GetOwnTaskAsync(taskId, authUserId);
        if (task is null) return NotFound("המשימה לא נמצאה");

        // The task must already be schedulable. These are the same three rules
        // the save endpoint enforces, re-checked because the task could have been
        // edited (or these fields cleared) between the two calls.
        if (!DateTime.TryParse(task.DueDate, out var dueDate))
            return BadRequest("יש לבחור תאריך כדי להוסיף את המשימה ליומן Google");

        if (!TryParseWallClock(task.StartTime, out var start) ||
            !TryParseWallClock(task.EndTime,   out var end))
            return BadRequest("יש להזין שעת התחלה ושעת סיום כדי להוסיף את המשימה ליומן Google");

        if (end <= start)
            return BadRequest("שעת הסיום חייבת להיות אחרי שעת ההתחלה");

        var result = await _events.ScheduleAsync(
            authUserId, taskId, GoogleCalendarEventService.PersonalTaskType,
            task.Title, task.Description, dueDate.Date, start, end);

        // 200 even for a Google failure: "saved, but not added" is a product
        // state the client renders, not a transport error. The Motiva task is
        // already committed by the time this endpoint is called at all.
        return Ok(result);
    }

    // ── DELETE /api/google-calendar/personal-tasks/{taskId}/schedule ──────────
    // Removes the Google event and the link. The Motiva task is untouched.
    [HttpDelete("{taskId:int}/schedule")]
    public async Task<IActionResult> Unschedule(int taskId, int authUserId)
    {
        var task = await GetOwnTaskAsync(taskId, authUserId);
        if (task is null) return NotFound("המשימה לא נמצאה");

        return Ok(await _events.UnscheduleAsync(
                      authUserId, taskId, GoogleCalendarEventService.PersonalTaskType));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the task only if it belongs to the caller. Ownership is structural
    /// — the WHERE clause carries it, so a task that is not theirs matches zero
    /// rows and the request never learns whether the id exists.
    /// </summary>
    private async Task<PersonalTaskRow?> GetOwnTaskAsync(int taskId, int userId)
    {
        var rows = await _db.GetRecordsAsync<PersonalTaskRow>(@"
            SELECT Id, Title, Description, DueDate, StartTime, EndTime
              FROM PersonalTasks
             WHERE Id     = @TaskId
               AND UserId = @UserId
             LIMIT 1",
            new { TaskId = taskId, UserId = userId });

        return rows?.FirstOrDefault();
    }

    /// <summary>"HH:mm" as stored; "HH:mm:ss" tolerated so a legacy or
    /// non-browser-written value is not rejected.</summary>
    private static bool TryParseWallClock(string? value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        return TimeSpan.TryParseExact(value.Trim(), new[] { @"hh\:mm", @"hh\:mm\:ss" },
                                      System.Globalization.CultureInfo.InvariantCulture, out time);
    }

    private sealed class PersonalTaskRow
    {
        public int     Id          { get; set; }
        public string  Title       { get; set; } = "";
        public string? Description { get; set; }
        public string? DueDate     { get; set; }
        public string? StartTime   { get; set; }
        public string? EndTime     { get; set; }
    }
}
