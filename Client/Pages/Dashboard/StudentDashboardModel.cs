using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Pages.Dashboard;

/// <summary>
/// The student dashboard's shared derivation rules.
///
/// <para><b>Why this exists.</b> Before Phase 4D the same four rules —
/// "is this task complete", "is this task an exception", the current-milestone
/// priority chain, and the next-submission pick — were copy-pasted across
/// StudentDashboardHero, ActionCenterCard and UpcomingSubmissionsCard, each
/// with a comment explaining that it had to stay byte-identical to the others.
/// The V4 rebuild adds two more consumers, so the rules move here once. The
/// logic is unchanged: every method below is a verbatim lift, not a rewrite.</para>
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

    /// <summary>Which kind a task is, using the same precedence the old
    /// ActionCenterCard used (returned beats overdue beats pending-Moodle).
    /// Null when the task is not an exception.</summary>
    public static AttentionKind? KindOf(TaskSummaryDto t) =>
        IsReturned(t)      ? AttentionKind.Returned
        : IsOverdue(t)     ? AttentionKind.Overdue
        : IsPendingMoodle(t) ? AttentionKind.PendingMoodle
        : null;

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
