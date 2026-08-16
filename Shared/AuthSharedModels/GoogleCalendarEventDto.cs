using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ──────────────────────────────────────────────────────────────────────────────
//  Task -> Google Calendar scheduling.
//
//  A Motiva due date and a Google Calendar block are NOT the same thing. The due
//  date answers "when must this be finished"; the calendar block answers "when do
//  I plan to work on it". Nothing here is derived from DueDate — the user picks
//  the calendar date and times explicitly, and the two never move together.
//
//  The client speaks only in product concepts: a date, a start time, an end time.
//  GoogleEventId, CalendarId, access tokens and Google's raw responses stay on
//  the server and never appear in any of these types.
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Body for PUT /api/google-calendar/tasks/{taskId}/schedule.
/// Times are Israel wall-clock "HH:mm" — the time the user typed, not an instant.
/// </summary>
public class ScheduleTaskInCalendarRequest
{
    /// <summary>The calendar event's date. Independent of the task's due date.</summary>
    public DateTime CalendarDate { get; set; }

    public string StartTime { get; set; } = "";
    public string EndTime   { get; set; } = "";
}

/// <summary>What the UI needs to show a scheduled task. Carries no Google ids.</summary>
public class TaskCalendarScheduleDto
{
    public int      TaskId       { get; set; }

    /// <summary>
    /// True only once Google has confirmed the event exists. A link awaiting a
    /// retry after a Google failure is returned with this false, so the UI can
    /// prefill the form without claiming the event is in the calendar.
    /// </summary>
    public bool     IsScheduled  { get; set; }

    public DateTime CalendarDate { get; set; }
    public string   StartTime    { get; set; } = "";
    public string   EndTime      { get; set; } = "";
}

/// <summary>
/// Outcome codes shared by the schedule/unschedule endpoints. Strings rather
/// than an enum so the wire format stays readable and additive.
/// </summary>
public static class CalendarScheduleOutcomes
{
    /// <summary>Google confirmed the event. The only state that shows "ביומן Google".</summary>
    public const string Synced = "synced";

    /// <summary>Nothing changed — the event already matches what was asked for.</summary>
    public const string Unchanged = "unchanged";

    /// <summary>The user has no active Google Calendar grant.</summary>
    public const string NotConnected = "not_connected";

    /// <summary>Google rejected or did not answer. The Motiva task is unaffected.</summary>
    public const string GoogleFailed = "google_failed";

    /// <summary>The link was removed locally, but Google could not be reached to
    /// delete the event (typically because the grant was disconnected first).</summary>
    public const string RemovedLocallyOnly = "removed_locally_only";

    /// <summary>Removed from both Google and Motiva.</summary>
    public const string Removed = "removed";
}

/// <summary>Result of a schedule / unschedule call.</summary>
public class TaskCalendarScheduleResultDto
{
    public bool    Success { get; set; }

    /// <summary>One of <see cref="CalendarScheduleOutcomes"/>.</summary>
    public string  Outcome { get; set; } = "";

    /// <summary>The stored schedule after the call, when there is one.</summary>
    public TaskCalendarScheduleDto? Schedule { get; set; }
}
