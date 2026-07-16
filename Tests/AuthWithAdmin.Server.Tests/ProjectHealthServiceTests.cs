using AuthWithAdmin.Server.Services;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Tests;

public class ProjectHealthServiceTests
{
    private static readonly DateTime Today = new(2026, 7, 16);

    private static MilestoneHealthRow Milestone(int id, string status, DateTime? due, DateTime? completedAt = null) =>
        new() { ProjectMilestoneId = id, Title = $"M{id}", Status = status, DueDate = due, CompletedAt = completedAt };

    // ── ComputeMilestoneDelay ────────────────────────────────────────────

    [Fact]
    public void OpenMilestone_PastDue_DelayIsTodayMinusDueDate()
    {
        var m = Milestone(1, "InProgress", Today.AddDays(-10));
        Assert.Equal(10, ProjectHealthService.ComputeMilestoneDelay(m, Today));
    }

    [Fact]
    public void OpenMilestone_NotYetDue_DelayIsZero()
    {
        var m = Milestone(1, "InProgress", Today.AddDays(5));
        Assert.Equal(0, ProjectHealthService.ComputeMilestoneDelay(m, Today));
    }

    [Fact]
    public void OpenMilestone_DueToday_DelayIsZero()
    {
        var m = Milestone(1, "InProgress", Today);
        Assert.Equal(0, ProjectHealthService.ComputeMilestoneDelay(m, Today));
    }

    [Fact]
    public void CompletedMilestone_FinishedLate_DelayIsCompletedMinusDue()
    {
        var m = Milestone(1, "Completed", Today.AddDays(-20), completedAt: Today.AddDays(-13));
        Assert.Equal(7, ProjectHealthService.ComputeMilestoneDelay(m, Today));
    }

    [Fact]
    public void CompletedMilestone_FinishedOnTimeOrEarly_DelayIsZero()
    {
        var onTime = Milestone(1, "Completed", Today.AddDays(-20), completedAt: Today.AddDays(-20));
        var early  = Milestone(2, "Completed", Today.AddDays(-20), completedAt: Today.AddDays(-25));
        Assert.Equal(0, ProjectHealthService.ComputeMilestoneDelay(onTime, Today));
        Assert.Equal(0, ProjectHealthService.ComputeMilestoneDelay(early, Today));
    }

    [Fact]
    public void CompletedMilestone_NoCompletedAtRecorded_DelayIsZero()
    {
        var m = Milestone(1, "Completed", Today.AddDays(-20), completedAt: null);
        Assert.Equal(0, ProjectHealthService.ComputeMilestoneDelay(m, Today));
    }

    [Fact]
    public void Milestone_NoDueDate_NeverContributesDelay()
    {
        var m = Milestone(1, "InProgress", due: null);
        Assert.Equal(0, ProjectHealthService.ComputeMilestoneDelay(m, Today));
    }

    // ── BucketStatus — the 14 vs 15 day boundary is the exact bug the
    //    demo audit was built to catch, so it gets its own explicit tests ──

    [Theory]
    [InlineData(0, ProjectHealthStatuses.Green)]
    [InlineData(1, ProjectHealthStatuses.Orange)]
    [InlineData(14, ProjectHealthStatuses.Orange)]
    [InlineData(15, ProjectHealthStatuses.Red)]
    [InlineData(100, ProjectHealthStatuses.Red)]
    public void BucketStatus_MatchesThresholds(int delayDays, string expected)
    {
        Assert.Equal(expected, ProjectHealthService.BucketStatus(delayDays));
    }

    // ── ComputeHealth — project-level aggregation ───────────────────────

    [Fact]
    public void ComputeHealth_UsesMaxDelayAcrossMilestones_NotSum()
    {
        var milestones = new List<MilestoneHealthRow>
        {
            Milestone(1, "InProgress", Today.AddDays(-5)),
            Milestone(2, "InProgress", Today.AddDays(-15)),
            Milestone(3, "Completed", Today.AddDays(-30), completedAt: Today.AddDays(-30)),
        };
        var result = ProjectHealthService.ComputeHealth(projectId: 1, milestones, Today);

        Assert.Equal(15, result.DelayDays); // max(5, 15, 0) — NOT 5+15+0
        Assert.Equal(ProjectHealthStatuses.Red, result.Status);
        Assert.Equal(2, result.RelevantMilestoneId); // the worst-delayed one
    }

    [Fact]
    public void ComputeHealth_NoDelayAnywhere_IsGreen_RelevantIsFirstNonCompleted()
    {
        var milestones = new List<MilestoneHealthRow>
        {
            Milestone(1, "Completed", Today.AddDays(-30), completedAt: Today.AddDays(-30)),
            Milestone(2, "InProgress", Today.AddDays(10)),
            Milestone(3, "NotStarted", Today.AddDays(20)),
        };
        var result = ProjectHealthService.ComputeHealth(projectId: 1, milestones, Today);

        Assert.Equal(0, result.DelayDays);
        Assert.Equal(ProjectHealthStatuses.Green, result.Status);
        Assert.Equal(2, result.RelevantMilestoneId); // first non-completed, not the completed one
    }

    [Fact]
    public void ComputeHealth_EverythingCompleted_RelevantIsTheLastOne()
    {
        var milestones = new List<MilestoneHealthRow>
        {
            Milestone(1, "Completed", Today.AddDays(-30), completedAt: Today.AddDays(-30)),
            Milestone(2, "Completed", Today.AddDays(-20), completedAt: Today.AddDays(-20)),
        };
        var result = ProjectHealthService.ComputeHealth(projectId: 1, milestones, Today);

        Assert.Equal(0, result.DelayDays);
        Assert.Equal(ProjectHealthStatuses.Green, result.Status);
        Assert.Equal(2, result.RelevantMilestoneId);
    }

    [Fact]
    public void ComputeHealth_NoMilestonesAtAll_IsGreenWithNoRelevantMilestone()
    {
        var result = ProjectHealthService.ComputeHealth(projectId: 1, new List<MilestoneHealthRow>(), Today);

        Assert.Equal(0, result.DelayDays);
        Assert.Equal(ProjectHealthStatuses.Green, result.Status);
        Assert.Null(result.RelevantMilestoneId);
    }

    // ── Product boundary: the result must never carry attention-style fields ─
    [Fact]
    public void ProjectHealthResult_HasNoAttentionOrRequestFields()
    {
        // Deliberately brittle by design: if someone adds an OpenRequestCount-
        // style field to ProjectHealthResult, this test should be the thing
        // that makes them stop and re-read the Concept 1 product boundary
        // note before doing it silently.
        var fields = typeof(ProjectHealthResult).GetProperties().Select(p => p.Name).ToList();
        string[] forbidden = { "OpenRequestCount", "OldOpenRequestCount", "MissingMandatorySubmissionCount", "MissingSubmissionCount", "RequestCount" };
        Assert.Empty(fields.Intersect(forbidden));
    }
}
