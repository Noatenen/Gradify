namespace AuthWithAdmin.Server.Services;

using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  ProjectRoadmapService — PHASE 1 of the business-logic consolidation epic
//  (design/business-logic-consolidation-epic.md, Concepts 2 &amp; 3).
//
//  Canonical stage/progress/current-stage calculation. This is the EXACT body
//  of RoadmapStagesController.BuildProgressAsync (already the one place in
//  the app with zero internal divergence across its three callers), relocated
//  so other controllers — MentorController, MilestonesOverviewController —
//  can call the same computation instead of re-deriving their own flat,
//  stage-unaware "current milestone" picks.
//
//  Two progress numbers, permanently and explicitly distinct:
//    • CurrentStageProgressPct — how much of the CURRENT STAGE is done
//      (the "62%-style" number).
//    • OverallProjectProgressPct — average across every stage with linked
//      milestones (the "52%-style" number; promoted from the client-side
//      MentorRoadmapOverviewPage.CalcOverallPct, now computed once, here).
//  Neither replaces the other. Both are on the same DTO so no consumer can
//  present one while meaning the other.
//
//  PHASE 1 SCOPE: new, self-contained. RoadmapStagesController keeps running
//  its own private BuildProgressAsync (identical logic) until Phase 2 swaps
//  it to call this service instead.
// ─────────────────────────────────────────────────────────────────────────────

public class ProjectRoadmapService
{
    private readonly DbRepository _db;
    public ProjectRoadmapService(DbRepository db) => _db = db;

    public async Task<ProjectRoadmapProgressDto> GetProjectRoadmapAsync(int projectId, int academicYearId)
    {
        var stages = (await _db.GetRecordsAsync<StageRow>(@"
            SELECT  Id, Code, Name, Description, DisplayOrder, IsActive,
                    SuggestedStartDate, SuggestedEndDate
            FROM    RoadmapStages
            WHERE   AcademicYearId = @YearId
              AND   IsActive = 1
            ORDER   BY DisplayOrder, Id",
            new { YearId = academicYearId }))?.ToList()
            ?? new List<StageRow>();

        var milestones = (await _db.GetRecordsAsync<ProjectMilestoneRow>(@"
            SELECT  pm.Id              AS ProjectMilestoneId,
                    pm.Status,
                    aym.RoadmapStageId  AS StageId,
                    aym.DueDate         AS DueDate,
                    mt.Title            AS Title,
                    mt.OrderIndex       AS OrderIndex
            FROM    ProjectMilestones     pm
            JOIN    AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            JOIN    MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            WHERE   pm.ProjectId = @P
            ORDER   BY mt.OrderIndex, pm.Id",
            new { P = projectId }))?.ToList()
            ?? new List<ProjectMilestoneRow>();

        return ComputeRoadmapProgress(projectId, academicYearId, stages, milestones, DateTime.UtcNow.Date);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Pure computation — no DB access. Identical to BuildProgressAsync's
    //  body today, plus CurrentStageProgressPct/CurrentStageName/
    //  OverallProjectProgressPct. "today" is a parameter so unit tests can
    //  pin exact UTC-boundary fixtures.
    // ═════════════════════════════════════════════════════════════════════
    public static ProjectRoadmapProgressDto ComputeRoadmapProgress(
        int projectId, int academicYearId,
        List<StageRow> stages, List<ProjectMilestoneRow> milestones, DateTime today)
    {
        var milestonesByStage = milestones
            .Where(m => m.StageId.HasValue)
            .GroupBy(m => m.StageId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new ProjectRoadmapProgressDto
        {
            ProjectId      = projectId,
            AcademicYearId = academicYearId,
            ScheduleStatus = RoadmapScheduleStatuses.NoSchedule,
        };

        StageProgressDto? currentStage = null;
        foreach (var s in stages)
        {
            var linked = milestonesByStage.TryGetValue(s.Id, out var ms) ? ms : new List<ProjectMilestoneRow>();

            int total     = linked.Count;
            int completed = linked.Count(m => string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase));

            bool noLinkedMilestones = total == 0;
            bool fullyComplete      = !noLinkedMilestones && completed == total;

            string stageStatus;
            if (noLinkedMilestones)
                stageStatus = RoadmapStageStatuses.NotApplicable;
            else if (fullyComplete)
                stageStatus = RoadmapStageStatuses.Completed;
            else if (currentStage is null)
                stageStatus = RoadmapStageStatuses.Current;
            else
                stageStatus = RoadmapStageStatuses.Future;

            var stageDto = new StageProgressDto
            {
                StageId            = s.Id,
                Code               = s.Code,
                Name                = s.Name,
                Description         = s.Description,
                DisplayOrder        = s.DisplayOrder,
                SuggestedStartDate  = s.SuggestedStartDate,
                SuggestedEndDate    = s.SuggestedEndDate,
                Status              = stageStatus,
                LinkedMilestoneCount    = total,
                CompletedMilestoneCount = completed,
                ProgressPct = stageStatus switch
                {
                    RoadmapStageStatuses.Completed     => 100,
                    RoadmapStageStatuses.NotApplicable => 0,
                    _ when total > 0                    => (int)Math.Round(100.0 * completed / total),
                    _                                    => 0,
                },
                Milestones = linked.Select(m => new StageMilestoneStateDto
                {
                    ProjectMilestoneId = m.ProjectMilestoneId,
                    Title              = m.Title,
                    Status             = m.Status,
                    DueDate            = m.DueDate,
                    OrderIndex         = m.OrderIndex,
                }).ToList(),
            };

            if (stageStatus == RoadmapStageStatuses.Current)
            {
                currentStage = stageDto;
                result.CurrentStageCode = s.Code;
                result.CurrentStageName = s.Name;
            }

            result.Stages.Add(stageDto);
        }

        // ── Schedule status ──────────────────────────────────────────────
        if (currentStage is not null)
        {
            if (currentStage.SuggestedEndDate is { } end && today > end.Date)
                result.ScheduleStatus = RoadmapScheduleStatuses.Behind;
            else if (currentStage.SuggestedStartDate is { } start && today < start.Date)
                result.ScheduleStatus = RoadmapScheduleStatuses.Ahead;
            else if (currentStage.SuggestedStartDate is null && currentStage.SuggestedEndDate is null)
                result.ScheduleStatus = RoadmapScheduleStatuses.NoSchedule;
            else
                result.ScheduleStatus = RoadmapScheduleStatuses.OnTrack;
        }

        // ── The two explicitly-distinct progress numbers ─────────────────
        result.CurrentStageProgressPct = currentStage?.ProgressPct;

        var contributing = result.Stages.Where(s => s.LinkedMilestoneCount > 0).ToList();
        result.OverallProjectProgressPct = contributing.Count == 0
            ? 0
            : (int)Math.Round(contributing.Average(s => s.ProgressPct));

        // ── Upcoming + overdue (unchanged from BuildProgressAsync) ───────
        var nonComplete = milestones
            .Where(m => !string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var upcomingCandidates = currentStage is not null
            ? nonComplete.Where(m => m.StageId == currentStage.StageId).ToList()
            : new();
        if (upcomingCandidates.Count == 0) upcomingCandidates = nonComplete;

        var upcoming = upcomingCandidates
            .Where(m => m.DueDate.HasValue)
            .OrderBy(m => m.DueDate!.Value)
            .FirstOrDefault();
        if (upcoming is not null)
            result.Upcoming = ToUpcoming(upcoming, today);

        var overdue = nonComplete
            .Where(m => m.DueDate.HasValue && m.DueDate.Value.Date < today)
            .OrderBy(m => m.DueDate!.Value)
            .FirstOrDefault();
        if (overdue is not null)
            result.Overdue = ToUpcoming(overdue, today);

        return result;
    }

    private static UpcomingMilestoneDto ToUpcoming(ProjectMilestoneRow m, DateTime today)
    {
        int? days = m.DueDate.HasValue ? (int?)(m.DueDate.Value.Date - today).TotalDays : null;
        return new UpcomingMilestoneDto
        {
            ProjectMilestoneId = m.ProjectMilestoneId,
            Title              = m.Title,
            DueDate            = m.DueDate,
            DaysUntilDue       = days,
        };
    }

    // ── Row types — structurally identical to RoadmapStagesController's
    //    private StageRow/ProjectMilestoneRow. Duplicated deliberately for
    //    Phase 1 ("extract, don't wire") — Phase 2 removes the controller's
    //    copies once it calls this service instead. ──────────────────────
    public sealed class StageRow
    {
        public int       Id                 { get; set; }
        public string    Code               { get; set; } = "";
        public string    Name               { get; set; } = "";
        public string    Description        { get; set; } = "";
        public int       DisplayOrder       { get; set; }
        public int       IsActive           { get; set; }
        public DateTime? SuggestedStartDate { get; set; }
        public DateTime? SuggestedEndDate   { get; set; }
    }

    public sealed class ProjectMilestoneRow
    {
        public int       ProjectMilestoneId { get; set; }
        public string    Status             { get; set; } = "";
        public int?      StageId            { get; set; }
        public DateTime? DueDate            { get; set; }
        public string    Title              { get; set; } = "";
        public int       OrderIndex         { get; set; }
    }
}
