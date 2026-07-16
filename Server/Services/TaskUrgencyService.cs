namespace AuthWithAdmin.Server.Services;

using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  TaskUrgencyService — PHASE 1 of the business-logic consolidation epic
//  (design/business-logic-consolidation-epic.md, Concept 4).
//
//  Canonical "does this task need a human to act, and how urgently" answer.
//  Promotes the one already-correct-but-unused implementation found by the
//  audit — ProjectsController.GetMyTasks's "IsUrgent" SQL flag, the only one
//  of ten competing formulas that respects per-team due-date overrides and
//  excludes tasks that already have a submission attached. Everywhere else
//  (ActionCenterCard, UpcomingSubmissionsCard, TaskStatusHelpers, Mentor's
//  three different overdue counts) re-derives its own answer from raw fields;
//  this service exists so Phase 3 can point all of them at one answer instead.
//
//  Computed server-side, in UTC, always. No consumer should ever recompute
//  "is this overdue" from DateTime.Today (browser-local) again — that was the
//  source of the UTC-vs-local discrepancy the demo audit found.
//
//  IMPORTANT — product boundary: this is the ATTENTION concept, separate from
//  ProjectHealthService's SCHEDULE concept. A task can be the reason a project
//  needs attention while the project's schedule health is still Green (e.g. a
//  submission is waiting for mentor review, but no milestone is actually
//  late yet). Do not fold this service's output into ProjectHealthResult.
//
//  PHASE 1 SCOPE: new, self-contained, unused by any controller/component yet.
// ─────────────────────────────────────────────────────────────────────────────

// TaskAttentionReasons now lives in Shared/AuthSharedModels/TaskAttentionReasons.cs
// (see the using directive above) — moved so client components can reuse the
// exact same canonical labels instead of re-declaring their own copy.

/// <summary>Raw facts about one task, as needed to classify urgency. The
/// caller is responsible for resolving DueDate to the EFFECTIVE due date
/// (after TeamTaskDueDateOverrides/TeamMilestoneDueDateOverrides) before
/// building this row — this service does not know about override tables,
/// it only classifies whatever due date it's given.</summary>
public class TaskUrgencyInputRow
{
    public int       TaskId               { get; set; }
    public bool       IsMandatory          { get; set; }
    public bool       IsSubmission         { get; set; }
    /// <summary>Raw Tasks.Status (Open/InProgress/SubmittedToMentor/
    /// ReturnedForRevision/RevisionSubmitted/ApprovedForSubmission/Done).</summary>
    public string     Status               { get; set; } = "";
    public DateTime?  EffectiveDueDate     { get; set; }
    public DateTime?  ClosedAt             { get; set; }
    /// <summary>Null when no submission exists yet for this task.</summary>
    public string?    LatestSubmissionStatus { get; set; }
    /// <summary>Pending | Approved | Returned | null (no submission yet).</summary>
    public string?    LatestMentorStatus     { get; set; }
    public bool       LatestMoodleConfirmed  { get; set; }
}

public class TaskUrgencyResult
{
    public int    TaskId          { get; set; }
    /// <summary>The canonical overdue flag — promoted from GetMyTasks's
    /// IsUrgent SQL. Mandatory, not already submitted, not closed, not in a
    /// terminal state, and past the effective due date.</summary>
    public bool   IsOverdue       { get; set; }
    /// <summary>None | ReturnedForRevision | Overdue | PendingMoodleConfirmation
    /// | PendingMentorReview | DueSoon — see TaskAttentionReasons.</summary>
    public string AttentionReason { get; set; } = TaskAttentionReasons.None;
    /// <summary>Lower = more urgent. Matches the priority order already
    /// established in ActionCenterCard (Returned &gt; Overdue &gt; PendingMoodle),
    /// extended with PendingMentorReview and DueSoon. int.MaxValue for None,
    /// so a plain ascending sort naturally pushes non-urgent tasks last.</summary>
    public int    AttentionRank   { get; set; } = int.MaxValue;
}

public class TaskUrgencyService
{
    private readonly DbRepository _db;
    public TaskUrgencyService(DbRepository db) => _db = db;

    /// <summary>Bulk fetch + classify for one project — the shape every real
    /// consumer needs (Dashboard cards, My Tasks page, mentor overdue counts),
    /// rather than one task at a time.</summary>
    public async Task<List<TaskUrgencyResult>> GetTaskUrgencyForProjectAsync(int projectId)
    {
        var rows = await FetchTaskRowsAsync(projectId);
        var utcNow = DateTime.UtcNow;
        return rows.Select(r => ComputeUrgency(r, utcNow)).ToList();
    }

    public async Task<TaskUrgencyResult?> GetTaskUrgencyAsync(int taskId)
    {
        var rows = await FetchTaskRowsAsync(projectId: null, taskId: taskId);
        var row = rows.FirstOrDefault();
        return row is null ? null : ComputeUrgency(row, DateTime.UtcNow);
    }

    private async Task<List<TaskUrgencyInputRow>> FetchTaskRowsAsync(int? projectId, int? taskId = null)
    {
        // Effective due date follows the same override chain as
        // ProjectsController.GetMyTasks's IsUrgent SQL: per-team task
        // override, then per-team milestone override, then the task's own
        // DueDate. Latest submission fields via the same "most recent
        // TaskSubmissions row" pattern used across the codebase.
        string where = taskId.HasValue ? "t.Id = @TaskId" : "t.ProjectId = @ProjectId";
        var rows = await _db.GetRecordsAsync<TaskUrgencyQueryRow>($@"
            SELECT  t.Id                        AS TaskId,
                    t.IsMandatory                AS IsMandatory,
                    t.IsSubmission               AS IsSubmission,
                    t.Status                     AS Status,
                    COALESCE(tto.OverrideDueDate, mo.OverrideDueDate, t.DueDate) AS EffectiveDueDate,
                    t.ClosedAt                   AS ClosedAt,
                    (SELECT s.Status FROM TaskSubmissions s WHERE s.TaskId = t.Id
                     ORDER BY s.Id DESC LIMIT 1) AS LatestSubmissionStatus,
                    (SELECT s.MentorStatus FROM TaskSubmissions s WHERE s.TaskId = t.Id
                     ORDER BY s.Id DESC LIMIT 1) AS LatestMentorStatus,
                    (SELECT CASE WHEN s.MoodleSubmittedAt IS NOT NULL OR s.CourseSubmittedAt IS NOT NULL
                                 THEN 1 ELSE 0 END
                     FROM TaskSubmissions s WHERE s.TaskId = t.Id
                     ORDER BY s.Id DESC LIMIT 1)  AS LatestMoodleConfirmed
            FROM    Tasks t
            LEFT JOIN Projects p ON p.Id = t.ProjectId
            LEFT JOIN TeamTaskDueDateOverrides tto
                            ON tto.TeamId = p.TeamId AND tto.TaskId = t.Id
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = p.TeamId AND mo.ProjectMilestoneId = t.ProjectMilestoneId
            WHERE   {where}",
            taskId.HasValue ? new { TaskId = taskId.Value } : new { ProjectId = projectId!.Value });

        return (rows ?? Enumerable.Empty<TaskUrgencyQueryRow>()).Select(r => new TaskUrgencyInputRow
        {
            TaskId                 = r.TaskId,
            IsMandatory             = r.IsMandatory,
            IsSubmission            = r.IsSubmission,
            Status                  = r.Status,
            EffectiveDueDate        = r.EffectiveDueDate,
            ClosedAt                = r.ClosedAt,
            LatestSubmissionStatus  = r.LatestSubmissionStatus,
            LatestMentorStatus      = r.LatestMentorStatus,
            LatestMoodleConfirmed   = r.LatestMoodleConfirmed,
        }).ToList();
    }

    private sealed class TaskUrgencyQueryRow
    {
        public int       TaskId                 { get; set; }
        public bool      IsMandatory            { get; set; }
        public bool      IsSubmission           { get; set; }
        public string    Status                 { get; set; } = "";
        public DateTime? EffectiveDueDate       { get; set; }
        public DateTime? ClosedAt               { get; set; }
        public string?   LatestSubmissionStatus { get; set; }
        public string?   LatestMentorStatus     { get; set; }
        public bool      LatestMoodleConfirmed  { get; set; }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Pure classification — no DB access. "utcNow" is a parameter (not read
    //  internally from DateTime.UtcNow) specifically so unit tests can pin
    //  exact UTC-boundary fixtures.
    // ═════════════════════════════════════════════════════════════════════
    public static TaskUrgencyResult ComputeUrgency(TaskUrgencyInputRow t, DateTime utcNow)
    {
        var today = utcNow.Date;
        bool hasSubmission = t.LatestSubmissionStatus is not null;
        bool isTerminalDone = t.Status is "Done" or "Completed" or "ApprovedForSubmission";

        // Promoted verbatim from ProjectsController.GetMyTasks's IsUrgent SQL.
        bool isOverdue = t.IsMandatory
            && !isTerminalDone
            && t.Status != "SubmittedToMentor"
            && t.ClosedAt is null
            && !hasSubmission
            && t.EffectiveDueDate is not null
            && t.EffectiveDueDate.Value.Date < today;

        string reason;
        if (t.LatestMentorStatus == "Returned" || t.LatestSubmissionStatus == "NeedsRevision")
        {
            reason = TaskAttentionReasons.ReturnedForRevision;
        }
        else if (isOverdue)
        {
            reason = TaskAttentionReasons.Overdue;
        }
        else if (t.IsSubmission && t.LatestMentorStatus == "Approved" && !t.LatestMoodleConfirmed)
        {
            reason = TaskAttentionReasons.PendingMoodleConfirmation;
        }
        else if (t.Status == "SubmittedToMentor" || t.LatestMentorStatus == "Pending")
        {
            reason = TaskAttentionReasons.PendingMentorReview;
        }
        else if (!isTerminalDone && t.EffectiveDueDate is not null)
        {
            double daysUntilDue = (t.EffectiveDueDate.Value.Date - today).TotalDays;
            reason = daysUntilDue is >= 0 and <= 3 ? TaskAttentionReasons.DueSoon : TaskAttentionReasons.None;
        }
        else
        {
            reason = TaskAttentionReasons.None;
        }

        int rank = reason switch
        {
            TaskAttentionReasons.ReturnedForRevision       => 0,
            TaskAttentionReasons.Overdue                   => 1,
            TaskAttentionReasons.PendingMoodleConfirmation => 2,
            TaskAttentionReasons.PendingMentorReview        => 3,
            TaskAttentionReasons.DueSoon                    => 4,
            _                                                => int.MaxValue,
        };

        return new TaskUrgencyResult
        {
            TaskId          = t.TaskId,
            IsOverdue       = isOverdue,
            AttentionReason = reason,
            AttentionRank   = rank,
        };
    }
}
