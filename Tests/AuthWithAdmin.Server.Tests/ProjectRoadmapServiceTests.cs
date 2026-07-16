using AuthWithAdmin.Server.Services;
using AuthWithAdmin.Shared.AuthSharedModels;
using StageRow = AuthWithAdmin.Server.Services.ProjectRoadmapService.StageRow;
using MilestoneRow = AuthWithAdmin.Server.Services.ProjectRoadmapService.ProjectMilestoneRow;

namespace AuthWithAdmin.Server.Tests;

public class ProjectRoadmapServiceTests
{
    private static readonly DateTime Today = new(2026, 7, 16);

    private static StageRow Stage(int id, int order, DateTime? start = null, DateTime? end = null) => new()
    {
        Id = id, Code = $"S{id}", Name = $"Stage {id}", Description = "", DisplayOrder = order,
        IsActive = 1, SuggestedStartDate = start, SuggestedEndDate = end,
    };

    private static MilestoneRow Milestone(int id, int stageId, string status, DateTime? due = null) => new()
    {
        ProjectMilestoneId = id, StageId = stageId, Status = status, DueDate = due, Title = $"M{id}", OrderIndex = id,
    };

    private static List<MilestoneRow> LinkedSet(int stageId, int total, int completed, int startId)
    {
        var list = new List<MilestoneRow>();
        for (int i = 0; i < total; i++)
            list.Add(Milestone(startId + i, stageId, i < completed ? "Completed" : "InProgress"));
        return list;
    }

    // ── The 62-vs-52 distinction: current-stage progress and whole-project
    //    progress must be independently correct and are expected to differ ──

    [Fact]
    public void CurrentStageProgress_And_OverallProgress_AreComputedIndependently_AndCanDiffer()
    {
        var stages = new List<StageRow> { Stage(1, 0), Stage(2, 1), Stage(3, 2) };
        var milestones = new List<MilestoneRow>();
        milestones.AddRange(LinkedSet(stageId: 1, total: 2, completed: 2, startId: 1));   // Stage 1: 100% → Completed
        milestones.AddRange(LinkedSet(stageId: 2, total: 8, completed: 5, startId: 10));  // Stage 2: 62.5% → Current
        milestones.AddRange(LinkedSet(stageId: 3, total: 6, completed: 0, startId: 20));  // Stage 3: 0% → Future

        var result = ProjectRoadmapService.ComputeRoadmapProgress(1, 1, stages, milestones, Today);

        Assert.Equal(RoadmapStageStatuses.Current, result.Stages.Single(s => s.StageId == 2).Status);
        Assert.Equal(62, result.CurrentStageProgressPct);      // the "62%-style" number
        Assert.Equal(54, result.OverallProjectProgressPct);    // the "52%-style" number — avg(100, 62, 0)
        Assert.NotEqual(result.CurrentStageProgressPct, result.OverallProjectProgressPct);
    }

    [Fact]
    public void ProgressPct_UsesBankersRounding_ForExactHalves()
    {
        var stages = new List<StageRow> { Stage(1, 0) };
        // 5 of 8 completed = 62.5 → rounds to 62 (nearest even), not 63.
        var milestones = LinkedSet(stageId: 1, total: 8, completed: 5, startId: 1);

        var result = ProjectRoadmapService.ComputeRoadmapProgress(1, 1, stages, milestones, Today);

        Assert.Equal(62, result.Stages.Single().ProgressPct);
    }

    [Fact]
    public void StageWithNoLinkedMilestones_IsNotApplicable_NotSelectedAsCurrent_AndExcludedFromOverallAverage()
    {
        var stages = new List<StageRow> { Stage(1, 0), Stage(2, 1), Stage(3, 2) };
        var milestones = new List<MilestoneRow>();
        milestones.AddRange(LinkedSet(stageId: 1, total: 2, completed: 2, startId: 1));  // 100% Completed
        // Stage 2 has zero linked milestones — must be skipped for "current" selection.
        milestones.AddRange(LinkedSet(stageId: 3, total: 6, completed: 3, startId: 10)); // 50%

        var result = ProjectRoadmapService.ComputeRoadmapProgress(1, 1, stages, milestones, Today);

        var stage2 = result.Stages.Single(s => s.StageId == 2);
        Assert.Equal(RoadmapStageStatuses.NotApplicable, stage2.Status);
        Assert.Equal("S3", result.CurrentStageCode); // stage 3, not the zero-milestone stage 2
        Assert.Equal(50, result.CurrentStageProgressPct);
        // Overall must average only contributing stages: (100 + 50) / 2 = 75, not /3.
        Assert.Equal(75, result.OverallProjectProgressPct);
    }

    [Fact]
    public void AllStagesComplete_NoCurrentStage_OverallProgressIs100()
    {
        var stages = new List<StageRow> { Stage(1, 0), Stage(2, 1) };
        var milestones = new List<MilestoneRow>();
        milestones.AddRange(LinkedSet(stageId: 1, total: 2, completed: 2, startId: 1));
        milestones.AddRange(LinkedSet(stageId: 2, total: 3, completed: 3, startId: 10));

        var result = ProjectRoadmapService.ComputeRoadmapProgress(1, 1, stages, milestones, Today);

        Assert.Null(result.CurrentStageCode);
        Assert.Null(result.CurrentStageProgressPct);
        Assert.Equal(100, result.OverallProjectProgressPct);
    }

    [Fact]
    public void NoStagesOrMilestonesConfigured_OverallProgressIsZero_NotCurrentStage()
    {
        var result = ProjectRoadmapService.ComputeRoadmapProgress(1, 1, new List<StageRow>(), new List<MilestoneRow>(), Today);

        Assert.Null(result.CurrentStageProgressPct);
        Assert.Equal(0, result.OverallProjectProgressPct);
        Assert.Equal(RoadmapScheduleStatuses.NoSchedule, result.ScheduleStatus);
    }

    // ── Schedule status vs current stage's suggested date range ──────────

    [Fact]
    public void ScheduleStatus_Behind_WhenTodayPastCurrentStageEndDate()
    {
        var stages = new List<StageRow> { Stage(1, 0, start: Today.AddDays(-30), end: Today.AddDays(-1)) };
        var milestones = LinkedSet(stageId: 1, total: 4, completed: 1, startId: 1);

        var result = ProjectRoadmapService.ComputeRoadmapProgress(1, 1, stages, milestones, Today);
        Assert.Equal(RoadmapScheduleStatuses.Behind, result.ScheduleStatus);
    }

    [Fact]
    public void ScheduleStatus_OnTrack_WhenTodayEqualsCurrentStageEndDate()
    {
        // today > end.Date is the Behind condition — equality must NOT be Behind.
        var stages = new List<StageRow> { Stage(1, 0, start: Today.AddDays(-10), end: Today) };
        var milestones = LinkedSet(stageId: 1, total: 4, completed: 1, startId: 1);

        var result = ProjectRoadmapService.ComputeRoadmapProgress(1, 1, stages, milestones, Today);
        Assert.Equal(RoadmapScheduleStatuses.OnTrack, result.ScheduleStatus);
    }

    [Fact]
    public void ScheduleStatus_Ahead_WhenTodayBeforeCurrentStageStartDate()
    {
        var stages = new List<StageRow> { Stage(1, 0, start: Today.AddDays(5), end: Today.AddDays(20)) };
        var milestones = LinkedSet(stageId: 1, total: 4, completed: 1, startId: 1);

        var result = ProjectRoadmapService.ComputeRoadmapProgress(1, 1, stages, milestones, Today);
        Assert.Equal(RoadmapScheduleStatuses.Ahead, result.ScheduleStatus);
    }

    [Fact]
    public void ScheduleStatus_NoSchedule_WhenCurrentStageHasNoDates()
    {
        var stages = new List<StageRow> { Stage(1, 0, start: null, end: null) };
        var milestones = LinkedSet(stageId: 1, total: 4, completed: 1, startId: 1);

        var result = ProjectRoadmapService.ComputeRoadmapProgress(1, 1, stages, milestones, Today);
        Assert.Equal(RoadmapScheduleStatuses.NoSchedule, result.ScheduleStatus);
    }

    // ── Upcoming / overdue milestone picks ────────────────────────────────

    [Fact]
    public void Overdue_PicksEarliestPastDueNonCompletedMilestone()
    {
        var stages = new List<StageRow> { Stage(1, 0) };
        var milestones = new List<MilestoneRow>
        {
            Milestone(1, 1, "InProgress", Today.AddDays(-10)),
            Milestone(2, 1, "InProgress", Today.AddDays(-3)),
            Milestone(3, 1, "Completed", Today.AddDays(-20)),
        };

        var result = ProjectRoadmapService.ComputeRoadmapProgress(1, 1, stages, milestones, Today);

        Assert.NotNull(result.Overdue);
        Assert.Equal(1, result.Overdue!.ProjectMilestoneId);
    }

    [Fact]
    public void Upcoming_PicksEarliestFutureDueMilestoneWithinCurrentStage()
    {
        var stages = new List<StageRow> { Stage(1, 0), Stage(2, 1) };
        var milestones = new List<MilestoneRow>
        {
            Milestone(1, 1, "InProgress", Today.AddDays(5)),
            Milestone(2, 1, "InProgress", Today.AddDays(2)),
            Milestone(3, 2, "InProgress", Today.AddDays(1)), // earlier, but in a future stage
        };

        var result = ProjectRoadmapService.ComputeRoadmapProgress(1, 1, stages, milestones, Today);

        Assert.NotNull(result.Upcoming);
        Assert.Equal(2, result.Upcoming!.ProjectMilestoneId); // earliest WITHIN the current stage, not globally
    }
}
