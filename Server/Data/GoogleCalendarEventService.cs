using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Data;

/// <summary>
/// Creates, updates and deletes Google Calendar events for Motiva tasks.
///
/// The only place in the codebase that calls the Calendar events API. Controllers
/// hand it product concepts (a task, a date, two times) and get back an outcome;
/// they never see an access token, an event id or a Google response.
///
/// ── SCOPE OF THIS PHASE ──────────────────────────────────────────────────────
/// Scheduling is PERSONAL. A team task added to Google Calendar creates one event
/// on the acting user's own primary calendar. No attendees are invited and no
/// other team member's calendar is touched — shared invitations belong to the
/// meetings phase. The (UserId, TaskId) uniqueness below is what encodes that:
/// each user gets at most one event per task, and their rows are independent.
///
/// ── IDEMPOTENCY ──────────────────────────────────────────────────────────────
/// Duplicate events are prevented by two mechanisms working together, neither of
/// which is "the button was disabled":
///
///   1. A UNIQUE(UserId, TaskType, TaskId) row. It is the mutex: the row is
///      written BEFORE Google is called, so a second request for the same pair
///      finds it and patches rather than inserts.
///   2. A client-supplied Google event id. Google's events.insert accepts an
///      `id`, and returns 409 for one that already exists on the calendar. The
///      id is generated once, stored on the row, and REUSED on every retry — so
///      the classic failure (event created, response lost, user retries) comes
///      back as 409, which this service reads as "already there", not as a
///      reason to insert a second event.
///
/// A fresh id is minted only when scheduling starts from nothing. That matters
/// because Google reserves a deleted event's id for a while: reusing the id of a
/// removed event would make re-adding the task fail, so remove-then-re-add gets a
/// new id rather than the old one.
///
/// ── TIME ─────────────────────────────────────────────────────────────────────
/// Motiva has no per-user timezone model. Times are sent to Google as wall-clock
/// plus <see cref="IsraelTime.IanaZoneId"/>, never as a UTC instant, so "16:00"
/// means 16:00 to the user and Google resolves DST. ScheduledStart/ScheduledEnd
/// are stored in that same wall-clock form.
/// </summary>
public class GoogleCalendarEventService
{
    /// <summary>This phase writes to the connected user's primary calendar only.</summary>
    public const string PrimaryCalendarId = "primary";

    /// <summary>
    /// Discriminator on the link row — the thing that lets ONE service and ONE
    /// table serve more than one kind of Motiva work item.
    ///
    /// <para>It is not decoration: TeamTasks.Id and PersonalTasks.Id are
    /// independent autoincrement sequences, so team task 7 and personal task 7
    /// are different rows that happen to share a number. Every query in this
    /// file therefore filters on (UserId, TaskType, TaskId) — the same triple
    /// the UNIQUE index is built on — and no read, write or delete can reach
    /// across the two kinds.</para>
    /// </summary>
    public const string TeamTaskType = "TeamTask";

    /// <summary>A mentor's own personal task. Joined in without a schema change,
    /// exactly as the TaskType column was designed for.</summary>
    public const string PersonalTaskType = "PersonalTask";

    private const string EventsEndpoint =
        "https://www.googleapis.com/calendar/v3/calendars/primary/events";

    /// <summary>Marker that makes the origin of the event obvious in Google
    /// Calendar. No ids, no technical metadata — see the payload builder.</summary>
    private const string MotivaMarker = "נוצר מתוך Motiva";

    private const string StatePending = "pending";
    private const string StateSynced  = "synced";

    private readonly DbRepository                        _db;
    private readonly GoogleCalendarTokenService          _tokens;
    private readonly IHttpClientFactory                  _httpFactory;
    private readonly ILogger<GoogleCalendarEventService> _log;

    public GoogleCalendarEventService(
        DbRepository                        db,
        GoogleCalendarTokenService          tokens,
        IHttpClientFactory                  httpFactory,
        ILogger<GoogleCalendarEventService> log)
    {
        _db          = db;
        _tokens      = tokens;
        _httpFactory = httpFactory;
        _log         = log;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every calendar link the user owns, for the tasks list and the edit modal.
    /// A link still awaiting a retry is included with IsScheduled=false, so the
    /// modal can prefill the form without the list claiming the event exists.
    /// </summary>
    public Task<List<TaskCalendarScheduleDto>> GetSchedulesForUserAsync(int userId) =>
        GetSchedulesForUserAsync(userId, TeamTaskType);

    /// <summary>Same, for one kind of work item. Personal-task links are never
    /// mixed into the team-task list: the ids collide across the two tables, so
    /// a combined list keyed by TaskId would be ambiguous by construction.</summary>
    public async Task<List<TaskCalendarScheduleDto>> GetSchedulesForUserAsync(int userId, string taskType)
    {
        // A user who disconnected Google still has their link rows — the Google
        // events themselves survive a revoke, so the rows are worth keeping for a
        // reconnect. But nothing may claim a LIVE calendar connection while the
        // grant is gone, so the list reads empty until they reconnect. This is a
        // DB read on GoogleCalendarConnections, not a Google call.
        var status = await _tokens.GetStatusAsync(userId);
        if (!status.IsConnected) return new List<TaskCalendarScheduleDto>();

        var rows = await _db.GetRecordsAsync<LinkRow>(@"
            SELECT Id, UserId, TaskId, TaskType, GoogleEventId, CalendarId,
                   ScheduledStart, ScheduledEnd, SyncState, IsActive
              FROM GoogleCalendarEventLinks
             WHERE UserId = @UserId AND TaskType = @TaskType AND IsActive = 1",
            new { UserId = userId, TaskType = taskType });

        return (rows ?? Enumerable.Empty<LinkRow>()).Select(ToDto).ToList();
    }

    public async Task<TaskCalendarScheduleDto?> GetScheduleAsync(
        int userId, int taskId, string taskType = TeamTaskType)
    {
        var row = await GetLinkAsync(userId, taskId, taskType);
        return row is null || row.IsActive != 1 ? null : ToDto(row);
    }

    // ── Schedule (create or update) ───────────────────────────────────────────

    /// <summary>
    /// Puts the task on the user's Google Calendar at the given wall-clock slot,
    /// or moves the existing event if there already is one. Safe to call
    /// repeatedly with the same values — see the idempotency notes on the class.
    /// </summary>
    public Task<TaskCalendarScheduleResultDto> ScheduleTeamTaskAsync(
        int userId, int taskId, string title, string? description,
        DateTime calendarDate, TimeSpan start, TimeSpan end) =>
        ScheduleAsync(userId, taskId, TeamTaskType, title, description, calendarDate, start, end);

    /// <summary>
    /// The generic form. Identical behaviour for every work-item kind — the only
    /// thing <paramref name="taskType"/> changes is which link row is claimed,
    /// which is what keeps one mentor's personal task and one student's team
    /// task from ever sharing an event.
    /// </summary>
    public async Task<TaskCalendarScheduleResultDto> ScheduleAsync(
        int userId, int taskId, string taskType, string title, string? description,
        DateTime calendarDate, TimeSpan start, TimeSpan end)
    {
        // The token check comes first so a disconnected user never leaves a link
        // row behind claiming a schedule that was never created.
        var token = await _tokens.GetValidAccessTokenAsync(userId);
        if (!token.IsConnected || token.AccessToken is null)
            return Fail(CalendarScheduleOutcomes.NotConnected);

        var existing = await GetLinkAsync(userId, taskId, taskType);
        var payload  = BuildEventPayload(title, description, calendarDate, start, end);

        // ── Already synced: patch in place, or do nothing at all ──────────────
        if (existing is { IsActive: 1, SyncState: StateSynced }
            && !string.IsNullOrEmpty(existing.GoogleEventId))
        {
            if (existing.ScheduledStart == StampOf(calendarDate, start)
                && existing.ScheduledEnd == StampOf(calendarDate, end))
            {
                // Same slot. The title/description could still have changed, so
                // patch anyway — but report "unchanged" so the UI stays quiet.
                await PatchEventAsync(token.AccessToken, existing.GoogleEventId, payload);
                return Ok(CalendarScheduleOutcomes.Unchanged, ToDto(existing));
            }

            var patched = await PatchEventAsync(token.AccessToken, existing.GoogleEventId, payload);

            if (patched == GoogleCallOutcome.Ok)
            {
                await UpsertLinkAsync(userId, taskId, taskType, existing.GoogleEventId,
                                      calendarDate, start, end, StateSynced);
                return Ok(CalendarScheduleOutcomes.Synced,
                          BuildDto(taskId, true, calendarDate, start, end));
            }

            // The event was deleted in Google directly. Fall through and create a
            // replacement rather than leaving the user stuck with a dead link.
            if (patched != GoogleCallOutcome.Gone)
                return Fail(CalendarScheduleOutcomes.GoogleFailed, ToDto(existing));
        }

        // ── Create ───────────────────────────────────────────────────────────
        // A pending row means a previous insert may or may not have reached
        // Google. Reusing its id is what turns that ambiguity into a 409 instead
        // of a duplicate event.
        var eventId = existing is { IsActive: 1, SyncState: StatePending }
                      && !string.IsNullOrEmpty(existing.GoogleEventId)
            ? existing.GoogleEventId
            : NewGoogleEventId();

        // Written BEFORE the call: if the process dies mid-insert, the retry finds
        // this row and reuses the id.
        await UpsertLinkAsync(userId, taskId, taskType, eventId, calendarDate, start, end, StatePending);

        var inserted = await InsertEventAsync(token.AccessToken, eventId, payload);

        if (inserted is GoogleCallOutcome.Ok or GoogleCallOutcome.AlreadyExists)
        {
            await MarkSyncedAsync(userId, taskId, taskType);

            if (inserted == GoogleCallOutcome.AlreadyExists)
                _log.LogInformation(
                    "Google already had the event for user {UserId} task {TaskId} — treated as success, no duplicate created.",
                    userId, taskId);

            return Ok(CalendarScheduleOutcomes.Synced,
                      BuildDto(taskId, true, calendarDate, start, end));
        }

        // Row stays active+pending with the same event id, so "ניסיון נוסף" is a
        // genuine retry of the same operation rather than a second event.
        return Fail(CalendarScheduleOutcomes.GoogleFailed,
                    BuildDto(taskId, false, calendarDate, start, end));
    }

    // ── Unschedule ────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes the event from Google and drops the local link. The Motiva task is
    /// never touched. An event Google says is already gone still gets cleaned up
    /// locally.
    /// </summary>
    public Task<TaskCalendarScheduleResultDto> UnscheduleTeamTaskAsync(int userId, int taskId) =>
        UnscheduleAsync(userId, taskId, TeamTaskType);

    /// <summary>The generic form. See <see cref="ScheduleAsync"/>.</summary>
    public async Task<TaskCalendarScheduleResultDto> UnscheduleAsync(
        int userId, int taskId, string taskType)
    {
        var existing = await GetLinkAsync(userId, taskId, taskType);
        if (existing is null || existing.IsActive != 1)
            return Ok(CalendarScheduleOutcomes.Removed);   // nothing to do

        var token = await _tokens.GetValidAccessTokenAsync(userId);

        if (!token.IsConnected || token.AccessToken is null)
        {
            // The grant is gone, so Motiva has no way to reach the event. Keeping
            // a link it can never manage is worse than dropping it; the caller is
            // told the difference so the UI can be honest about the leftover.
            await DeleteLinkAsync(userId, taskId, taskType);
            return Ok(CalendarScheduleOutcomes.RemovedLocallyOnly);
        }

        var deleted = await DeleteEventAsync(token.AccessToken, existing.GoogleEventId);

        // Gone == already deleted in Google. Clean up locally either way.
        if (deleted is GoogleCallOutcome.Ok or GoogleCallOutcome.Gone)
        {
            await DeleteLinkAsync(userId, taskId, taskType);
            return Ok(CalendarScheduleOutcomes.Removed);
        }

        // A transient failure keeps the link, so the user can try again and
        // actually get the event out of their calendar.
        return Fail(CalendarScheduleOutcomes.GoogleFailed, ToDto(existing));
    }

    // ── Task deletion ─────────────────────────────────────────────────────────

    /// <summary>
    /// Best-effort cleanup when a Motiva task is deleted: removes every user's
    /// event for it, then drops every link row.
    ///
    /// <para>Never throws and never reports failure. Task deletion is a Motiva
    /// operation and must not depend on Google being reachable — a leftover
    /// Google event is a far smaller problem than a task that refuses to delete.
    /// The link rows are dropped unconditionally, so no row survives pointing at
    /// a task id that no longer exists (and could later be reissued).</para>
    /// </summary>
    public async Task RemoveLinksForDeletedTaskAsync(int taskId, string taskType = TeamTaskType)
    {
        try
        {
            var rows = await _db.GetRecordsAsync<LinkRow>(@"
                SELECT Id, UserId, TaskId, TaskType, GoogleEventId, CalendarId,
                       ScheduledStart, ScheduledEnd, SyncState, IsActive
                  FROM GoogleCalendarEventLinks
                 WHERE TaskId = @TaskId AND TaskType = @TaskType",
                new { TaskId = taskId, TaskType = taskType });

            foreach (var row in rows ?? Enumerable.Empty<LinkRow>())
            {
                if (string.IsNullOrEmpty(row.GoogleEventId)) continue;

                var token = await _tokens.GetValidAccessTokenAsync(row.UserId);
                if (!token.IsConnected || token.AccessToken is null) continue;

                await DeleteEventAsync(token.AccessToken, row.GoogleEventId);
            }

            await _db.SaveDataAsync(
                "DELETE FROM GoogleCalendarEventLinks WHERE TaskId = @TaskId AND TaskType = @TaskType",
                new { TaskId = taskId, TaskType = taskType });
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                "Calendar cleanup for deleted task {TaskId} did not complete: {Message}. The task itself was deleted.",
                taskId, ex.Message);
        }
    }

    // ── Google HTTP ───────────────────────────────────────────────────────────

    private enum GoogleCallOutcome { Ok, AlreadyExists, Gone, Failed }

    private async Task<GoogleCallOutcome> InsertEventAsync(
        string accessToken, string eventId, GoogleEvent payload)
    {
        payload.Id = eventId;
        return await SendAsync(HttpMethod.Post, EventsEndpoint, accessToken, payload);
    }

    private async Task<GoogleCallOutcome> PatchEventAsync(
        string accessToken, string eventId, GoogleEvent payload)
    {
        // id is immutable and must not be echoed back on a patch.
        payload.Id = null;
        return await SendAsync(HttpMethod.Patch, EventUrl(eventId), accessToken, payload);
    }

    private Task<GoogleCallOutcome> DeleteEventAsync(string accessToken, string eventId) =>
        SendAsync(HttpMethod.Delete, EventUrl(eventId), accessToken, payload: null);

    private static string EventUrl(string eventId) =>
        $"{EventsEndpoint}/{Uri.EscapeDataString(eventId)}";

    private async Task<GoogleCallOutcome> SendAsync(
        HttpMethod method, string url, string accessToken, GoogleEvent? payload)
    {
        try
        {
            var http = _httpFactory.CreateClient(GoogleCalendarTokenService.HttpClientName);

            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            if (payload is not null)
                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request);

            if (response.IsSuccessStatusCode) return GoogleCallOutcome.Ok;

            return response.StatusCode switch
            {
                // events.insert with an id that already exists on the calendar.
                HttpStatusCode.Conflict                        => GoogleCallOutcome.AlreadyExists,
                // 404 = never existed here; 410 = deleted. Both mean "not there".
                HttpStatusCode.NotFound or HttpStatusCode.Gone => GoogleCallOutcome.Gone,
                _                                              => LogAndFail(method, response.StatusCode),
            };
        }
        catch (Exception ex)
        {
            // Transport only — the message describes connectivity, not the body,
            // and the request carried a bearer token that is never logged.
            _log.LogWarning("Google Calendar {Method} failed at transport level: {Message}",
                            method.Method, ex.Message);
            return GoogleCallOutcome.Failed;
        }
    }

    private GoogleCallOutcome LogAndFail(HttpMethod method, HttpStatusCode status)
    {
        // Status code only. The response body of a Calendar error can echo the
        // request, and the request carries the event payload.
        _log.LogWarning("Google Calendar {Method} returned {Status}.", method.Method, (int)status);
        return GoogleCallOutcome.Failed;
    }

    // ── Payload ───────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static GoogleEvent BuildEventPayload(
        string title, string? description, DateTime date, TimeSpan start, TimeSpan end) => new()
    {
        Summary     = title,
        Description = ComposeDescription(description),
        Start       = new GoogleEventTime(LocalIso(date, start), IsraelTime.IanaZoneId),
        End         = new GoogleEventTime(LocalIso(date, end),   IsraelTime.IanaZoneId),
    };

    /// <summary>
    /// The task's own description plus a one-line origin marker. Deliberately
    /// carries no task id, event id or link — the description is user-facing text
    /// in someone's personal calendar, not a place for internal identifiers.
    /// </summary>
    private static string ComposeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? MotivaMarker
            : $"{description.Trim()}\n\n{MotivaMarker}";

    /// <summary>
    /// Local ISO-8601 with NO offset. Paired with an explicit timeZone this is
    /// what tells Google "16:00 in Jerusalem" instead of "16:00 UTC", and lets
    /// Google apply DST for the date in question.
    /// </summary>
    private static string LocalIso(DateTime date, TimeSpan time) =>
        (date.Date + time).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    // ── Link rows ─────────────────────────────────────────────────────────────

    private async Task<LinkRow?> GetLinkAsync(int userId, int taskId, string taskType)
    {
        var rows = await _db.GetRecordsAsync<LinkRow>(@"
            SELECT Id, UserId, TaskId, TaskType, GoogleEventId, CalendarId,
                   ScheduledStart, ScheduledEnd, SyncState, IsActive
              FROM GoogleCalendarEventLinks
             WHERE UserId = @UserId AND TaskId = @TaskId AND TaskType = @TaskType",
            new { UserId = userId, TaskId = taskId, TaskType = taskType });

        return rows?.FirstOrDefault();
    }

    /// <summary>
    /// Claims the (UserId, TaskId) slot. The UNIQUE index makes this the single
    /// row for the pair, so two concurrent saves converge on one event instead of
    /// racing to insert two.
    /// </summary>
    private Task UpsertLinkAsync(
        int userId, int taskId, string taskType, string googleEventId,
        DateTime date, TimeSpan start, TimeSpan end, string syncState) =>
        _db.SaveDataAsync(@"
            INSERT INTO GoogleCalendarEventLinks
                (UserId, TaskId, TaskType, GoogleEventId, CalendarId,
                 ScheduledStart, ScheduledEnd, SyncState, CreatedAt, UpdatedAt, IsActive)
            VALUES
                (@UserId, @TaskId, @TaskType, @GoogleEventId, @CalendarId,
                 @ScheduledStart, @ScheduledEnd, @SyncState, datetime('now'), datetime('now'), 1)
            ON CONFLICT(UserId, TaskType, TaskId) DO UPDATE SET
                GoogleEventId  = excluded.GoogleEventId,
                CalendarId     = excluded.CalendarId,
                ScheduledStart = excluded.ScheduledStart,
                ScheduledEnd   = excluded.ScheduledEnd,
                SyncState      = excluded.SyncState,
                UpdatedAt      = datetime('now'),
                IsActive       = 1",
            new
            {
                UserId         = userId,
                TaskId         = taskId,
                TaskType       = taskType,
                GoogleEventId  = googleEventId,
                CalendarId     = PrimaryCalendarId,
                ScheduledStart = StampOf(date, start),
                ScheduledEnd   = StampOf(date, end),
                SyncState      = syncState,
            });

    private Task MarkSyncedAsync(int userId, int taskId, string taskType) =>
        _db.SaveDataAsync(@"
            UPDATE GoogleCalendarEventLinks
               SET SyncState = @State, UpdatedAt = datetime('now')
             WHERE UserId = @UserId AND TaskId = @TaskId AND TaskType = @TaskType",
            new { State = StateSynced, UserId = userId, TaskId = taskId, TaskType = taskType });

    private Task DeleteLinkAsync(int userId, int taskId, string taskType) =>
        _db.SaveDataAsync(
            "DELETE FROM GoogleCalendarEventLinks WHERE UserId = @UserId AND TaskId = @TaskId AND TaskType = @TaskType",
            new { UserId = userId, TaskId = taskId, TaskType = taskType });

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Israel wall-clock, the form stored in ScheduledStart/ScheduledEnd.</summary>
    private static string StampOf(DateTime date, TimeSpan time) =>
        (date.Date + time).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// A Google-legal event id: base32hex (0-9, a-v), 5–1024 chars, unique per
    /// calendar. 128 random bits, so two users scheduling at the same instant
    /// cannot collide. The "motiva" prefix is itself valid base32hex.
    /// </summary>
    private static string NewGoogleEventId() =>
        "motiva" + Base32Hex(RandomNumberGenerator.GetBytes(16));

    private const string Base32HexAlphabet = "0123456789abcdefghijklmnopqrstuv";

    private static string Base32Hex(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                sb.Append(Base32HexAlphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
            sb.Append(Base32HexAlphabet[(buffer << (5 - bitsLeft)) & 31]);

        return sb.ToString();
    }

    private static TaskCalendarScheduleDto ToDto(LinkRow row)
    {
        var (date, start) = SplitStamp(row.ScheduledStart);
        var (_, end)      = SplitStamp(row.ScheduledEnd);

        return new TaskCalendarScheduleDto
        {
            TaskId       = row.TaskId,
            IsScheduled  = row.SyncState == StateSynced,
            CalendarDate = date,
            StartTime    = start,
            EndTime      = end,
        };
    }

    private static TaskCalendarScheduleDto BuildDto(
        int taskId, bool isScheduled, DateTime date, TimeSpan start, TimeSpan end) => new()
    {
        TaskId       = taskId,
        IsScheduled  = isScheduled,
        CalendarDate = date.Date,
        StartTime    = FormatTime(start),
        EndTime      = FormatTime(end),
    };

    private static string FormatTime(TimeSpan t) =>
        $"{t.Hours:D2}:{t.Minutes:D2}";

    private static (DateTime Date, string Time) SplitStamp(string stamp) =>
        DateTime.TryParseExact(stamp, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
                               DateTimeStyles.None, out var parsed)
            ? (parsed.Date, parsed.ToString("HH:mm", CultureInfo.InvariantCulture))
            : (default, "");

    private static TaskCalendarScheduleResultDto Ok(
        string outcome, TaskCalendarScheduleDto? schedule = null) =>
        new() { Success = true, Outcome = outcome, Schedule = schedule };

    private static TaskCalendarScheduleResultDto Fail(
        string outcome, TaskCalendarScheduleDto? schedule = null) =>
        new() { Success = false, Outcome = outcome, Schedule = schedule };

    // ── Internal models ───────────────────────────────────────────────────────

    private sealed class LinkRow
    {
        public int    Id             { get; set; }
        public int    UserId         { get; set; }
        public int    TaskId         { get; set; }
        public string TaskType       { get; set; } = "";
        public string GoogleEventId  { get; set; } = "";
        public string CalendarId     { get; set; } = "";
        public string ScheduledStart { get; set; } = "";
        public string ScheduledEnd   { get; set; } = "";
        public string SyncState      { get; set; } = "";
        public int    IsActive       { get; set; }
    }

    private sealed class GoogleEvent
    {
        [JsonPropertyName("id")]          public string?          Id          { get; set; }
        [JsonPropertyName("summary")]     public string           Summary     { get; set; } = "";
        [JsonPropertyName("description")] public string?          Description { get; set; }
        [JsonPropertyName("start")]       public GoogleEventTime? Start       { get; set; }
        [JsonPropertyName("end")]         public GoogleEventTime? End         { get; set; }
    }

    private sealed class GoogleEventTime
    {
        public GoogleEventTime() { }

        public GoogleEventTime(string dateTime, string timeZone)
        {
            DateTime = dateTime;
            TimeZone = timeZone;
        }

        [JsonPropertyName("dateTime")] public string DateTime { get; set; } = "";
        [JsonPropertyName("timeZone")] public string TimeZone { get; set; } = "";
    }
}
