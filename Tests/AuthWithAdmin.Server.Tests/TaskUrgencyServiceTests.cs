using AuthWithAdmin.Server.Services;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Tests;

public class TaskUrgencyServiceTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 16, 9, 0, 0, DateTimeKind.Utc);

    private static TaskUrgencyInputRow Row(
        bool isMandatory = true,
        bool isSubmission = true,
        string status = "Open",
        DateTime? dueDate = null,
        DateTime? closedAt = null,
        string? latestSubmissionStatus = null,
        string? latestMentorStatus = null,
        bool latestMoodleConfirmed = false) => new()
    {
        TaskId = 1,
        IsMandatory = isMandatory,
        IsSubmission = isSubmission,
        Status = status,
        EffectiveDueDate = dueDate,
        ClosedAt = closedAt,
        LatestSubmissionStatus = latestSubmissionStatus,
        LatestMentorStatus = latestMentorStatus,
        LatestMoodleConfirmed = latestMoodleConfirmed,
    };

    // ── Completed vs overdue ───────────────────────────────────────────────

    [Fact]
    public void MandatoryTask_PastDue_NoSubmission_IsOverdue()
    {
        var t = Row(status: "Open", dueDate: UtcNow.Date.AddDays(-1));
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        Assert.True(r.IsOverdue);
        Assert.Equal(TaskAttentionReasons.Overdue, r.AttentionReason);
        Assert.Equal(1, r.AttentionRank);
    }

    [Fact]
    public void DoneTask_PastDue_IsNotOverdue()
    {
        var t = Row(status: "Done", dueDate: UtcNow.Date.AddDays(-30));
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        Assert.False(r.IsOverdue);
        Assert.Equal(TaskAttentionReasons.None, r.AttentionReason);
        Assert.Equal(int.MaxValue, r.AttentionRank);
    }

    [Fact]
    public void NonMandatoryTask_PastDue_NeverCountsAsOverdue()
    {
        var t = Row(isMandatory: false, status: "Open", dueDate: UtcNow.Date.AddDays(-5));
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        Assert.False(r.IsOverdue);
    }

    [Fact]
    public void ClosedTask_PastDue_IsNotOverdue()
    {
        var t = Row(status: "Open", dueDate: UtcNow.Date.AddDays(-5), closedAt: UtcNow.AddDays(-1));
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        Assert.False(r.IsOverdue);
    }

    [Fact]
    public void TaskWithAnySubmissionAlready_IsNeverOverdue_EvenIfPastDue()
    {
        var t = Row(status: "SubmittedToMentor", dueDate: UtcNow.Date.AddDays(-10),
            latestSubmissionStatus: "Submitted", latestMentorStatus: "Pending");
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        Assert.False(r.IsOverdue);
        Assert.Equal(TaskAttentionReasons.PendingMentorReview, r.AttentionReason);
    }

    // ── Returned submissions — highest priority reason ─────────────────────

    [Fact]
    public void MentorReturned_IsReturnedForRevision_RegardlessOfDueDate()
    {
        var t = Row(status: "ReturnedForRevision", dueDate: UtcNow.Date.AddDays(10),
            latestSubmissionStatus: "Submitted", latestMentorStatus: "Returned");
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        Assert.Equal(TaskAttentionReasons.ReturnedForRevision, r.AttentionReason);
        Assert.Equal(0, r.AttentionRank);
    }

    [Fact]
    public void SubmissionNeedsRevision_IsReturnedForRevision_EvenWithoutMentorStatus()
    {
        var t = Row(status: "RevisionSubmitted", dueDate: UtcNow.Date.AddDays(10),
            latestSubmissionStatus: "NeedsRevision", latestMentorStatus: null);
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        Assert.Equal(TaskAttentionReasons.ReturnedForRevision, r.AttentionReason);
    }

    [Fact]
    public void ReturnedForRevision_TakesPriorityOver_OverdueSignal()
    {
        // Task IS mandatory/overdue-eligible by date, but has a submission whose
        // mentor status is Returned — Returned must win, and IsOverdue is false
        // anyway because a submission already exists.
        var t = Row(isMandatory: true, status: "ReturnedForRevision", dueDate: UtcNow.Date.AddDays(-5),
            latestSubmissionStatus: "Submitted", latestMentorStatus: "Returned");
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        Assert.False(r.IsOverdue);
        Assert.Equal(TaskAttentionReasons.ReturnedForRevision, r.AttentionReason);
    }

    // ── Mentor-review waiting states ────────────────────────────────────────

    [Fact]
    public void SubmittedToMentor_WithPendingMentorStatus_IsPendingMentorReview()
    {
        var t = Row(status: "SubmittedToMentor", dueDate: UtcNow.Date.AddDays(2),
            latestSubmissionStatus: "Submitted", latestMentorStatus: "Pending");
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        Assert.Equal(TaskAttentionReasons.PendingMentorReview, r.AttentionReason);
        Assert.Equal(3, r.AttentionRank);
    }

    [Fact]
    public void PendingMentorStatus_WithoutSubmittedToMentorTaskStatus_StillFlagsPendingReview()
    {
        var t = Row(status: "RevisionSubmitted", dueDate: UtcNow.Date.AddDays(2),
            latestSubmissionStatus: "Submitted", latestMentorStatus: "Pending");
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        Assert.Equal(TaskAttentionReasons.PendingMentorReview, r.AttentionReason);
    }

    [Fact]
    public void ApprovedSubmission_ForSubmissionTask_NotYetMoodleConfirmed_IsPendingMoodleConfirmation()
    {
        var t = Row(isSubmission: true, status: "ApprovedForSubmission", dueDate: UtcNow.Date.AddDays(2),
            latestSubmissionStatus: "Approved", latestMentorStatus: "Approved", latestMoodleConfirmed: false);
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        Assert.Equal(TaskAttentionReasons.PendingMoodleConfirmation, r.AttentionReason);
        Assert.Equal(2, r.AttentionRank);
    }

    [Fact]
    public void ApprovedSubmission_AlreadyMoodleConfirmed_FallsThroughToNone()
    {
        var t = Row(isSubmission: true, status: "ApprovedForSubmission", dueDate: UtcNow.Date.AddDays(2),
            latestSubmissionStatus: "Approved", latestMentorStatus: "Approved", latestMoodleConfirmed: true);
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        // "ApprovedForSubmission" is a terminal status (isTerminalDone), so the
        // DueSoon check — guarded by !isTerminalDone — never runs here even
        // though the due date is 2 days out. Once Moodle is confirmed there is
        // nothing left to flag.
        Assert.Equal(TaskAttentionReasons.None, r.AttentionReason);
    }

    // ── Due-soon window ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void DueSoon_AppliesOnlyWithinZeroToThreeDaysInclusive(int daysUntilDue, bool expectDueSoon)
    {
        var t = Row(isMandatory: false, status: "Open", dueDate: UtcNow.Date.AddDays(daysUntilDue));
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        Assert.Equal(expectDueSoon, r.AttentionReason == TaskAttentionReasons.DueSoon);
    }

    // ── UTC date boundaries ──────────────────────────────────────────────

    [Fact]
    public void DueDateEqualToToday_IsNotYetOverdue()
    {
        var t = Row(status: "Open", dueDate: UtcNow.Date);
        var r = TaskUrgencyService.ComputeUrgency(t, UtcNow);

        Assert.False(r.IsOverdue);
    }

    [Fact]
    public void JustBeforeUtcMidnight_DueToday_StillNotOverdue()
    {
        var utcNow = new DateTime(2026, 7, 16, 23, 59, 0, DateTimeKind.Utc);
        var t = Row(status: "Open", dueDate: new DateTime(2026, 7, 16));
        var r = TaskUrgencyService.ComputeUrgency(t, utcNow);

        Assert.False(r.IsOverdue);
    }

    [Fact]
    public void JustAfterUtcMidnight_PreviousDayDue_BecomesOverdue()
    {
        var utcNow = new DateTime(2026, 7, 17, 0, 0, 1, DateTimeKind.Utc);
        var t = Row(status: "Open", dueDate: new DateTime(2026, 7, 16));
        var r = TaskUrgencyService.ComputeUrgency(t, utcNow);

        Assert.True(r.IsOverdue);
        Assert.Equal(TaskAttentionReasons.Overdue, r.AttentionReason);
    }

    // ── Rank ordering — proves the priority chain sorts as documented ────

    [Fact]
    public void AttentionRank_SortsInDocumentedPriorityOrder()
    {
        var returned   = TaskUrgencyService.ComputeUrgency(Row(status: "ReturnedForRevision", latestSubmissionStatus: "Submitted", latestMentorStatus: "Returned", dueDate: UtcNow.Date.AddDays(10)), UtcNow);
        var overdue    = TaskUrgencyService.ComputeUrgency(Row(status: "Open", dueDate: UtcNow.Date.AddDays(-1)), UtcNow);
        var moodle     = TaskUrgencyService.ComputeUrgency(Row(isSubmission: true, status: "ApprovedForSubmission", latestMentorStatus: "Approved", latestSubmissionStatus: "Approved", latestMoodleConfirmed: false, dueDate: UtcNow.Date.AddDays(10)), UtcNow);
        var mentor     = TaskUrgencyService.ComputeUrgency(Row(status: "SubmittedToMentor", latestSubmissionStatus: "Submitted", latestMentorStatus: "Pending", dueDate: UtcNow.Date.AddDays(10)), UtcNow);
        var dueSoon    = TaskUrgencyService.ComputeUrgency(Row(isMandatory: false, status: "Open", dueDate: UtcNow.Date.AddDays(2)), UtcNow);
        var none       = TaskUrgencyService.ComputeUrgency(Row(isMandatory: false, status: "Open", dueDate: UtcNow.Date.AddDays(30)), UtcNow);

        var ranks = new[] { returned, overdue, moodle, mentor, dueSoon, none }.Select(r => r.AttentionRank).ToList();
        var sorted = ranks.OrderBy(x => x).ToList();

        Assert.Equal(sorted, ranks); // already in ascending, documented priority order
        Assert.True(ranks[0] < ranks[1]);
        Assert.True(ranks[^2] < ranks[^1]);
        Assert.Equal(int.MaxValue, none.AttentionRank);
    }
}
