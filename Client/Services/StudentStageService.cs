using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

/// <summary>
/// Where a student is in the onboarding journey — the single answer three
/// different surfaces used to work out for themselves.
/// </summary>
public enum StudentStage
{
    /// <summary>Signed in but in no active team. Nothing else can happen until
    /// a team exists.</summary>
    NoTeam,

    /// <summary>A team with no submitted assignment form. The catalogue is the
    /// job: browse, rank three, submit.</summary>
    NeedsPreferences,

    /// <summary>The form is in, the lecturer has not decided yet. NOT the same
    /// as NeedsPreferences, and the difference is the whole point of this type
    /// — before it, a team that had already submitted was sent back to an
    /// editable catalogue as though nothing had happened.</summary>
    AwaitingAssignment,

    /// <summary>A project is assigned. The normal student workspace.</summary>
    Assigned
}

/// <summary>
/// Resolves <see cref="StudentStage"/> once and hands the same answer to
/// everyone who asks.
///
/// <para><b>Why this exists.</b> The rule "a student with a team but no project
/// belongs in the catalogue" was written out longhand in three places —
/// LoginPage's redirect, and two branches of Dashboard — each re-fetching and
/// each able to drift from the others. None of them knew whether the team had
/// already SUBMITTED, so all three sent a team that was waiting for a decision
/// back to a catalogue that invited it to make one.</para>
///
/// <para><b>It answers for students only.</b> <see cref="ResolveAsync"/> is
/// called behind a role check by every caller; the service itself never
/// navigates, so it cannot send a lecturer into a student route. Callers decide
/// what to do with the answer — some redirect, some render a different body,
/// which is what keeps a guard from becoming a redirect loop.</para>
///
/// <para><b>Cached for the lifetime of the TAB, not the request.</b> In a WASM
/// app a scoped service is created once and lives until the page is reloaded, so
/// the cache outlives sign-outs unless something drops it. Everything that can
/// change the answer therefore calls <see cref="Invalidate"/> explicitly: login
/// and logout (the IDENTITY changed), team creation (NoTeam no longer holds),
/// and assignment submission (NeedsPreferences became AwaitingAssignment). A
/// stale stage is not a slow UI — it is a redirect to the wrong journey.</para>
/// </summary>
public interface IStudentStageService
{
    Task<StudentStage> ResolveAsync();

    /// <summary>Drops the cached answer. Called wherever an input to it changes:
    /// login, logout, team creation, and assignment submission.</summary>
    void Invalidate();

    /// <summary>The submitted form, when there is one — so the waiting state can
    /// show what was sent without a second round trip.</summary>
    ExistingAssignmentDto? Submission { get; }

    /// <summary>The submission window as the FORM BUILDER defines it, including
    /// AllowEditAfterSubmit. Read, never assumed: whether a submitted team may
    /// reopen its form is the admin's decision and the server enforces it in
    /// FormsRepository.EvaluateGate.</summary>
    AssignmentFormStatusDto? FormStatus { get; }
}

public class StudentStageService : IStudentStageService
{
    private readonly HttpClient _http;

    private StudentStage? _cached;

    public StudentStageService(HttpClient http) => _http = http;

    public ExistingAssignmentDto?   Submission { get; private set; }
    public AssignmentFormStatusDto? FormStatus { get; private set; }

    public void Invalidate()
    {
        _cached    = null;
        Submission = null;
        FormStatus = null;
    }

    public async Task<StudentStage> ResolveAsync()
    {
        if (_cached is StudentStage hit) return hit;

        //  Two existing endpoints, no new server surface. The assignment
        //  context knows about the TEAM and the FORM; the dashboard knows about
        //  the PROJECT. Neither knows both, which is why the rule lived in the
        //  callers before.
        AssignmentContextDto? ctx = null;
        DashboardDto?         dash = null;

        try { ctx  = await _http.GetFromJsonAsync<AssignmentContextDto>("api/assignment/context"); } catch { }
        try { dash = await _http.GetFromJsonAsync<DashboardDto>("api/projects/my-dashboard");     } catch { }

        Submission = ctx?.ExistingSubmission;
        FormStatus = ctx?.FormStatus;

        //  Ordered most-settled first: an assigned project outranks everything,
        //  because a team that has been given a project is done with this
        //  journey whatever its form says.
        var stage =
            dash?.Project is not null                     ? StudentStage.Assigned
            : ctx?.ExistingSubmission is not null         ? StudentStage.AwaitingAssignment
            : (ctx?.HasTeam ?? dash?.HasTeam ?? false)    ? StudentStage.NeedsPreferences
            :                                               StudentStage.NoTeam;

        //  A failed fetch is not an answer. Caching "NoTeam" because the network
        //  blinked would bounce a real team to the registration form for the
        //  rest of the session.
        if (ctx is not null || dash is not null) _cached = stage;

        return stage;
    }

    /// <summary>The route a stage belongs on. Returned WITHOUT a leading slash,
    /// matching PageRoutes' own convention.</summary>
    public static string RouteFor(StudentStage stage) => stage switch
    {
        StudentStage.NoTeam             => "create-team",
        StudentStage.NeedsPreferences   => PageRoutes.StudentCatalog,
        StudentStage.AwaitingAssignment => PageRoutes.StudentCatalog,
        _                               => PageRoutes.Dashboard
    };
}
