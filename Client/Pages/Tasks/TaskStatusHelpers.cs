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
    ///  3. Submission pending mentor review → "PendingMentorReview"
    ///  4. Mentor approved, reviewer pending → "ApprovedByMentor"
    ///  5. Fall back to task.Status (covers Done, InProgress, Open, etc.)
    /// </summary>
    public static string ResolveDisplayStatus(TaskItemDto task)
    {
        if (!task.IsSubmission || task.LatestSubmissionStatus is null)
            return task.Status;

        if (task.LatestMentorStatus == "Returned")
            return "ReturnedByMentor";

        if (task.LatestSubmissionStatus == "NeedsRevision")
            return "ReturnedForRevision";

        if (task.LatestMentorStatus == "Pending")
            return "PendingMentorReview";

        if (task.LatestMentorStatus == "Approved" && task.LatestSubmissionStatus == "Submitted")
            return "ApprovedByMentor";

        return task.Status;
    }

    /// <summary>True when the task requires the student's own action right now:
    /// returned/needs-revision (must resubmit), approved-but-unconfirmed
    /// (must confirm Moodle), overdue, or due within 3 days. False for tasks
    /// waiting on the mentor (PendingMentorReview) or already Done.</summary>
    public static bool TaskRequiresAction(TaskItemDto t)
    {
        string resolved = ResolveDisplayStatus(t);
        if (resolved is "ReturnedByMentor" or "ReturnedForRevision") return true;
        if (resolved == "ApprovedByMentor") return true;            // must confirm Moodle
        if (resolved == "PendingMentorReview")       return false;  // waiting on mentor
        if (t.Status  == "Done")                     return false;
        if (t.DueDate.HasValue && t.DueDate.Value.Date < DateTime.Today)                    return true; // overdue
        if (t.DueDate.HasValue && (t.DueDate.Value.Date - DateTime.Today).TotalDays <= 3)   return true; // due soon
        return false;
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
}