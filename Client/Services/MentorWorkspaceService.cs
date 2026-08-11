using AuthWithAdmin.Client.Pages.Mentor;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

/// <summary>
/// One snapshot of everything the cross-project mentor screens read.
///
/// <para>בית and המשימות שלי are two views over the SAME four collections —
/// the projects the mentor guides, the submissions awaiting their review, the
/// requests on their projects, and their own personal reminders. Fetching that
/// set in one place means the two pages cannot disagree about a count, and the
/// joins the design needs (a submission's team name, a project's open-request
/// count) are computed once instead of twice.</para>
/// </summary>
public sealed record MentorWorkspace(
    IReadOnlyList<MentorProjectSummaryDto>   Projects,
    IReadOnlyList<MentorPendingSubmissionDto> PendingSubmissions,
    IReadOnlyList<ProjectRequestRowDto>       Requests,
    IReadOnlyList<PersonalTaskDto>            PersonalTasks)
{
    public static readonly MentorWorkspace Empty =
        new(Array.Empty<MentorProjectSummaryDto>(),
            Array.Empty<MentorPendingSubmissionDto>(),
            Array.Empty<ProjectRequestRowDto>(),
            Array.Empty<PersonalTaskDto>());

    /// <summary>Team name for a project id — the design labels every submission
    /// row with its team ("ניווט לעיוורים · צוות רועי כהן"), but the submissions
    /// endpoint returns only the project. Resolved from the projects list the
    /// same snapshot already holds rather than by adding a server field.</summary>
    public string? TeamNameFor(int projectId) =>
        Projects.FirstOrDefault(p => p.Id == projectId)?.TeamName;

    /// <summary>Open (non-terminal) requests filed against one project. Real
    /// data, grouped client-side — the mentor projects endpoint has no
    /// per-project request count of its own.</summary>
    public int OpenRequestsFor(int projectId) =>
        Requests.Count(r => r.ProjectId == projectId
                            && r.Status is not (RequestStatuses.Resolved or RequestStatuses.Closed));
}

public interface IMentorWorkspaceService
{
    /// <summary>Every dated thing across the mentor's projects, for יומן ותכנון.
    /// Costs one call per project on top of the snapshot, because milestone and
    /// deliverable dates live in the per-project detail payload and no
    /// cross-project endpoint returns them — see MentorCalendarPage.</summary>
    Task<IReadOnlyList<MentorCalendarEvent>> LoadCalendarAsync();

    /// <summary>Loads all four collections concurrently. Never throws and never
    /// returns null — each underlying service already swallows transport errors
    /// and yields an empty list, so a partial outage degrades one section of a
    /// page rather than blanking the whole screen.</summary>
    Task<MentorWorkspace> LoadAsync();
}

public class MentorWorkspaceService : IMentorWorkspaceService
{
    private readonly IMentorProjectsService _projects;
    private readonly IProjectRequestsService _requests;
    private readonly IPersonalTasksService _personal;

    public MentorWorkspaceService(
        IMentorProjectsService projects,
        IProjectRequestsService requests,
        IPersonalTasksService personal)
    {
        _projects = projects;
        _requests = requests;
        _personal = personal;
    }

    public async Task<MentorWorkspace> LoadAsync()
    {
        // Concurrent, not sequential: these are four independent GETs and the
        // mentor shell should not pay for them one after another.
        var projectsTask    = _projects.GetProjectsAsync();
        var submissionsTask = _projects.GetPendingSubmissionsAsync();
        var requestsTask    = _requests.GetAllAsync();
        var personalTask    = _personal.GetAsync();

        await Task.WhenAll(projectsTask, submissionsTask, requestsTask, personalTask);

        return new MentorWorkspace(
            await projectsTask,
            await submissionsTask,
            // GetAllAsync is the only one that can answer null (it returns
            // null on a non-success response rather than an empty list).
            // Server-side this endpoint is already scoped to the caller's own
            // projects for a mentor-only user, so no client-side filter is
            // needed or wanted here.
            await requestsTask ?? new List<ProjectRequestRowDto>(),
            await personalTask);
    }

    public async Task<IReadOnlyList<MentorCalendarEvent>> LoadCalendarAsync()
    {
        var snapshot = await LoadAsync();
        var events = new List<MentorCalendarEvent>();

        // ── Personal reminders. The only entries with no project. ──
        foreach (var t in snapshot.PersonalTasks.Where(t => !t.IsDone && t.DueDate is not null))
        {
            events.Add(new MentorCalendarEvent(
                Id: $"pt-{t.Id}", Type: MentorEventType.PersonalTask,
                Date: t.DueDate!.Value, Title: t.Title,
                ProjectId: null, ProjectTitle: null, TeamName: null,
                Detail: t.Description, Href: "/mentor/tasks?focus=personal"));
        }

        // ── Submissions already sitting with the mentor. Dated by ARRIVAL,
        //    which is the only real date there is: nothing stores a
        //    review-by deadline. ──
        foreach (var r in snapshot.PendingSubmissions)
        {
            events.Add(new MentorCalendarEvent(
                Id: $"rv-{r.SubmissionId}", Type: MentorEventType.Review,
                Date: r.SubmittedAt, Title: $"התקבלה לבדיקה — {r.TaskTitle}",
                ProjectId: r.ProjectId, ProjectTitle: r.ProjectTitle,
                TeamName: snapshot.TeamNameFor(r.ProjectId),
                Detail: string.IsNullOrWhiteSpace(r.MilestoneTitle) ? null : $"אבן דרך: {r.MilestoneTitle}",
                Href: $"/mentor/projects/{r.ProjectId}"));
        }

        // ── Milestones and dated deliverables, per project.
        //    Fetched concurrently: sequential awaits here would make the
        //    calendar's load time scale with the mentor's caseload. ──
        var details = await Task.WhenAll(
            snapshot.Projects.Select(p => _projects.GetProjectDetailAsync(p.Id)));

        foreach (var detail in details)
        {
            if (detail is null) continue;
            var team = snapshot.TeamNameFor(detail.Id) ?? detail.TeamName;

            foreach (var m in detail.Milestones)
            {
                if (m.DueDate is not null)
                {
                    events.Add(new MentorCalendarEvent(
                        Id: $"ms-{detail.Id}-{m.ProjectMilestoneId}", Type: MentorEventType.Milestone,
                        Date: m.DueDate.Value, Title: m.Title,
                        ProjectId: detail.Id, ProjectTitle: detail.Title, TeamName: team,
                        Detail: $"{m.CompletedTasks}/{m.TotalTasks} משימות הושלמו",
                        Href: $"/mentor/projects/{detail.Id}"));
                }

                // A dated deliverable the team owes. Only IsSubmission tasks —
                // an ordinary project task is the team's internal business and
                // would drown the mentor's planning view.
                foreach (var t in m.Tasks.Where(t => t.IsSubmission && t.DueDate is not null))
                {
                    events.Add(new MentorCalendarEvent(
                        Id: $"sb-{detail.Id}-{t.Id}", Type: MentorEventType.Submission,
                        Date: t.DueDate!.Value, Title: t.Title,
                        ProjectId: detail.Id, ProjectTitle: detail.Title, TeamName: team,
                        Detail: string.IsNullOrWhiteSpace(m.Title) ? null : $"אבן דרך: {m.Title}",
                        Href: $"/mentor/projects/{detail.Id}"));
                }
            }
        }

        return events.OrderBy(e => e.Date).ToList();
    }
}
