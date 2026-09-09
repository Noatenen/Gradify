using AuthWithAdmin.Client.Pages.Mentor;
using AuthWithAdmin.Shared.AuthSharedModels;
using System.Net.Http.Json;

namespace AuthWithAdmin.Client.Services;

/// <summary>
/// One snapshot of everything the cross-project mentor screens read.
///
/// <para><b>Attention is the centre of this record.</b> בית, המשימות שלי and
/// יומן ותכנון all read <see cref="Attention"/> for anything that involves
/// "what is waiting on me", "how long has it waited" and "how urgent is it".
/// None of them filters by status, computes an age or applies a threshold any
/// more — the server did all three, once, in MentorAttentionService, and the
/// daily digest reads the same model from the same place. That is what stops an
/// email and a screen disagreeing about a count.</para>
///
/// <para>The other three collections remain because they answer questions
/// attention does not: the projects the mentor guides, every request on those
/// projects (not just the ones awaiting the mentor), and the mentor's complete
/// personal-task list including undated and completed rows.</para>
/// </summary>
public sealed record MentorWorkspace(
    MentorAttentionDto                      Attention,
    IReadOnlyList<MentorProjectSummaryDto>  Projects,
    IReadOnlyList<ProjectRequestRowDto>     Requests,
    IReadOnlyList<PersonalTaskDto>          PersonalTasks)
{
    public static readonly MentorWorkspace Empty =
        new(new MentorAttentionDto(),
            Array.Empty<MentorProjectSummaryDto>(),
            Array.Empty<ProjectRequestRowDto>(),
            Array.Empty<PersonalTaskDto>());

    // ── Attention views ─────────────────────────────────────────────────────
    // Items arrives already in canonical worst-first order (NeedsAttention →
    // Waiting → New, oldest first inside each), and filtering preserves order,
    // so these are projections and never re-sorts.

    /// <summary>Submissions awaiting this mentor's review.</summary>
    public IReadOnlyList<MentorAttentionItemDto> Reviews =>
        Attention.Items.Where(i => i.Kind == MentorAttentionKind.Submission).ToList();

    /// <summary>Requests awaiting this mentor's recommendation.</summary>
    public IReadOnlyList<MentorAttentionItemDto> AwaitingMe =>
        Attention.Items.Where(i => i.Kind == MentorAttentionKind.Request).ToList();

    /// <summary>The single most pressing item across every kind — the first
    /// entry of an already worst-first list.</summary>
    public MentorAttentionItemDto? TopPriority => Attention.Items.FirstOrDefault();

    /// <summary>Team name for a project id — used where a payload names the
    /// project but not the team. Attention items already carry TeamName, so this
    /// is only for the project-level surfaces.</summary>
    public string? TeamNameFor(int projectId) =>
        Projects.FirstOrDefault(p => p.Id == projectId)?.TeamName;

    /// <summary>Open (non-terminal) requests filed against one project.
    /// Deliberately NOT the same question as <see cref="AwaitingMe"/>: this
    /// counts everything still live on the project, including requests the
    /// lecturer or the team currently holds.</summary>
    public int OpenRequestsFor(int projectId) =>
        Requests.Count(r => r.ProjectId == projectId && IsOpen(r));

    /// <summary>The same open requests, as rows rather than as a count — for
    /// the surfaces that word WHICH request is waiting rather than how many.
    /// One predicate serves both so a project cannot show "בקשה פתוחה" beside
    /// a count of zero.</summary>
    public IReadOnlyList<ProjectRequestRowDto> OpenRequestList(int projectId) =>
        Requests.Where(r => r.ProjectId == projectId && IsOpen(r)).ToList();

    private static bool IsOpen(ProjectRequestRowDto r) =>
        r.Status is not (RequestStatuses.Resolved or RequestStatuses.Closed);
}

public interface IMentorWorkspaceService
{
    /// <summary>
    /// Every dated thing across the mentor's projects, for יומן ותכנון.
    ///
    /// <para>Takes the snapshot rather than fetching it: the calendar page
    /// already holds one (it needs Projects for the filter chips), and loading a
    /// second copy here meant every visit fetched attention, requests and
    /// personal tasks TWICE. Composing from what the caller already has is what
    /// makes the page cost one snapshot instead of two.</para>
    ///
    /// <para>Still costs one call per project on top of that, because milestone
    /// and deliverable dates live only in the per-project detail payload and no
    /// cross-project endpoint returns them — see MentorCalendarPage.</para>
    ///
    /// <para><paramref name="details"/> lets a caller that ALREADY holds those
    /// payloads hand them over instead of paying for them twice. The project
    /// workspace at <c>/mentor/projects/{id}</c> loads its own detail and then
    /// wants this exact dated view of it; without this it would either fetch the
    /// same endpoint a second time or grow a second copy of the rules below,
    /// and the second copy is the one that eventually disagrees. Omitted (the
    /// default), the method fetches as it always has and every existing caller
    /// is unaffected.</para>
    /// </summary>
    Task<IReadOnlyList<MentorCalendarEvent>> BuildCalendarAsync(
        MentorWorkspace snapshot,
        IReadOnlyList<MentorProjectDetailDto>? details = null);

    /// <summary>Loads the snapshot. Never throws and never returns null — each
    /// underlying call already swallows transport errors and yields an empty
    /// result, so a partial outage degrades one section of a page rather than
    /// blanking the whole screen.</summary>
    Task<MentorWorkspace> LoadAsync();
}

public class MentorWorkspaceService : IMentorWorkspaceService
{
    private readonly IMentorAttentionService _attention;
    private readonly IMentorProjectsService _projects;
    private readonly IProjectRequestsService _requests;
    private readonly IPersonalTasksService _personal;

    public MentorWorkspaceService(
        IMentorAttentionService attention,
        IMentorProjectsService projects,
        IProjectRequestsService requests,
        IPersonalTasksService personal)
    {
        _attention = attention;
        _projects  = projects;
        _requests  = requests;
        _personal  = personal;
    }

    public async Task<MentorWorkspace> LoadAsync()
    {
        // Concurrent, not sequential: four independent GETs, and the mentor
        // shell should not pay for them one after another.
        var attentionTask = _attention.GetAsync();
        var projectsTask  = _projects.GetProjectsAsync();
        var requestsTask  = _requests.GetAllAsync();
        var personalTask  = _personal.GetAsync();

        await Task.WhenAll(attentionTask, projectsTask, requestsTask, personalTask);

        return new MentorWorkspace(
            await attentionTask,
            await projectsTask,
            // GetAllAsync is the only one that can answer null (it returns null
            // on a non-success response rather than an empty list). Server-side
            // this endpoint is already scoped to the caller's own projects for a
            // mentor-only user, so no client-side filter is needed or wanted.
            await requestsTask ?? new List<ProjectRequestRowDto>(),
            await personalTask);
    }

    public async Task<IReadOnlyList<MentorCalendarEvent>> BuildCalendarAsync(
        MentorWorkspace snapshot,
        IReadOnlyList<MentorProjectDetailDto>? details = null)
    {
        var events = new List<MentorCalendarEvent>();

        // ── Personal reminders. The only entries with no project. ──
        //
        //    Read from the FULL personal list rather than from Attention:
        //    attention holds only what is due today or earlier, and a planner
        //    whose personal tasks vanish until their due date is not a planner.
        foreach (var t in snapshot.PersonalTasks.Where(t => !t.IsDone && t.DueDate is not null))
        {
            // Project context only when the SERVER supplied it — it resolves the
            // association through ProjectMentors, so a task pointing at a project
            // the mentor has lost arrives with these null and renders as
            // "משימה אישית". A stale or deleted ProjectId therefore degrades to
            // no context instead of breaking the row or naming a project the
            // mentor may not see.
            bool hasContext = t.ProjectId is int && !string.IsNullOrWhiteSpace(t.ProjectTitle);

            // The optional schedule. A START is what makes an entry timed — a
            // personal task is the only kind of entry whose hour someone
            // actually chose. The END is separate and optional: with one, the
            // entry is a block; without, it is a deadline AT an hour, and the
            // grid draws it as a marker rather than guessing a duration.
            //
            // An end that is not after its start is discarded rather than
            // trusted. The API refuses that pair, but an older row could still
            // hold one, and a negative-length block would break the grid.
            var  start   = ParseWallClock(t.StartTime);
            var  end     = ParseWallClock(t.EndTime);
            bool isTimed = start is not null;
            bool hasEnd  = isTimed && end is TimeSpan e0 && e0 > start!.Value;

            var date = isTimed ? t.DueDate!.Value.Date + start!.Value : t.DueDate!.Value;

            // A team AND an hour is what makes this entry a MEETING rather than
            // a reminder — the design's fifth type, taken off the same row
            // rather than out of a new table. Either fact alone leaves it a
            // personal task: a timed entry with no team is the mentor's own
            // work block, and a team entry with no hour is a note about that
            // team. See MentorCalendarModel for why this is not a fabrication.
            var kind = hasContext && isTimed
                ? MentorEventType.Meeting
                : MentorEventType.PersonalTask;

            events.Add(new MentorCalendarEvent(
                Id: $"pt-{t.Id}", Type: kind,
                Date: date, Title: t.Title,
                ProjectId:    hasContext ? t.ProjectId : null,
                ProjectTitle: hasContext ? t.ProjectTitle : null,
                TeamName:     hasContext ? t.TeamName : null,
                Detail: t.Description,
                // Straight to THIS task's editor, not the section.
                Href: MentorLinks.PersonalTask(t.Id),
                HasTime: isTimed,
                EndsAt:  hasEnd ? t.DueDate!.Value.Date + end!.Value : null,
                EntityId: t.Id));
        }

        // ── Submissions already sitting with the mentor. Dated by ARRIVAL,
        //    which is the only real date there is: nothing stores a review-by
        //    deadline. Age and state come straight from the attention model, so
        //    a review reads identically here and on המשימות שלי. ──
        foreach (var r in snapshot.Reviews)
        {
            events.Add(new MentorCalendarEvent(
                Id: $"rv-{r.EntityId}", Type: MentorEventType.Review,
                Date: r.WaitingSince ?? DateTime.Now, Title: $"התקבלה לבדיקה — {r.Title}",
                ProjectId: r.ProjectId, ProjectTitle: r.ProjectTitle,
                TeamName: r.TeamName ?? (r.ProjectId is int pid ? snapshot.TeamNameFor(pid) : null),
                Detail: string.IsNullOrWhiteSpace(r.MilestoneTitle) ? null : $"אבן דרך: {r.MilestoneTitle}",
                // The attention model's own Href, which IS the deep link to this
                // submission's review drawer — it used to stop at the project
                // page, so the calendar rebuilt the link itself. Now that every
                // attention Href opens its own entity, a calendar entry, a Home
                // row and the daily digest all land in the same drawer.
                Href: r.Href,
                Age: r.Age,
                WaitingLabel: r.WaitingLabel,
                EntityId: r.EntityId));
        }

        // ── Milestones and dated deliverables, per project.
        //    Fetched concurrently: sequential awaits here would make the
        //    calendar's load time scale with the mentor's caseload. ──
        var resolved = details
            ?? await Task.WhenAll(
                   snapshot.Projects.Select(p => _projects.GetProjectDetailAsync(p.Id)));

        foreach (var detail in resolved)
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
                        Href: $"mentor/projects/{detail.Id}"));
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
                        Href: $"mentor/projects/{detail.Id}"));
                }
            }
        }

        return events.OrderBy(e => e.Date).ToList();
    }

    /// <summary>"HH:mm" as stored by the personal-task endpoints, or null.
    /// One parser, shared with the time field that writes the value, so what
    /// the editor accepts and what the calendar reads cannot diverge.</summary>
    private static TimeSpan? ParseWallClock(string? value) =>
        AuthWithAdmin.Client.Components.MotivaDates.ParseWallClock(value);
}
