using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Pages.Dashboard;

/// <summary>
/// The student dashboard's shared derivation rules.
///
/// <para><b>Why this exists.</b> Before Phase 4D the same four rules —
/// "is this task complete", "is this task an exception", the current-milestone
/// priority chain, and the next-submission pick — were copy-pasted across the
/// dashboard's sections, each with a comment explaining that it had to stay
/// byte-identical to the others. The rules moved here once, and every consumer
/// since (the focus card, both attention cards, the deadlines card and the page
/// itself) reads them from here.</para>
///
/// <para>Pure functions over DTOs. No service calls, no state, no sample data,
/// and nothing server-side is touched.</para>
/// </summary>
public static class StudentDashboardModel
{
    // ── Completeness ─────────────────────────────────────────────────────────

    /// <summary>A submission task is only "done" once it has been submitted,
    /// approved by the mentor AND confirmed in Moodle. A plain task is done at
    /// Status == "Done".</summary>
    public static bool IsComplete(TaskSummaryDto t) =>
        t.IsSubmission
            ? t.LatestSubmissionStatus is not null
              && t.LatestMentorStatus == "Approved"
              && t.LatestMoodleConfirmed
            : t.Status == "Done";

    // ── Current milestone ────────────────────────────────────────────────────

    /// <summary>The milestone the student is standing in, by descending
    /// confidence: open+in-progress → open → in-progress → first not-completed.</summary>
    public static MilestoneSummaryDto? CurrentMilestone(IReadOnlyList<MilestoneSummaryDto> milestones) =>
        milestones.FirstOrDefault(m => m.IsCurrentlyOpen && m.Status == "InProgress")
        ?? milestones.FirstOrDefault(m => m.IsCurrentlyOpen)
        ?? milestones.FirstOrDefault(m => m.Status == "InProgress")
        ?? milestones.FirstOrDefault(m => m.Status != "Completed");

    /// <summary>Completed milestones as a percentage of all milestones.</summary>
    public static int OverallPercent(IReadOnlyList<MilestoneSummaryDto> milestones) =>
        milestones.Count == 0
            ? 0
            : milestones.Count(m => m.Status == "Completed") * 100 / milestones.Count;

    // ── Journey position ─────────────────────────────────────────────────────

    /// <summary>
    /// Where a milestone sits on the journey: the four-way split the dashboard
    /// already draws as its bands — הושלמו · בעבודה עכשיו · הבא בתור · בהמשך.
    ///
    /// <para>This is NOT a second milestone-state system. It is the grouping
    /// StudentDashboardHero.BuildPhases has always computed inline, lifted here
    /// verbatim so the hero's strip and the journey view opened from it cannot
    /// disagree about which milestone is "next". The underlying facts —
    /// Status, IsCurrentlyOpen, task completion — are still the server's and
    /// are not recomputed.</para>
    /// </summary>
    public enum JourneyPosition { Completed, InWork, Next, Later }

    /// <summary>Whether a milestone is genuinely being worked on: the one the
    /// dashboard calls current, one the data says is InProgress/Delayed, or one
    /// that already has finished tasks in it.
    ///
    /// <para>Deliberately NOT IsCurrentlyOpen. Being inside a visibility window
    /// says a milestone CAN be worked on, not that it is — on live data six of
    /// this project's milestones are inside their window at once, which is what
    /// collapsed the strip to two bands before this rule replaced it.</para></summary>
    public static bool IsInWork(MilestoneSummaryDto m, MilestoneSummaryDto? anchor) =>
        m.ProjectMilestoneId == anchor?.ProjectMilestoneId
        || m.Status is "InProgress" or "Delayed"
        || (m.Tasks.Count > 0 && m.Tasks.Any(IsComplete));

    /// <summary>
    /// Every milestone's position, keyed by ProjectMilestoneId.
    ///
    /// <para>"Next" is the first milestone still ahead in the PROJECT'S OWN
    /// ORDER, not the earliest by date. The two genuinely differ in live data —
    /// a late אפיון milestone can fall due after an early פיתוח one — and the
    /// sequence is what /project's stepper draws, so picking by date here would
    /// have the two screens disagree about what comes next.</para>
    /// </summary>
    public static IReadOnlyDictionary<int, JourneyPosition> JourneyPositions(
        IReadOnlyList<MilestoneSummaryDto> milestones)
    {
        var anchor = CurrentMilestone(milestones);
        var map    = new Dictionary<int, JourneyPosition>();
        var nextTaken = false;

        foreach (var m in milestones)
        {
            JourneyPosition pos;

            if (m.Status == "Completed")
            {
                pos = JourneyPosition.Completed;
            }
            else if (IsInWork(m, anchor))
            {
                pos = JourneyPosition.InWork;
            }
            else if (!nextTaken)
            {
                pos = JourneyPosition.Next;
                nextTaken = true;
            }
            else
            {
                pos = JourneyPosition.Later;
            }

            map[m.ProjectMilestoneId] = pos;
        }

        return map;
    }

    /// <summary>
    /// Whether a milestone has actually OPENED — the availability fact, as
    /// opposed to <see cref="JourneyPositions"/>'s ordering fact.
    ///
    /// <para>This is the phrase the product already uses in two places for
    /// exactly this question — MilestoneDetailModal's progress stage and the
    /// project workspace's stage test: open by date, or carrying an
    /// InProgress/Delayed status, or holding real completed work. Completed is
    /// added because a finished milestone is obviously reviewable.</para>
    ///
    /// <para><b>Read this, never the journey position, to decide whether a
    /// student may act.</b> A milestone can be positioned "הבאה" and still be
    /// open — a second milestone inside its date window that nobody has started
    /// yet lands there — so gating actions on position would close a milestone
    /// the rules actually leave open.</para>
    /// </summary>
    public static bool HasOpened(MilestoneSummaryDto m) =>
        m.Status == "Completed"
        || m.IsCurrentlyOpen
        || m.Status is "InProgress" or "Delayed"
        || m.Tasks.Any(IsComplete);

    /// <summary>The single milestone the journey calls "הבאה", or null when the
    /// project has nothing still ahead of it.</summary>
    public static MilestoneSummaryDto? NextMilestone(IReadOnlyList<MilestoneSummaryDto> milestones)
    {
        var positions = JourneyPositions(milestones);
        return milestones.FirstOrDefault(
            m => positions.TryGetValue(m.ProjectMilestoneId, out var p)
                 && p == JourneyPosition.Next);
    }

    // ── Exceptions ("דורש התייחסות") ─────────────────────────────────────────

    /// <summary>Task-level exception kinds, in the order they are surfaced.</summary>
    public enum AttentionKind { Returned, Overdue, PendingMoodle }

    /// <summary>Returned by the mentor, or returned for revision by the
    /// reviewer — the student must act.</summary>
    public static bool IsReturned(TaskSummaryDto t) =>
        t.LatestMentorStatus == "Returned" || t.LatestSubmissionStatus == "NeedsRevision";

    /// <summary>Past its due date and still genuinely open.</summary>
    public static bool IsOverdue(TaskSummaryDto t) =>
        t.DueDate.HasValue
        && t.DueDate.Value.Date < DateTime.Today
        && !IsComplete(t)
        && t.Status is "Open" or "InProgress" or "ReturnedForRevision";

    /// <summary>Approved by the mentor but the student has not confirmed the
    /// Moodle submission yet.</summary>
    public static bool IsPendingMoodle(TaskSummaryDto t) =>
        t.IsSubmission && t.LatestMentorStatus == "Approved" && !t.LatestMoodleConfirmed;

    /// <summary>Whether a task is surfaced by the attention band at all. Used
    /// by the calm "upcoming" list to exclude it, so the same task is never
    /// shown as both urgent and routine.</summary>
    public static bool IsAttentionItem(TaskSummaryDto t) =>
        IsReturned(t) || IsOverdue(t) || IsPendingMoodle(t);

    /// <summary>Which kind a task is, using the precedence the dashboard has
    /// always used (returned beats overdue beats pending-Moodle).
    /// Null when the task is not an exception.</summary>
    public static AttentionKind? KindOf(TaskSummaryDto t) =>
        IsReturned(t)      ? AttentionKind.Returned
        : IsOverdue(t)     ? AttentionKind.Overdue
        : IsPendingMoodle(t) ? AttentionKind.PendingMoodle
        : null;

    /// <summary>
    /// Every task-level exception across the project, worst kind first.
    ///
    /// <para>Stated here rather than inside the card that draws it because the
    /// page itself also needs the answer — it decides whether to render the
    /// attention row at all, and a second copy of the filter in Dashboard.razor
    /// could drift from the card's. The card maps these to rows; nothing else
    /// re-derives them.</para>
    ///
    /// <para><paramref name="excludeTaskId"/> drops the task the focus card
    /// already shows in full, so one exception is never stated twice on a
    /// single screen.</para>
    /// </summary>
    public static List<(MilestoneSummaryDto Milestone, TaskSummaryDto Task, AttentionKind Kind)>
        AttentionTasks(IReadOnlyList<MilestoneSummaryDto> milestones, int? excludeTaskId = null) =>
        milestones
            .SelectMany(ms => ms.Tasks.Select(t => (Milestone: ms, Task: t)))
            .Where(x => x.Task.Id != excludeTaskId)
            .Select(x => (x.Milestone, x.Task, Kind: KindOf(x.Task)))
            .Where(x => x.Kind is not null)
            .Select(x => (x.Milestone, x.Task, Kind: x.Kind!.Value))
            .OrderBy(x => (int)x.Kind)
            .ToList();

    // ── Next submission ──────────────────────────────────────────────────────

    /// <summary>Nearest-due incomplete submission across the whole project,
    /// regardless of exception status. This is the task the focus card shows,
    /// and the one the upcoming list excludes by identity.</summary>
    public static TaskSummaryDto? NextSubmission(IReadOnlyList<MilestoneSummaryDto> milestones) =>
        milestones
            .SelectMany(m => m.Tasks)
            .Where(t => t.IsSubmission && t.DueDate.HasValue && !IsComplete(t))
            .OrderBy(t => t.DueDate)
            .FirstOrDefault();

    /// <summary>The milestone a given task belongs to.</summary>
    public static MilestoneSummaryDto? MilestoneOf(
        IReadOnlyList<MilestoneSummaryDto> milestones, TaskSummaryDto? task) =>
        task is null ? null : milestones.FirstOrDefault(m => m.Tasks.Any(t => t.Id == task.Id));

    /// <summary>The single task the focus card recommends: the nearest-due
    /// incomplete submission across the project, else the first incomplete task
    /// of the current milestone (submission first, then any).
    ///
    /// <para>Lives here rather than on the focus card because the attention
    /// band and the deadline list both need to exclude exactly this task by
    /// identity — if the rule were stated twice they could drift and the same
    /// task would appear twice on one screen.</para></summary>
    public static TaskSummaryDto? FocusTask(IReadOnlyList<MilestoneSummaryDto> milestones)
    {
        var current = CurrentMilestone(milestones);
        return NextSubmission(milestones)
            ?? current?.Tasks.FirstOrDefault(t => !IsComplete(t) && t.IsSubmission)
            ?? current?.Tasks.FirstOrDefault(t => !IsComplete(t));
    }

    // ── Due-date phrasing ────────────────────────────────────────────────────

    /// <summary>Whole days from today to the due date; negative when overdue.</summary>
    public static int? DaysUntil(DateTime? due) =>
        due is { } d ? (int)(d.Date - DateTime.Today).TotalDays : null;

    /// <summary>The Master's due-date phrasing, shared by every dashboard list
    /// so "היום" never means two different things on one screen.</summary>
    public static string DaysText(int days) =>
        days < 0    ? $"{-days} ימים באיחור"
        : days == 0 ? "היום"
        : days == 1 ? "מחר"
        : $"נותרו {days} ימים";
}
