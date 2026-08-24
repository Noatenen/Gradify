using System;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Pages.Tasks;

/// <summary>
/// Shared status-resolution logic for the student tasks experience.
/// Single source of truth for turning raw TaskItemDto fields into the
/// display status used by the tasks page's urgency buckets, the milestone
/// accordion, and the flat task lists — previously duplicated identically
/// across StudentTasksPage and TaskMilestoneAccordion.
/// </summary>
public static class TaskStatusHelpers
{
    /// <summary>
    /// Effective status resolution. Priority order for submission tasks:
    ///  1. Mentor returned   → "ReturnedByMentor"    (student must fix + resubmit)
    ///  2. Reviewer returned → "ReturnedForRevision"  (NeedsRevision from lecturer)
    ///  3. Task already terminal → "Done"
    ///  4. Submission pending mentor review → "PendingMentorReview"
    ///  5. Mentor approved, not yet confirmed → "ApprovedByMentor"
    ///  6. Fall back to task.Status (InProgress, Open, etc.)
    ///
    /// <para>Step 3 is the terminal-state check, and it has to sit ABOVE step 5.
    /// The Moodle-confirmation handler
    /// (TaskSubmissionsController POST {id}/mark-moodle-submitted) writes
    /// MoodleSubmittedAt and sets Tasks.Status = 'Done', but it deliberately
    /// leaves the submission row's own Status/MentorStatus alone as an audit
    /// record — they stay "Submitted"/"Approved" forever. With step 5 above
    /// step 3, every finished submission task therefore matched
    /// "ApprovedByMentor" and the fallback was unreachable: "Done" was
    /// impossible for any submission task, so a Moodle-confirmed task still
    /// told the student to go submit it, dropped out of the הושלמו filter, and
    /// disagreed with the milestone's own DoneCount (which the server computes
    /// from the same normalized 'Done').
    ///
    /// Steps 1–2 stay above it on purpose: a task whose latest submission was
    /// returned is NOT done, whatever a stale Tasks.Status says. The server
    /// applies the same precedence in NormalizeTaskStatus, so both ends agree.
    ///
    /// "ApprovedByMentor" keeps its own meaning and stays actionable — after
    /// this reorder it matches only tasks that genuinely have not been
    /// confirmed in Moodle yet, which is exactly when the student still has
    /// something to do.</para>
    /// </summary>
    public static string ResolveDisplayStatus(TaskItemDto task)
    {
        if (!task.IsSubmission || task.LatestSubmissionStatus is null)
            return task.Status;

        if (task.LatestMentorStatus == "Returned")
            return "ReturnedByMentor";

        if (task.LatestSubmissionStatus == "NeedsRevision")
            return "ReturnedForRevision";

        // Terminal state, checked before the approval rules below — see the
        // note on this method. Tasks.Status is pipeline-owned past
        // ApprovedForSubmission (StudentEditableTaskStatuses is {Open,
        // InProgress}), so 'Done' here means the Moodle confirmation ran.
        if (task.Status == "Done")
            return "Done";

        if (task.LatestMentorStatus == "Pending")
            return "PendingMentorReview";

        if (task.LatestMentorStatus == "Approved" && task.LatestSubmissionStatus == "Submitted")
            return "ApprovedByMentor";

        return task.Status;
    }

    /// <summary>
    /// The 5 user-facing status categories shown across the tasks page's
    /// summary cards, filter chips, and per-row status badges. This is the
    /// single grouping axis — every task falls into exactly one of these,
    /// mutually exclusive by construction (see <see cref="GetCategory"/>).
    /// Overdue is intentionally NOT a member here — it is a separate,
    /// orthogonal date-based flag (see <see cref="IsOverdue"/>) that can
    /// overlap with Open or Returned.
    /// </summary>
    public enum TaskCategory { Open, Returned, ApprovedForSubmission, Completed }

    /// <summary>
    /// Maps a task's resolved display status onto one of the 4 primary
    /// categories (פתוחות / הוחזרו לתיקון / מאושרות להגשה / הושלמו).
    /// "PendingMentorReview" (submitted, awaiting the mentor's decision) has
    /// no dedicated category in the new status model — it is mapped to
    /// <see cref="TaskCategory.Open"/> as the closest fit (it is still an
    /// active, non-terminal, non-returned, non-approved task), same as any
    /// other legacy raw status string that isn't Done/Returned/Approved.
    /// </summary>
    public static TaskCategory GetCategory(TaskItemDto t)
    {
        string resolved = ResolveDisplayStatus(t);
        if (resolved == "Done") return TaskCategory.Completed;
        if (resolved is "ReturnedByMentor" or "ReturnedForRevision") return TaskCategory.Returned;
        if (resolved == "ApprovedByMentor") return TaskCategory.ApprovedForSubmission;
        return TaskCategory.Open;
    }

    /// <summary>
    /// Overdue counting rule (documented per the tasks-page redesign spec):
    /// a task is overdue when it is not completed, its due date has passed,
    /// and it isn't currently sitting with the mentor/reviewer for a decision
    /// (PendingMentorReview) or awaiting only a Moodle confirmation
    /// (ApprovedByMentor) — in both of those cases the delay is not the
    /// student's to resolve, so flagging them "overdue" would be misleading.
    /// Overdue overlaps with Open and Returned by design (a returned task
    /// past its due date is both) — it is never summed into a grand total
    /// alongside the 4 primary categories above.
    /// </summary>
    public static bool IsOverdue(TaskItemDto t)
    {
        if (GetCategory(t) == TaskCategory.Completed) return false;
        if (!t.DueDate.HasValue || t.DueDate.Value.Date >= DateTime.Today) return false;
        string resolved = ResolveDisplayStatus(t);
        return resolved is not ("PendingMentorReview" or "ApprovedByMentor");
    }

    public static (string RelLabel, string AbsLabel, bool IsOverdue, bool IsSoon)
        GetDateDisplay(DateTime? dueDate)
    {
        if (!dueDate.HasValue) return ("", "", false, false);
        int    days = (dueDate.Value.Date - DateTime.Today).Days;
        string abs  = dueDate.Value.ToString("dd/MM/yy");
        if (days < 0)  return ("באיחור",           abs, true,  false);
        if (days == 0) return ("היום",              abs, false, true);
        if (days == 1) return ("מחר",               abs, false, true);
        if (days <= 7) return ($"בעוד {days} ימים", abs, false, true);
        return ("", abs, false, false);
    }

    /// <summary>
    /// The 4 workflow groups the tasks page is organized around (replacing
    /// the earlier flat 5-status filter as the PRIMARY organizing axis — the
    /// finer statuses still show on each row's badge, they just aren't
    /// top-level navigable groups anymore). Mutually exclusive, priority
    /// ordered: Completed &gt; Returned &gt; ApprovedForSubmission &gt;
    /// (mentor/teammate's court) &gt; the student's own open work.
    /// </summary>
    public enum WorkflowSection { NeedsAction, ReadyToSubmit, Team, Completed }

    /// <summary>
    /// True when a task belongs to the viewing student rather than a
    /// teammate. `GetMyTasks` returns the whole team's tasks (filtered by
    /// project, not by assignee), so `AssignedToName` can legitimately be a
    /// teammate's name — compared here against `TasksPageDto.StudentName`.
    /// An unassigned task (empty name) is treated as the student's own,
    /// since there's no evidence otherwise.
    /// </summary>
    public static bool IsMine(TaskItemDto t, string? studentName) =>
        string.IsNullOrWhiteSpace(t.AssignedToName)
        || string.IsNullOrWhiteSpace(studentName)
        || string.Equals(t.AssignedToName.Trim(), studentName.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps a task onto one of the 4 workflow sections. "PendingMentorReview"
    /// and any task assigned to a teammate both land in Team — in both cases
    /// the next move isn't the viewing student's to make.
    /// </summary>
    public static WorkflowSection GetWorkflowSection(TaskItemDto t, string? studentName)
    {
        var category = GetCategory(t);
        if (category == TaskCategory.Completed)             return WorkflowSection.Completed;
        if (category == TaskCategory.Returned)               return WorkflowSection.NeedsAction;
        if (category == TaskCategory.ApprovedForSubmission)  return WorkflowSection.ReadyToSubmit;

        if (ResolveDisplayStatus(t) == "PendingMentorReview") return WorkflowSection.Team;
        if (!IsMine(t, studentName))                          return WorkflowSection.Team;

        return WorkflowSection.NeedsAction;
    }
}