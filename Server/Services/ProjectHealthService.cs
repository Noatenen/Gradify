namespace AuthWithAdmin.Server.Services;

using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  ProjectHealthService — PHASE 1 of the business-logic consolidation epic
//  (design/business-logic-consolidation-epic.md, Concept 1).
//
//  Canonical schedule-health calculation: is this project's delivery slipping,
//  and by how much? This is the ONLY question this service answers. It is the
//  exact logic already in ProjectHealthController.GetAll, relocated so other
//  controllers can call the same computation instead of re-deriving their own
//  (as LecturerDashboardController's composite score, MilestonesOverviewController's
//  own Overdue/Healthy status, and the raw unwritten Projects.HealthStatus column
//  currently do).
//
//  IMPORTANT — product boundary (explicit, not accidental):
//  ProjectHealthResult carries ONLY schedule-delay facts. It deliberately has
//  no field for open requests, missing submissions, or anything else that
//  might read as "needs attention." A project can be Green here and still have
//  a returned submission or a pending request sitting in TaskUrgencyService —
//  Health and Attention are separate concepts and must never be conflated by
//  sharing a result shape. See TaskUrgencyService for the attention side.
//
//  PHASE 1 SCOPE: this file is new and self-contained. Nothing in the app
//  calls it yet — ProjectHealthController keeps running its own copy of this
//  same logic until Phase 2 switches it to call this service instead.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One milestone's raw facts, as needed to compute delay. Deliberately
/// minimal — no title/team/mentor display fields belong here (those are
/// presentation-layer, added by whichever controller assembles a screen-ready
/// row around this result).</summary>
public class MilestoneHealthRow
{
    public int       ProjectMilestoneId { get; set; }
    public string    Title              { get; set; } = "";
    public string    Status             { get; set; } = "";
    public DateTime? DueDate            { get; set; }
    public DateTime? CompletedAt        { get; set; }
}

/// <summary>Canonical health result for one project. Green/Orange/Red only —
/// see the file header for why request/submission counts are not here.</summary>
public class ProjectHealthResult
{
    public int    ProjectId { get; set; }
    /// <summary>Green | Orange | Red — see ProjectHealthStatuses.</summary>
    public string Status    { get; set; } = ProjectHealthStatuses.Green;
    /// <summary>Max delay, in days, across every milestone. 0 = on schedule.</summary>
    public int    DelayDays { get; set; }
    /// <summary>The milestone driving Status/DelayDays — the worst-delayed one,
    /// or (if nothing is delayed) the first not-yet-completed one, or (if
    /// everything is completed) the last one. Null only when the project has
    /// no milestones at all.</summary>
    public int?       RelevantMilestoneId          { get; set; }
    public string?    RelevantMilestoneTitle        { get; set; }
    public DateTime?  RelevantMilestoneDueDate      { get; set; }
    public DateTime?  RelevantMilestoneCompletedAt  { get; set; }
}

public class ProjectHealthService
{
    private readonly DbRepository _db;
    public ProjectHealthService(DbRepository db) => _db = db;

    /// <summary>Fetches milestones for one project and computes its health.
    /// Same milestone query ProjectHealthController.GetAll uses today
    /// (effective due date = TeamMilestoneDueDateOverrides.OverrideDueDate,
    /// falling back to AcademicYearMilestones.DueDate).</summary>
    public async Task<ProjectHealthResult> GetProjectHealthAsync(int projectId)
    {
        var milestones = await FetchMilestonesAsync(new[] { projectId });
        return ComputeHealth(projectId, milestones.GetValueOrDefault(projectId) ?? new(), DateTime.UtcNow.Date);
    }

    /// <summary>Batch variant — one round trip for many projects (list/fleet
    /// screens: mentor overview, staff overview, project-health page).</summary>
    public async Task<List<ProjectHealthResult>> GetProjectHealthBatchAsync(IEnumerable<int> projectIds)
    {
        var ids = projectIds.Distinct().ToList();
        if (ids.Count == 0) return new();

        var byProject = await FetchMilestonesAsync(ids);
        var today = DateTime.UtcNow.Date;
        return ids
            .Select(id => ComputeHealth(id, byProject.GetValueOrDefault(id) ?? new(), today))
            .ToList();
    }

    private async Task<Dictionary<int, List<MilestoneHealthRow>>> FetchMilestonesAsync(IEnumerable<int> projectIds)
    {
        var idsCsv = string.Join(",", projectIds);
        var rows = await _db.GetRecordsAsync<MilestoneHealthQueryRow>($@"
            SELECT  pm.Id                       AS ProjectMilestoneId,
                    pm.ProjectId                 AS ProjectId,
                    pm.Status,
                    pm.CompletedAt,
                    mt.Title                    AS MilestoneTitle,
                    mt.OrderIndex               AS OrderIndex,
                    COALESCE(mo.OverrideDueDate, aym.DueDate) AS DueDate
            FROM    ProjectMilestones pm
            JOIN    AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            JOIN    MilestoneTemplates     mt  ON mt.Id  = aym.MilestoneTemplateId
            LEFT JOIN Projects p ON p.Id = pm.ProjectId
            LEFT JOIN TeamMilestoneDueDateOverrides mo
                            ON mo.TeamId = p.TeamId AND mo.ProjectMilestoneId = pm.Id
            WHERE   pm.ProjectId IN ({idsCsv})
            ORDER   BY pm.ProjectId, mt.OrderIndex");

        return (rows ?? Enumerable.Empty<MilestoneHealthQueryRow>())
            .GroupBy(r => r.ProjectId)
            .ToDictionary(g => g.Key, g => g.Select(r => new MilestoneHealthRow
            {
                ProjectMilestoneId = r.ProjectMilestoneId,
                Title              = r.MilestoneTitle,
                Status             = r.Status,
                DueDate            = r.DueDate,
                CompletedAt        = r.CompletedAt,
            }).ToList());
    }

    private sealed class MilestoneHealthQueryRow
    {
        public int       ProjectMilestoneId { get; set; }
        public int       ProjectId          { get; set; }
        public string    Status             { get; set; } = "";
        public DateTime? CompletedAt        { get; set; }
        public string    MilestoneTitle     { get; set; } = "";
        public int       OrderIndex         { get; set; }
        public DateTime? DueDate            { get; set; }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Pure computation — no DB access, deterministic given inputs. This is
    //  what Phase 1's unit tests exercise directly, with fixture data and an
    //  explicit "today", so boundary behavior (14 vs 15 days, UTC dates) is
    //  testable without a database.
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Identical logic to ProjectHealthController.ComputeMilestoneDelay
    /// today: completed-late milestones measure CompletedAt−DueDate; open
    /// milestones measure today−DueDate. Floored at 0 either way.</summary>
    public static int ComputeMilestoneDelay(MilestoneHealthRow m, DateTime today)
    {
        if (m.DueDate is null) return 0;
        DateTime due = m.DueDate.Value.Date;

        if (string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            if (m.CompletedAt is null) return 0;
            int days = (m.CompletedAt.Value.Date - due).Days;
            return days > 0 ? days : 0;
        }

        int openDays = (today - due).Days;
        return openDays > 0 ? openDays : 0;
    }

    /// <summary>0 → Green, 1–14 → Orange, &gt;14 → Red. Identical thresholds to
    /// ProjectHealthController today.</summary>
    public static string BucketStatus(int delayDays) => delayDays switch
    {
        0   => ProjectHealthStatuses.Green,
        <= 14 => ProjectHealthStatuses.Orange,
        _     => ProjectHealthStatuses.Red,
    };

    public static ProjectHealthResult ComputeHealth(int projectId, List<MilestoneHealthRow> milestones, DateTime today)
    {
        int maxDelay = 0;
        MilestoneHealthRow? worstDelayed = null;
        foreach (var m in milestones)
        {
            int delay = ComputeMilestoneDelay(m, today);
            if (delay > maxDelay)
            {
                maxDelay = delay;
                worstDelayed = m;
            }
        }

        MilestoneHealthRow? relevant = worstDelayed
            ?? milestones.FirstOrDefault(m => !string.Equals(m.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            ?? milestones.LastOrDefault();

        return new ProjectHealthResult
        {
            ProjectId                     = projectId,
            Status                        = BucketStatus(maxDelay),
            DelayDays                     = maxDelay,
            RelevantMilestoneId           = relevant?.ProjectMilestoneId,
            RelevantMilestoneTitle        = relevant?.Title,
            RelevantMilestoneDueDate      = relevant?.DueDate,
            RelevantMilestoneCompletedAt  = relevant?.CompletedAt,
        };
    }
}
