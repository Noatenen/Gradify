using AuthWithAdmin.Client.Services;

namespace AuthWithAdmin.Client.Pages.Learning;

/// <summary>
/// Stage resolvers for <c>ResourcesLibrary.CurrentStageLoader</c>.
///
/// <para>The shared Resources screen shows a "current stage" strip, but no
/// single endpoint can answer "which stage is it now" for every role:
/// <c>api/dashboard</c> is <c>[Authorize(Admin, Staff, Mentor)]</c> and would
/// refuse a student, while <c>api/projects/my-dashboard</c> answers only for
/// the caller's own project. So the question is asked by the HOST, against the
/// source that host is authorised for, and handed to the component as a
/// delegate.</para>
///
/// <para>This class exists for the one resolver Lecturer and Mentor SHARE —
/// same endpoint, same aggregation, different server-side scope. The student's
/// resolver is three lines over a different service and lives in its own host
/// rather than being generalised into a second method here.</para>
/// </summary>
public static class ResourceStageSources
{
    /// <summary>
    /// The most common current milestone across the projects in scope, or null
    /// when the dashboard is unavailable or no project has one.
    /// </summary>
    /// <param name="scope">"lecturer" or "mentor". The server resolves the
    /// scope itself from the caller's roles — passing it here never widens
    /// what the caller can see.</param>
    public static Func<Task<string?>> FromDashboardOverview(
        IDashboardOverviewService service, string scope) => async () =>
    {
        var overview = await service.GetAsync(scope);
        if (overview is null || overview.Projects.Count == 0) return null;

        return overview.Projects
            .Where(p => !string.IsNullOrWhiteSpace(p.CurrentMilestoneTitle))
            .GroupBy(p => p.CurrentMilestoneTitle!)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;
    };
}
