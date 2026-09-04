using System;
using System.Collections.Generic;
using System.Linq;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Pages.Assignments;

/// <summary>
/// One submitted assignment form, joined with everything the board knows about
/// that team.
///
/// <para>This used to be a private <c>TeamRow</c> class inside AssignmentsPage,
/// which meant the only thing that could render a team's form was the page
/// itself — and so "look at what this team submitted" and "decide where this
/// team goes" had to be the same expanded table row. It is a top-level type now
/// because three different surfaces need it: the waiting queue, the read-only
/// form viewer (<c>AssignmentFormModal</c>) and the decision workspace
/// (<c>TeamAssignmentWorkspace</c>).</para>
///
/// <para>The submissions endpoint owns the form (members, strengths,
/// preferences, notes); the board owns the assignment and the match scores.
/// Neither alone can answer "what did they ask for, and where are they now".</para>
/// </summary>
public sealed class AssignmentTeamView
{
    public int    TeamId       { get; init; }
    public string TeamName     { get; init; } = "";
    public string AcademicYear { get; init; } = "";
    public string SubmittedAt  { get; init; } = "";

    /// <summary>False for a team the board knows about but that has no form row
    /// — a withdrawn submission, or the two endpoints disagreeing. It still needs
    /// to appear in the queue, or it becomes invisible on the only screen that
    /// can assign it, but it must not claim to have submitted anything.</summary>
    public bool HasSubmission { get; init; }

    /// <summary>True for the single inspection row built by
    /// <c>AssignmentSampleForm</c> — a form that exists only so the reading path
    /// can be walked on realistic data.
    ///
    /// <para>It is a property on the row rather than a check against an id range
    /// because everything that must exclude it is a QUESTION ABOUT THE ROW: is
    /// this part of the remaining job (the counts), is this a rival for a
    /// contested project (the cohort), may this be assigned (the handlers). A
    /// row that answers "I am a sample" answers all three, and a new call site
    /// cannot forget an id range it never has to know about.</para></summary>
    public bool IsSample { get; init; }

    public bool    HasOwnProject         { get; init; }
    public string? OwnProjectDescription { get; init; }
    public string? Notes                 { get; init; }

    public List<AssignmentSubmissionMemberDto>     Members     { get; init; } = new();
    public List<AssignmentSubmissionPreferenceDto> Preferences { get; init; } = new();

    /// <summary>Every project the matcher scored for this team, best first.</summary>
    public List<TeamProjectMatchDto> Matches { get; set; } = new();

    public int?    AssignedProjectId   { get; set; }
    public string? AssignedProjectName { get; set; }
    public bool    AssignedIsDraft     { get; set; }

    public bool IsWaiting => !AssignedProjectId.HasValue;

    public string MembersLine => string.Join(" · ", Members.Select(m => m.FullName));
}

/// <summary>
/// How contested one project is, counted from the submitted preference forms.
///
/// <para><b>Why this is not the board's DemandScore.</b> The server returns a
/// WEIGHTED sum — 30 for a first choice, 20 for a second, 10 for a third —
/// which is the right thing to rank a catalogue by and the wrong thing to put
/// in front of a person: "70" cannot be read as either "how many" or "how
/// badly", and neither count can be recovered from it. Both numbers here come
/// from the same preference rows the server sums, so nothing is invented and
/// the two can never disagree.</para>
/// </summary>
public sealed record ProjectDemand(int Teams, int FirstChoice)
{
    public static readonly ProjectDemand None = new(0, 0);
}

/// <summary>
/// Another WAITING team's claim on the same project.
///
/// <para>Only teams that actually ranked the project appear here. A team the
/// matcher happens to score highly on a project it never asked for has no
/// claim on it — protecting a project for such a team would be the engine
/// overriding the cohort's own stated wishes, which is the opposite of what
/// the preference form is for.</para>
/// </summary>
public sealed record RivalClaim(int TeamId, string TeamName, int Score, int PreferenceRank);

/// <summary>
/// What every still-waiting team scores on every project.
///
/// <para><b>Why the recommender needs this.</b> Scored one team at a time, the
/// engine hands a contested project to whichever team the lecturer happens to
/// open first — the other claimant is invisible at the moment of the decision,
/// and by the time it is opened the project is gone. This is the cohort seen
/// whole, so a recommendation can account for who else is waiting for the same
/// thing.</para>
///
/// <para>Assigned teams are deliberately absent: they are no longer competing
/// for anything, and their old scores would freeze a project for a team that
/// already has one.</para>
/// </summary>
public sealed class CohortClaims
{
    private readonly Dictionary<int, List<RivalClaim>> _byProject;

    public static readonly CohortClaims Empty = new(new List<AssignmentTeamView>());

    public CohortClaims(IEnumerable<AssignmentTeamView> waitingTeams)
    {
        _byProject = new Dictionary<int, List<RivalClaim>>();

        foreach (var team in waitingTeams.Where(t => t.IsWaiting))
        {
            var scores = team.Matches.ToDictionary(m => m.ProjectId, m => m.TotalMatchScore);

            foreach (var pref in team.Preferences)
            {
                var score = scores.TryGetValue(pref.ProjectId, out var v)
                    ? v
                    : pref.Priority switch { 1 => 30, 2 => 20, 3 => 10, _ => 0 };

                if (!_byProject.TryGetValue(pref.ProjectId, out var list))
                    _byProject[pref.ProjectId] = list = new List<RivalClaim>();

                list.Add(new RivalClaim(team.TeamId, team.TeamName, score, pref.Priority));
            }
        }

        foreach (var list in _byProject.Values)
            list.Sort((a, b) => b.Score.CompareTo(a.Score));
    }

    /// <summary>The strongest OTHER waiting claimant on a project, or null when
    /// nobody else is waiting for it.</summary>
    public RivalClaim? BestRival(int projectId, int excludingTeamId) =>
        _byProject.TryGetValue(projectId, out var list)
            ? list.FirstOrDefault(c => c.TeamId != excludingTeamId)
            : null;
}

/// <summary>Where an option came from. A project the team actually asked for
/// and a project the matcher merely liked are not the same kind of suggestion,
/// and the workspace must never present them in one undifferentiated list.</summary>
public enum AssignmentOptionSource { Preference, External }

/// <summary>One project this team could be put on, with everything needed to
/// judge it in one place: rank, score, demand and availability.</summary>
public sealed record AssignmentOption(
    TeamProjectMatchDto    Match,
    AssignmentOptionSource Source,
    int?                   PreferenceRank,
    ProjectDemand          Demand,
    string?                TakenBy,
    bool                   IsCurrent,
    string                 Reason,
    RivalClaim?            Rival = null,
    bool                   Yielded = false)
{
    public int    ProjectId   => Match.ProjectId;
    public string ProjectName => Match.ProjectName;
    public int    Score       => Match.TotalMatchScore;

    /// <summary>Free to assign right now. The team's OWN current project is not
    /// "available" — it is where they already are.</summary>
    public bool IsAvailable => TakenBy is null && !IsCurrent;

    /// <summary>Another waiting team wants this too, and fits it meaningfully
    /// better. Not a blocker — a fact the lecturer should see before choosing.</summary>
    public bool HasStrongerRival => Rival is not null && Rival.Score - Score >= 10;
}

/// <summary>The whole recommendation for one team, recomputed from current
/// state every time it is asked for.</summary>
public sealed class AssignmentRecommendation
{
    /// <summary>What the system recommends doing right now, or null when there
    /// is no valid move left.</summary>
    public AssignmentOption? Primary { get; init; }

    /// <summary>ONLY options from outside the submitted form. The team's other
    /// preferences are not repeated here — they are already listed whole, with
    /// their own actions, under העדפות הצוות, and showing them twice made the
    /// same project look like two different offers.</summary>
    public List<AssignmentOption> Alternatives { get; init; } = new();

    /// <summary>A better-matched waiting team the recommendation stepped aside
    /// for, with the project it stepped aside from. Null when nothing was
    /// contested — which is most of the time.</summary>
    public AssignmentOption? YieldedTo { get; init; }

    /// <summary>Every submitted preference, in the rank the team gave it,
    /// INCLUDING the ones that are taken. This is the team's own answer and it
    /// is shown whole — a preference that is unavailable is a fact the lecturer
    /// needs, not a row to hide.</summary>
    public List<AssignmentOption> Preferences { get; init; } = new();

    public bool HasAnyMove => Primary is not null;
}

/// <summary>
/// Ranks the options for one team.
///
/// <para><b>Why this exists on the client.</b> The server's matcher scores
/// <c>preference(30/20/10) + skills</c> over EVERY open project, so a project
/// the team never asked for can outrank one they put first — a technological
/// project scores 45 on skills alone, a methodological first choice scores 30.
/// Ordering the raw match list by score therefore produced a headline
/// recommendation with no relationship to what the team submitted, which is the
/// single thing the lecturer is trying to honour. Nothing about the scores is
/// changed here: they are the server's, they are still shown, and they still
/// rank options WITHIN a group. What changes is that the team's own priority
/// decides the group order.</para>
///
/// <para><b>Why it is dynamic.</b> Availability is read from the live board on
/// every call, so assigning, moving or unassigning any team re-answers the
/// question for every other team without a second endpoint: a #1 that goes to
/// somebody else drops out and the #2 becomes the recommendation; unassign it
/// again and the #1 comes back.</para>
/// </summary>
public static class AssignmentRecommender
{
    /// <summary>How many non-preference projects are worth offering. The
    /// catalogue runs to a hundred-plus projects and a list of them is not a
    /// recommendation.</summary>
    private const int MaxExternal = 3;

    /// <summary>How much better another team's match has to be before this one
    /// steps aside. Ten points is not a tuned constant — it is one full rung of
    /// the server's own preference ladder (30 / 20 / 10), so "substantially
    /// better" means "better by more than a whole preference rank", stated in
    /// the only unit this model actually has.</summary>
    private const int MeaningfulLead = 10;

    public static AssignmentRecommendation Build(
        AssignmentTeamView          team,
        Func<int, string?>          holderOf,
        Func<int, ProjectDemand>    demandOf,
        CohortClaims?               cohort = null)
    {
        cohort ??= CohortClaims.Empty;

        var byProject = team.Matches.ToDictionary(m => m.ProjectId);

        // ── The team's own answer, in the team's own order ──────────────────
        // Built from the PREFERENCES, not from the match list, so a preference
        // the matcher has no row for still appears (it is the team's stated
        // wish either way) rather than silently vanishing.
        var preferences = team.Preferences
            .OrderBy(p => p.Priority)
            .Select(p => Make(
                byProject.TryGetValue(p.ProjectId, out var m) ? m : Synthetic(team, p),
                AssignmentOptionSource.Preference,
                p.Priority,
                team, holderOf, demandOf, cohort))
            .ToList();

        var availablePrefs = preferences.Where(o => o.IsAvailable).ToList();

        var prefIds = preferences.Select(o => o.ProjectId).ToHashSet();

        // ── Everything else the matcher scored, best first ──────────────────
        // Ranked by usefulness, not by raw score alone: an option a better-
        // matched team is also waiting for is a worse suggestion than a
        // slightly lower-scoring one nobody is competing for.
        var externals = team.Matches
            .Where(m => !prefIds.Contains(m.ProjectId))
            .Select(m => Make(m, AssignmentOptionSource.External, null, team, holderOf, demandOf, cohort))
            .Where(o => o.IsAvailable)
            .OrderBy(o => o.HasStrongerRival ? 1 : 0)
            .ThenByDescending(o => o.Score)
            .ThenBy(o => o.ProjectName, StringComparer.Ordinal)
            .Take(MaxExternal)
            .ToList();

        // ── The one move ───────────────────────────────────────────────────
        // In preference order, then by score — but a candidate is skipped when
        // another WAITING team wants the same project, is substantially better
        // matched to it, and gains more from it than this team loses by moving
        // on. That last clause is what keeps this a cohort decision rather than
        // a rule that quietly punishes whoever is opened first: stepping aside
        // has to make the pair of teams better off, not just this one worse.
        AssignmentOption? primary = null;
        AssignmentOption? yielded = null;

        // Stepping aside happens WITHIN the submitted form and never out of it.
        // A team whose every choice has a stronger claimant is still a team
        // that asked for three specific projects; recommending a fourth it
        // never mentioned would trade a contested wish for an unwanted one, and
        // the rivals it stepped aside for have not actually been assigned yet.
        for (var i = 0; i < availablePrefs.Count; i++)
        {
            var candidate = availablePrefs[i];
            var rival     = candidate.Rival;

            if (rival is null || rival.Score - candidate.Score < MeaningfulLead)
            {
                primary = candidate;
                break;
            }

            // What this team would actually fall through to — the options BELOW
            // this one, not every other option. Measuring against a choice it
            // has already passed over would make stepping aside look free when
            // it is not.
            var fallback  = availablePrefs.Skip(i + 1).Select(o => (int?)o.Score).Max() ?? 0;
            var myLoss    = candidate.Score - fallback;
            var rivalGain = rival.Score - candidate.Score;

            // Yield only when the pair of teams is better off for it: the rival
            // must gain more than this team gives up.
            if (rivalGain <= myLoss)
            {
                primary = candidate;
                break;
            }

            yielded ??= candidate with { Yielded = true };
        }

        // Nothing left inside the form, or nothing in it to begin with.
        primary ??= availablePrefs.FirstOrDefault() ?? externals.FirstOrDefault();

        var alternatives = externals
            .Where(o => primary is null || o.ProjectId != primary.ProjectId)
            .ToList();

        // The reason can only be written once the winner is known — "this is
        // the next free preference" is a statement about the ones above it.
        if (primary is not null)
            primary = primary with { Reason = PrimaryReason(primary, preferences, yielded, externals) };

        return new AssignmentRecommendation
        {
            Primary      = primary,
            Alternatives = alternatives,
            Preferences  = preferences,
            YieldedTo    = yielded
        };
    }

    private static AssignmentOption Make(
        TeamProjectMatchDto      match,
        AssignmentOptionSource   source,
        int?                     rank,
        AssignmentTeamView       team,
        Func<int, string?>       holderOf,
        Func<int, ProjectDemand> demandOf,
        CohortClaims             cohort)
    {
        var isCurrent = team.AssignedProjectId == match.ProjectId;
        var holder    = isCurrent ? null : holderOf(match.ProjectId);

        return new AssignmentOption(
            Match:          match,
            Source:         source,
            PreferenceRank: rank ?? match.PreferenceRank,
            Demand:         demandOf(match.ProjectId),
            TakenBy:        holder,
            IsCurrent:      isCurrent,
            Reason:         "",
            Rival:          cohort.BestRival(match.ProjectId, team.TeamId));
    }

    /// <summary>A preference the matcher produced no row for — it can happen
    /// when the project has since been closed. Scored as the server would score
    /// the preference alone, so the number on screen is never invented.</summary>
    private static TeamProjectMatchDto Synthetic(
        AssignmentTeamView team, AssignmentSubmissionPreferenceDto pref)
    {
        var prefScore = pref.Priority switch { 1 => 30, 2 => 20, 3 => 10, _ => 0 };
        return new TeamProjectMatchDto
        {
            TeamId              = team.TeamId,
            TeamName            = team.TeamName,
            ProjectId           = pref.ProjectId,
            ProjectName         = pref.ProjectTitle,
            ProjectType         = pref.ProjectType,
            PreferenceRank      = pref.Priority,
            PreferenceScore     = prefScore,
            SkillScore          = 0,
            TotalMatchScore     = prefScore,
            RecommendationLabel = ""
        };
    }

    /// <summary>One sentence a lecturer can argue with. It names facts they can
    /// check on the same screen — a rank, a taken project, a competing team —
    /// and never the mechanism that produced them.</summary>
    private static string PrimaryReason(
        AssignmentOption        primary,
        List<AssignmentOption>  preferences,
        AssignmentOption?       yielded,
        List<AssignmentOption>  externals)
    {
        // The contest outranks every other explanation: it is the one fact
        // that explains why the obvious answer is not the answer.
        if (yielded is not null && yielded.ProjectId != primary.ProjectId)
            return $"{yielded.ProjectName} מבוקש — ל{yielded.Rival!.TeamName} התאמה גבוהה יותר אליו";

        if (primary.HasStrongerRival)
            return $"פרויקט מבוקש — גם ל{primary.Rival!.TeamName} התאמה גבוהה יותר אליו";

        if (primary.Source == AssignmentOptionSource.External)
        {
            var lead = preferences.Count == 0
                ? "הצוות לא דירג העדפות"
                : "אף אחת מההעדפות שהוגשו אינה פנויה";
            return $"{lead} — זו ההתאמה הגבוהה ביותר מבין הפרויקטים הפנויים";
        }

        var blocked = preferences
            .TakeWhile(o => o.ProjectId != primary.ProjectId)
            .Where(o => !o.IsAvailable)
            .ToList();

        if (blocked.Count > 0)
        {
            var names = string.Join(", ", blocked.Select(b => b.ProjectName));
            return $"ההעדפות שמעליה כבר תפוסות ({names}) — זו ההעדפה הפנויה הבאה של הצוות";
        }

        // A first choice that is also uncontested and quiet is worth saying so:
        // it is the decision the lecturer can take without thinking about it.
        if (primary.PreferenceRank == 1)
        {
            if (primary.Demand.Teams <= 1)
                return "העדפה ראשונה של הצוות, פנויה וללא תחרות";

            var topScore = preferences.Concat(externals).Max(o => o.Score);
            return primary.Score >= topScore
                ? "העדפה ראשונה של הצוות, וגם ההתאמה הגבוהה ביותר מבין הפרויקטים הפנויים"
                : "העדפה ראשונה של הצוות, והפרויקט פנוי";
        }

        return $"ההעדפה הפנויה הגבוהה ביותר של הצוות (#{primary.PreferenceRank})";
    }
}
