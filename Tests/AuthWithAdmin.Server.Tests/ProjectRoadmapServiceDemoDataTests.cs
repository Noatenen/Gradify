using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Server.Services;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.Extensions.Configuration;

namespace AuthWithAdmin.Server.Tests;

// ─────────────────────────────────────────────────────────────────────────────
//  Slice 1 verification — reads the REAL seeded demo database (read-only
//  SELECTs only, no mutation) and exercises the exact same code path
//  RoadmapStagesController now calls (ProjectRoadmapService.GetProjectRoadmapAsync),
//  for Project 9001 ("Motiva — Final Project Management Platform", the
//  project used throughout the demo-story walkthroughs).
//
//  This is the project where the original consistency audit found the
//  "62% vs 52%" discrepancy — current-stage progress and whole-project
//  progress legitimately differing for the same project. This test pins
//  both numbers against the live demo data so a future change can't
//  silently collapse them back into looking the same, or drift them apart
//  for the wrong reason.
// ─────────────────────────────────────────────────────────────────────────────

public class ProjectRoadmapServiceDemoDataTests
{
    private const int DemoProjectId      = 129;  // ProjectNumber 9001
    private const int DemoAcademicYearId = 1;

    private static ProjectRoadmapService BuildServiceAgainstRealDemoDb()
    {
        // Tests/AuthWithAdmin.Server.Tests/bin/Debug/net10.0 -> ../../../../../Server/FinalProjectDB.db
        string dbPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Server", "FinalProjectDB.db");
        dbPath = Path.GetFullPath(dbPath);
        if (!File.Exists(dbPath))
            throw new FileNotFoundException(
                $"Seeded demo database not found at {dbPath}. These tests verify Slice 1 against the real Motiva demo dataset (Project 9001) and require it to exist.", dbPath);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={dbPath}",
            })
            .Build();

        return new ProjectRoadmapService(new DbRepository(config));
    }

    [Fact]
    public async Task DemoProject9001_CurrentStage_IsSpecification()
    {
        var service = BuildServiceAgainstRealDemoDb();
        var result = await service.GetProjectRoadmapAsync(DemoProjectId, DemoAcademicYearId);

        Assert.Equal("Specification", result.CurrentStageCode);
        Assert.Equal("אפיון", result.CurrentStageName);
    }

    [Fact]
    public async Task DemoProject9001_CurrentStageProgress_Is62Percent()
    {
        // Specification stage: 8 linked milestones, 5 Completed → 5/8 = 62.5%
        // → rounds to 62 (banker's rounding, nearest even). This is the
        // "62%-style" number from the original audit.
        var service = BuildServiceAgainstRealDemoDb();
        var result = await service.GetProjectRoadmapAsync(DemoProjectId, DemoAcademicYearId);

        Assert.Equal(62, result.CurrentStageProgressPct);
    }

    [Fact]
    public async Task DemoProject9001_OverallProgress_Is52Percent_NotConfusedWithCurrentStage()
    {
        // Selection, Kickoff, Definition: 100% each (fully completed, 1
        // milestone apiece). Specification: 62%. Development, Evaluation,
        // SubmissionGrading: 0% each (not yet started). All 7 stages have
        // linked milestones, so all 7 contribute:
        //   (100 + 100 + 100 + 62 + 0 + 0 + 0) / 7 = 51.71... → 52.
        // This is the exact "52%-style" number from the original audit —
        // deliberately different from CurrentStageProgressPct (62) for the
        // same project, proving the two are independently computed.
        var service = BuildServiceAgainstRealDemoDb();
        var result = await service.GetProjectRoadmapAsync(DemoProjectId, DemoAcademicYearId);

        Assert.Equal(52, result.OverallProjectProgressPct);
        Assert.NotEqual(result.CurrentStageProgressPct, result.OverallProjectProgressPct);
    }

    [Fact]
    public async Task DemoProject9001_AllSevenStages_HaveLinkedMilestones_NoneDistortTheAverage()
    {
        // Every stage in the demo cycle has at least one linked milestone —
        // confirms the "stages with zero linked milestones don't distort the
        // overall percentage" rule isn't accidentally masked by this
        // particular project (it's separately covered with a synthetic
        // zero-milestone stage in ProjectRoadmapServiceTests).
        var service = BuildServiceAgainstRealDemoDb();
        var result = await service.GetProjectRoadmapAsync(DemoProjectId, DemoAcademicYearId);

        Assert.Equal(7, result.Stages.Count);
        Assert.All(result.Stages, s => Assert.True(s.LinkedMilestoneCount > 0));
    }

    [Fact]
    public async Task DemoProject9001_StageStatuses_MatchExpectedTimeline()
    {
        var service = BuildServiceAgainstRealDemoDb();
        var result = await service.GetProjectRoadmapAsync(DemoProjectId, DemoAcademicYearId);

        string StatusOf(string code) => result.Stages.Single(s => s.Code == code).Status;

        Assert.Equal(RoadmapStageStatuses.Completed, StatusOf("Selection"));
        Assert.Equal(RoadmapStageStatuses.Completed, StatusOf("Kickoff"));
        Assert.Equal(RoadmapStageStatuses.Completed, StatusOf("Definition"));
        Assert.Equal(RoadmapStageStatuses.Current,   StatusOf("Specification"));
        Assert.Equal(RoadmapStageStatuses.Future,     StatusOf("Development"));
        Assert.Equal(RoadmapStageStatuses.Future,     StatusOf("Evaluation"));
        Assert.Equal(RoadmapStageStatuses.Future,     StatusOf("SubmissionGrading"));
    }
}
