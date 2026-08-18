using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Data;

// ─────────────────────────────────────────────────────────────────────────────
//  MentorAttentionService — the single source of truth for
//  "what currently requires this mentor's attention".
//
//  EVERY mentor surface reads this and nothing else:
//      בית              MentorHomePage
//      המשימות שלי      MentorTasksPage
//      יומן ותכנון      MentorCalendarPage (review entries)
//      daily digest     MentorDigestComposer
//      Project Workspace (later — filter Items by ProjectId)
//
//  It takes a mentorUserId rather than reading HttpContext, because the digest
//  runs inside a BackgroundService with no request in flight. That constraint is
//  the reason the whole model lives on the server: a client-side definition
//  could not have been reused by the scheduler.
//
//  SCOPING
//  Strictly ProjectMentors — "projects where YOU are a mentor" — for every
//  caller, including Admin/Staff. This deliberately differs from
//  GET /api/mentor/submissions, which shows Admin/Staff everything: that
//  endpoint answers "what is pending across the programme", and this one
//  answers "what is waiting on me". An admin who mentors nothing correctly
//  gets an empty snapshot rather than the whole cohort's queue.
// ─────────────────────────────────────────────────────────────────────────────
public class MentorAttentionService
{
    private readonly DbRepository _db;

    public MentorAttentionService(DbRepository db) => _db = db;

    // ── Submissions ──────────────────────────────────────────────────────────
    //
    // MentorStatus = 'Pending' is the whole gate, and it is exact: a submission
    // round is its own TaskSubmissions row, so a resubmission after a return
    // creates a NEW row with a NEW SubmittedAt. Waiting age therefore never
    // inherits from a previous round.
    //
    // MILESTONE-LESS SUBMISSIONS — LEFT JOIN, NOT INNER JOIN.
    // Tasks.ProjectMilestoneId is nullable and 6 rows in the current database
    // already use it. The existing /api/mentor/submissions and the lecturer's
    // pending-mentor-approvals inbox both INNER JOIN through
    // ProjectMilestones → AcademicYearMilestones → MilestoneTemplates, so a
    // pending submission on a milestone-less task is invisible to both — it
    // would silently never reach the mentor OR the digest. A data check on the
    // current database found no such row today (0 of 2 pending submissions, and
    // no IsSubmission task with a NULL milestone), but the schema allows it, so
    // this query LEFT JOINs the whole chain and reports an empty milestone
    // title instead of dropping the row. That is the local fix; the submission
    // system itself is untouched.
    private const string SubmissionsSql = @"
        SELECT  ts.Id                                     AS SubmissionId,
                ts.TaskId,
                t.Title                                   AS TaskTitle,
                p.Id                                      AS ProjectId,
                p.Title                                   AS ProjectTitle,
                COALESCE(tm.TeamName, '')                 AS TeamName,
                COALESCE(mt.Title, '')                    AS MilestoneTitle,
                COALESCE(u.FirstName || ' ' || u.LastName, '') AS SubmittedBy,
                ts.SubmittedAt
        FROM    TaskSubmissions ts
        JOIN    Tasks           t  ON t.Id = ts.TaskId
        JOIN    Projects        p  ON p.Id = t.ProjectId
        JOIN    ProjectMentors  pm ON pm.ProjectId = p.Id AND pm.UserId = @MentorId
        LEFT JOIN Teams                  tm  ON tm.Id  = p.TeamId
        LEFT JOIN users                  u   ON u.Id   = ts.SubmittedByUserId
        LEFT JOIN ProjectMilestones      pms ON pms.Id = t.ProjectMilestoneId
        LEFT JOIN AcademicYearMilestones aym ON aym.Id = pms.AcademicYearMilestoneId
        LEFT JOIN MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
        WHERE   ts.MentorStatus = 'Pending'
        ORDER   BY ts.SubmittedAt ASC";

    // ── Requests ─────────────────────────────────────────────────────────────
    //
    // PendingMentorRecommendation is the ONLY status that waits on a mentor,
    // and that is a capability statement rather than a stylistic one: the
    // mentor's single write endpoint
    // (POST /api/project-requests/{id}/mentor-recommendation) is
    // [Authorize(Roles = Mentor)] and gated on exactly this status. /handle is
    // Admin+Staff and /reply requires team membership, so a mentor genuinely
    // cannot move anything else forward. Only Extension requests ever enter it.
    //
    // WAITING-SINCE ASSUMPTION — READ BEFORE CHANGING THE REQUEST FLOW.
    // WaitingSince is ProjectRequests.CreatedAt. That is correct ONLY because
    // the mentor stage is entered exactly once, at creation
    // (ProjectRequestsController.Create sets PendingMentorRecommendation for
    // RequestType = Extension), and the flow is one-directional:
    //     PendingMentorRecommendation → PendingLecturerDecision → terminal
    // Nothing sends a request BACK to the mentor stage.
    //
    // If that ever changes — a lecturer bouncing a request back for a second
    // recommendation, or a new request type entering the mentor stage after
    // creation — CreatedAt will over-state the age, reporting the whole life of
    // the request instead of the current wait. The upgrade is one nullable
    // column, ProjectRequests.MentorWaitingSince TEXT, stamped on every entry
    // into the status and backfilled from CreatedAt; this SELECT then reads
    // COALESCE(r.MentorWaitingSince, r.CreatedAt) and nothing else moves.
    // Reconstructing it from ProjectRequestEvents is NOT a viable fallback:
    // that table stores Hebrew display labels in OldValue/NewValue, not status
    // codes.
    private const string RequestsSql = @"
        SELECT  r.Id                                      AS RequestId,
                r.RequestType,
                r.Title,
                p.Id                                      AS ProjectId,
                p.Title                                   AS ProjectTitle,
                COALESCE(tm.TeamName, '')                 AS TeamName,
                COALESCE(u.FirstName || ' ' || u.LastName, '') AS CreatedByName,
                r.CreatedAt
        FROM    ProjectRequests r
        JOIN    Projects        p  ON p.Id = r.ProjectId
        JOIN    ProjectMentors  pm ON pm.ProjectId = p.Id AND pm.UserId = @MentorId
        LEFT JOIN Teams         tm ON tm.Id = p.TeamId
        LEFT JOIN users         u  ON u.Id  = r.CreatedByUserId
        WHERE   r.Status = @PendingStatus
        ORDER   BY r.CreatedAt ASC";

    // ── Personal tasks ───────────────────────────────────────────────────────
    //
    // Not "waiting" — these carry a real DueDate the mentor set themselves, and
    // are the ONLY kind in this model that may legitimately read as באיחור.
    // Undated tasks are excluded: with no date there is nothing to be due today
    // and nothing to be late for, so counting them in "what needs you today"
    // would inflate the headline with items the mentor never scheduled.
    private const string PersonalTasksSql = @"
        SELECT  Id, Title, Description, DueDate
        FROM    PersonalTasks
        WHERE   UserId = @MentorId
          AND   IsDone = 0
          AND   DueDate IS NOT NULL
        ORDER   BY DueDate ASC";

    /// <summary>
    /// Builds the mentor's complete attention snapshot.
    ///
    /// <para>Never throws and never returns null: DbRepository answers null on
    /// any Dapper failure, which is treated here as "no rows". A partial outage
    /// therefore empties one section rather than blanking a page or aborting a
    /// digest run.</para>
    /// </summary>
    public async Task<MentorAttentionDto> GetAsync(int mentorUserId)
    {
        // Sequential, NOT Task.WhenAll: DbRepository holds one SqliteConnection
        // per scoped instance and opens/closes it around each call, so
        // concurrent queries on the same instance race on connection state.
        var submissionRows = (await _db.GetRecordsAsync<SubmissionRow>(
            SubmissionsSql, new { MentorId = mentorUserId }))?.ToList() ?? new();

        var requestRows = (await _db.GetRecordsAsync<RequestRow>(
            RequestsSql, new
            {
                MentorId      = mentorUserId,
                PendingStatus = RequestStatuses.PendingMentorRecommendation,
            }))?.ToList() ?? new();

        var personalRows = (await _db.GetRecordsAsync<PersonalRow>(
            PersonalTasksSql, new { MentorId = mentorUserId }))?.ToList() ?? new();

        var items = new List<MentorAttentionItemDto>(
            submissionRows.Count + requestRows.Count + personalRows.Count);

        foreach (var s in submissionRows)
        {
            int days = IsraelTime.CalendarDaysSince(s.SubmittedAt);
            var age  = MentorAttention.AgeOf(days);
            items.Add(new MentorAttentionItemDto
            {
                Kind           = MentorAttentionKind.Submission,
                EntityType     = "TaskSubmission",
                EntityId       = s.SubmissionId,
                Title          = s.TaskTitle,
                ProjectId      = s.ProjectId,
                ProjectTitle   = s.ProjectTitle,
                TeamName       = NullIfBlank(s.TeamName),
                MilestoneTitle = NullIfBlank(s.MilestoneTitle),
                ActorName      = NullIfBlank(s.SubmittedBy),
                WaitingSince   = s.SubmittedAt,
                WaitingDays    = days,
                Age            = age,
                WaitingLabel   = MentorAging.WaitingLabel(MentorAttentionKind.Submission, days),
                // The safest destination that exists TODAY. It is also exactly
                // where the approved Home and Tasks rows already send a review,
                // so the digest cannot land somewhere the UI does not. Precise
                // per-submission deep-linking waits for Project Workspace.
                Href           = $"/mentor/projects/{s.ProjectId}",
            });
        }

        foreach (var r in requestRows)
        {
            int days = IsraelTime.CalendarDaysSince(r.CreatedAt);
            var age  = MentorAttention.AgeOf(days);
            items.Add(new MentorAttentionItemDto
            {
                Kind         = MentorAttentionKind.Request,
                EntityType   = "ProjectRequest",
                EntityId     = r.RequestId,
                Title        = r.Title,
                ProjectId    = r.ProjectId,
                ProjectTitle = r.ProjectTitle,
                TeamName     = NullIfBlank(r.TeamName),
                ActorName    = NullIfBlank(r.CreatedByName),
                RequestType  = r.RequestType,
                WaitingSince = r.CreatedAt,
                WaitingDays  = days,
                Age          = age,
                WaitingLabel = MentorAging.WaitingLabel(MentorAttentionKind.Request, days),
                // The existing request deep link — MentorRequestsPage already
                // reads ?requestId= and expands that row.
                Href         = $"/mentor-requests?requestId={r.RequestId}",
            });
        }

        var today = IsraelTime.Today;
        foreach (var t in personalRows)
        {
            // Date-only comparison, no timezone conversion: a due date is a date
            // the mentor picked, not an instant (see IsraelTime.DaysUntilDate).
            int daysUntil = IsraelTime.DaysUntilDate(t.DueDate!.Value);
            if (daysUntil > 0) continue;   // not yet due — not today's business

            items.Add(new MentorAttentionItemDto
            {
                Kind        = MentorAttentionKind.PersonalTask,
                EntityType  = "PersonalTask",
                EntityId    = t.Id,
                Title       = t.Title,
                // A personal task is dated, not waiting. WaitingSince stays null
                // and WaitingDays stays 0 so nothing downstream can accidentally
                // describe it with waiting-age wording.
                WaitingDays = 0,
                Age         = MentorAttentionAge.New,
                DueDate     = t.DueDate,
                IsOverdue   = daysUntil < 0,
                Href        = "/mentor/tasks?focus=personal",
            });
        }

        // ── Canonical ordering ───────────────────────────────────────────────
        // Worst first: NeedsAttention, then Waiting, then New — and oldest first
        // inside each band. An overdue personal task ranks with NeedsAttention
        // and one due today ranks with Waiting, so the single Items list stays
        // meaningfully sorted across kinds; filtering by Kind preserves the
        // order, which is why no page has to sort.
        items = items
            .OrderBy(i => i.Kind == MentorAttentionKind.PersonalTask
                              ? (i.IsOverdue ? 0 : 1)
                              : MentorAttention.Severity(i.Age))
            .ThenByDescending(i => i.WaitingDays)
            .ThenBy(i => i.WaitingSince ?? i.DueDate ?? DateTime.MaxValue)
            .ThenBy(i => i.EntityId)
            .ToList();

        // ── Counts — derived from the SAME list, never recounted downstream ──
        int subs        = items.Count(i => i.Kind == MentorAttentionKind.Submission);
        int subsToday   = items.Count(i => i.Kind == MentorAttentionKind.Submission
                                        && i.Age  == MentorAttentionAge.New);
        int reqs        = items.Count(i => i.Kind == MentorAttentionKind.Request);
        int reqsToday   = items.Count(i => i.Kind == MentorAttentionKind.Request
                                        && i.Age  == MentorAttentionAge.New);
        int personal    = items.Count(i => i.Kind == MentorAttentionKind.PersonalTask);
        int personalLate= items.Count(i => i.Kind == MentorAttentionKind.PersonalTask && i.IsOverdue);

        // NeedsAttention counts WAITING items only. A personal task is late
        // against a real deadline, which is a different idea with its own count
        // — folding the two together would let a deadline miss masquerade as a
        // waiting age, and vice versa.
        int needsAttention = items.Count(i => i.Kind != MentorAttentionKind.PersonalTask
                                           && i.Age  == MentorAttentionAge.NeedsAttention);

        return new MentorAttentionDto
        {
            Items         = items,
            AsOfLocalDate = today,
            Counts = new MentorAttentionCountsDto
            {
                PendingSubmissions    = subs,
                SubmissionsNewToday   = subsToday,
                AwaitingRequests      = reqs,
                RequestsNewToday      = reqsToday,
                PersonalTasksDueToday = personal,
                PersonalTasksOverdue  = personalLate,
                NeedsAttention        = needsAttention,
                Total                 = subs + reqs + personal,
            },
        };
    }

    /// <summary>Every active user who mentors at least one project. The digest
    /// audience — computed from ProjectMentors rather than from the Mentor role,
    /// so a mentor with no assignments is never emailed about an empty
    /// caseload.</summary>
    public async Task<IReadOnlyList<int>> GetActiveMentorIdsAsync()
    {
        const string sql = @"
            SELECT DISTINCT pm.UserId
            FROM   ProjectMentors pm
            JOIN   users          u ON u.Id = pm.UserId
            WHERE  u.IsActive = 1
            ORDER  BY pm.UserId";

        return (await _db.GetRecordsAsync<int>(sql))?.ToList() ?? new List<int>();
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;

    // ── Dapper row types ─────────────────────────────────────────────────────
    // Classes with public setters, not positional records: DbRepository
    // swallows any Dapper mapping failure and returns null, which surfaces
    // downstream as an unexplained empty section.

    private sealed class SubmissionRow
    {
        public int      SubmissionId   { get; set; }
        public int      TaskId         { get; set; }
        public string   TaskTitle      { get; set; } = "";
        public int      ProjectId      { get; set; }
        public string   ProjectTitle   { get; set; } = "";
        public string   TeamName       { get; set; } = "";
        public string   MilestoneTitle { get; set; } = "";
        public string   SubmittedBy    { get; set; } = "";
        public DateTime SubmittedAt    { get; set; }
    }

    private sealed class RequestRow
    {
        public int      RequestId     { get; set; }
        public string   RequestType   { get; set; } = "";
        public string   Title         { get; set; } = "";
        public int      ProjectId     { get; set; }
        public string   ProjectTitle  { get; set; } = "";
        public string   TeamName      { get; set; } = "";
        public string   CreatedByName { get; set; } = "";
        public DateTime CreatedAt     { get; set; }
    }

    private sealed class PersonalRow
    {
        public int       Id          { get; set; }
        public string    Title       { get; set; } = "";
        public string?   Description { get; set; }
        public DateTime? DueDate     { get; set; }
    }
}
