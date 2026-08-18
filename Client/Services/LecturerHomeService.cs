using AuthWithAdmin.Shared.AuthSharedModels;
using System.Net.Http.Json;

namespace AuthWithAdmin.Client.Services;

// ─────────────────────────────────────────────────────────────────────────────
//  LecturerWorkspace — composed daily snapshot for the Lecturer Home (בית).
//
//  Mirrors MentorWorkspace: one record assembled from existing endpoints,
//  computed projections live here so the page never re-derives them.
//
//  Data sources (all existing, no new server endpoints):
//    • GET /api/dashboard?scope=lecturer        → DashboardOverviewDto
//    • GET /api/project-requests               → List<ProjectRequestRowDto>
//    • GET /api/task-submissions/pending-mentor-approvals
//                                             → List<PendingMentorApprovalRowDto>
// ─────────────────────────────────────────────────────────────────────────────

public record LecturerWorkspace(
    DashboardOverviewDto Overview,
    List<ProjectRequestRowDto> AllRequests,
    List<PendingMentorApprovalRowDto> PendingApprovals)
{
    public static LecturerWorkspace Empty { get; } = new(
        new DashboardOverviewDto { Summary = new(), Charts = new(), Projects = new() },
        new List<ProjectRequestRowDto>(),
        new List<PendingMentorApprovalRowDto>());

    // ── Project health counts ────────────────────────────────────────────
    public int OnTrackCount   => Overview.Charts.HealthHealthy;
    public int AttentionCount => Overview.Charts.HealthAttention;
    public int AtRiskCount    => Overview.Charts.HealthAtRisk;

    // ── "ממתין להחלטתך" action counts ───────────────────────────────────

    /// <summary>Extension requests escalated to lecturer for a final decision.</summary>
    public List<ProjectRequestRowDto> PendingDecisionRequests =>
        AllRequests
            .Where(r => r.Status == RequestStatuses.PendingLecturerDecision)
            .OrderBy(r => r.UpdatedAt)   // oldest first → longest-waiting most urgent
            .ToList();

    /// <summary>Submissions where a mentor explicitly escalated to the Lecturer
    /// for a decision/override (EscalatedToLecturer=1). These are the SAME
    /// records that appear as "awaiting_lecturer" on the Submissions page, so
    /// the "הגשות לאישורך" count on Lecturer Home always matches the
    /// "לאישור מרצה" KPI on the Submissions page.</summary>
    public List<PendingMentorApprovalRowDto> PendingSubmissionApprovals =>
        PendingApprovals
            .Where(s => s.EscalatedToLecturer)
            .OrderByDescending(s => s.DaysWaiting)
            .ToList();

    // ── At-risk projects for the attention feed ──────────────────────────
    public List<DashboardProjectRowDto> AtRiskProjects =>
        Overview.Projects
            .Where(p => p.HealthBucket == HealthBuckets.AtRisk)
            .OrderBy(p => p.HealthScore)
            .ToList();

    // ── Upcoming course events ───────────────────────────────────────────
    /// <summary>Projects with a future milestone (any status), sorted by
    /// nearest due date. Used to build the "מועדים קרובים בקורס" list.
    /// Uses NextMilestoneDueDate rather than CurrentMilestoneDueDate because
    /// the current milestone is often an overdue InProgress one — the next
    /// field is the genuine upcoming deadline.</summary>
    public IEnumerable<DashboardProjectRowDto> UpcomingMilestones =>
        Overview.Projects
            .Where(p => p.NextMilestoneDueDate is not null
                     && p.NextMilestoneDueDate >= DateTime.Today)
            .OrderBy(p => p.NextMilestoneDueDate)
            .Take(4);

    // ── Recent activity ──────────────────────────────────────────────────
    /// <summary>Most-recently-updated requests — a real-data approximation of
    /// the "פעילות מהותית אחרונה" feed. Ordered newest-first.</summary>
    public IEnumerable<ProjectRequestRowDto> RecentActivity =>
        AllRequests
            .OrderByDescending(r => r.UpdatedAt)
            .Take(5);
}

// ─────────────────────────────────────────────────────────────────────────────

public interface ILecturerHomeService
{
    /// <summary>Fetches and composes the lecturer's daily workspace.
    /// Returns LecturerWorkspace.Empty on total failure (never throws).</summary>
    Task<LecturerWorkspace> LoadAsync();
}

public class LecturerHomeService : ILecturerHomeService
{
    private readonly IDashboardOverviewService       _dashboard;
    private readonly IProjectRequestsService         _requests;
    private readonly IPendingMentorApprovalsService  _approvals;

    public LecturerHomeService(
        IDashboardOverviewService      dashboard,
        IProjectRequestsService        requests,
        IPendingMentorApprovalsService  approvals)
    {
        _dashboard = dashboard;
        _requests  = requests;
        _approvals = approvals;
    }

    public async Task<LecturerWorkspace> LoadAsync()
    {
        // All three are independent — fire in parallel.
        var ovTask  = _dashboard.GetAsync("lecturer");
        var reqTask = _requests.GetAllAsync();
        var appTask = _approvals.GetPendingAsync();

        await Task.WhenAll(ovTask, reqTask, appTask);

        var overview  = ovTask.Result  ?? LecturerWorkspace.Empty.Overview;
        var requests  = reqTask.Result ?? new List<ProjectRequestRowDto>();
        var approvals = appTask.Result ?? new List<PendingMentorApprovalRowDto>();

        return new LecturerWorkspace(overview, requests, approvals);
    }
}
