using System;
using System.Collections.Generic;
using System.Linq;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Pages.Assignments;

/// <summary>
/// ONE sample submitted assignment form, laid over the waiting queue so the
/// lecturer-side reading path — row → טופס שיבוץ → preferences → team →
/// notes → project quick view → עבור לשיבוץ הצוות — can be inspected on a
/// realistic form without a realistic form having to exist in the database.
///
/// <para><b>Why this is not <see cref="AssignmentsDemoData"/>.</b> That overlay
/// answers "how does this screen behave on a cohort of seven": it is a MODE,
/// it is off whenever real submissions exist, and its teams take part in demand
/// and in the cohort's rivalry so that assigning one really does change what the
/// next one is offered. This is the opposite object. It is a single form that is
/// always present, that never joins a count, never joins demand, never becomes
/// a rival, and can never be assigned. It exists to be READ, not to be worked.</para>
///
/// <para><b>What it deliberately cannot touch.</b>
/// <list type="bullet">
///   <item>It is built as an <see cref="AssignmentTeamView"/>, never as an
///         <c>AssignmentSubmissionListItemDto</c>, so it structurally cannot
///         enter the page's submissions spine — which is what demand is counted
///         over. The isolation is a type, not a filter someone has to remember.</item>
///   <item><c>IsSample</c> keeps it out of every count on the screen: the tab
///         badge, the header summary and the result line all stay on the real
///         cohort, so the QA baseline still reads 5 waiting / 0 assigned /
///         0 published with this row on screen.</item>
///   <item>The page refuses every mutating handler for
///         <see cref="TeamId"/> before any HTTP call, so עבור לשיבוץ הצוות opens
///         the real workspace and the real workspace still cannot post it.</item>
/// </list></para>
///
/// <para><b>What is real about it.</b> Everything it points AT. Its three
/// preferences are real rows from the board the API returned, picked as the
/// three most-wanted free projects in the CURRENT cohort, so the demand meters
/// beside them are the real counts off the real submissions, the availability is
/// the real board's, and the rivals its recommendation weighs are the real
/// waiting teams. Only the students, the timestamp and the note are invented —
/// and the row says so, in the open, on both surfaces.</para>
/// </summary>
internal static class AssignmentSampleForm
{
    /// <summary>Far outside anything the database can produce, and above
    /// <see cref="AssignmentsDemoData.TeamIdBase"/> so the two overlays can
    /// never collide.</summary>
    public const int TeamId = 990_001;

    private const int MemberIdBase = 991_001;

    /// <summary>The one label. Used verbatim on the queue row, in the form
    /// viewer and in the decision workspace.</summary>
    public const string Label = "נתוני הדגמה";

    public const string Hint =
        "שורת הדגמה לבדיקת המסך בלבד. הסטודנטים, מועד ההגשה וההערה מומצאים; הפרויקטים, הביקוש והזמינות אמיתיים. השורה אינה נספרת באף מונה ולא ניתן לשבץ אותה.";

    public static bool IsSampleTeam(int teamId) => teamId == TeamId;

    // ─────────────────────────────────────────────────────────────────────────
    //  The invented half — two students, their strengths, and what they wrote
    // ─────────────────────────────────────────────────────────────────────────

    private const string SampleTeamName = "צוות דוגמה";

    private sealed record Member(string Name, string Mail, string[] Strengths);

    private static readonly Member[] Members =
    {
        new("דניאל בן-שושן", "daniel.benshoshan@demo.local", new[] { "Technology", "Design" }),
        new("שירה אלקיים",   "shira.elkayam@demo.local",     new[] { "ProjectManagement", "Content" })
    };

    private const string SampleNotes =
        "הצוות מעוניין בפרויקט עם דגש על ניתוח נתונים וממשק משתמש, ומוכן להתגמש בסדר ההעדפות אם תימצא התאמה טובה יותר.";

    /// <summary>Rank 1/2/3 skill halves. Chosen so the three rows land in three
    /// different score bands (54 / 35 / 19 against the server's own
    /// 30-20-10 ladder), which is what makes the form worth opening: a viewer
    /// tested on three identical scores tells you nothing about the bands.</summary>
    private static readonly int[] SkillScores = { 24, 15, 9 };

    /// <summary>Fallback stamp for a board whose real submissions carry no
    /// parseable date. Never a clock: a screenshot taken twice is the same
    /// screenshot.</summary>
    private const string FallbackSubmittedAt = "2026-05-04T10:22:00";

    // ─────────────────────────────────────────────────────────────────────────
    //  Resolution against the live board
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>How many free projects the sample needs. Below three it would
    /// have to repeat a project across two ranks, and the preference list — the
    /// one thing this form exists to show — would be a lie about the shape of a
    /// real submission.</summary>
    private const int RequiredOpenProjects = 3;

    /// <summary>The sample row, or null when the board cannot carry it (no data
    /// yet, or fewer than three free projects). Never throws and never partially
    /// builds: the caller either gets a whole form or nothing at all.</summary>
    public static AssignmentTeamView? Build(
        AssignmentBoardDto?                              board,
        IReadOnlyList<AssignmentSubmissionListItemDto>?  realSubmissions)
    {
        if (board is null) return null;

        var free = board.Projects.Where(p => !p.AssignedTeamId.HasValue).ToList();
        if (free.Count < RequiredOpenProjects) return null;

        // ── Preferences: the three most-wanted FREE projects in the real cohort
        //    Ranking by the real demand is what makes the meters in the viewer
        //    say something true. A cohort with no submissions at all falls back
        //    to project number, which is arbitrary but never invented.
        var demand = RealDemand(realSubmissions);

        var picked = free
            .OrderByDescending(p => demand.TryGetValue(p.ProjectId, out var d) ? d.Teams : 0)
            .ThenByDescending(p => demand.TryGetValue(p.ProjectId, out var d) ? d.FirstChoice : 0)
            .ThenBy(p => p.ProjectNumber)
            .Take(RequiredOpenProjects)
            .ToList();

        var prefs = picked
            .Select((p, i) => new AssignmentSubmissionPreferenceDto
            {
                Priority      = i + 1,
                ProjectId     = p.ProjectId,
                ProjectNumber = p.ProjectNumber,
                ProjectTitle  = p.ProjectName,
                ProjectType   = p.ProjectType
            })
            .ToList();

        return new AssignmentTeamView
        {
            TeamId        = TeamId,
            TeamName      = SampleTeamName,
            AcademicYear  = board.AcademicYearName,
            SubmittedAt   = SubmittedAt(realSubmissions),
            HasSubmission = true,
            IsSample      = true,
            Notes         = SampleNotes,
            Members       = Members.Select((m, i) => new AssignmentSubmissionMemberDto
            {
                UserId    = MemberIdBase + i,
                FullName  = m.Name,
                Email     = m.Mail,
                Strengths = m.Strengths.ToList()
            }).ToList(),
            Preferences = prefs,
            Matches     = BuildMatches(prefs, free)
        };
    }

    /// <summary>Demand over the REAL submissions only — the same two counts the
    /// page shows, computed here purely to decide which projects the sample
    /// should rank. Nothing derived from the sample is ever fed back into it.</summary>
    private static Dictionary<int, ProjectDemand> RealDemand(
        IReadOnlyList<AssignmentSubmissionListItemDto>? submissions)
    {
        var result = new Dictionary<int, ProjectDemand>();
        if (submissions is null) return result;

        var teams = new Dictionary<int, HashSet<int>>();
        var first = new Dictionary<int, HashSet<int>>();

        foreach (var sub in submissions)
            foreach (var pref in sub.Preferences)
            {
                if (!teams.TryGetValue(pref.ProjectId, out var all))
                    teams[pref.ProjectId] = all = new HashSet<int>();
                all.Add(sub.TeamId);

                if (pref.Priority != 1) continue;
                if (!first.TryGetValue(pref.ProjectId, out var tops))
                    first[pref.ProjectId] = tops = new HashSet<int>();
                tops.Add(sub.TeamId);
            }

        foreach (var kv in teams)
            result[kv.Key] = new ProjectDemand(
                kv.Value.Count,
                first.TryGetValue(kv.Key, out var f) ? f.Count : 0);

        return result;
    }

    /// <summary>Placed just after the newest real form, so the sample reads as
    /// the most recent arrival in the same window as the rest of the cohort
    /// rather than as a date from nowhere.</summary>
    private static string SubmittedAt(IReadOnlyList<AssignmentSubmissionListItemDto>? submissions)
    {
        DateTime? newest = null;

        foreach (var sub in submissions ?? Array.Empty<AssignmentSubmissionListItemDto>())
            if (DateTime.TryParse(sub.SubmittedAt, out var dt) && (newest is null || dt > newest))
                newest = dt;

        return newest is null ? FallbackSubmittedAt : newest.Value.AddHours(3).ToString("s");
    }

    /// <summary>The sample's scored rows, built the way the server builds them
    /// — <c>preference(30/20/10) + skill</c> — so a sample score and a real one
    /// mean the same thing and land in the same bands the queue colours by.
    ///
    /// <para>The externals matter: they are what the decision workspace offers
    /// under חלופות, and a sample whose alternatives panel is empty cannot be
    /// used to inspect that panel.</para></summary>
    private static List<TeamProjectMatchDto> BuildMatches(
        List<AssignmentSubmissionPreferenceDto> prefs,
        List<AssignmentBoardProjectDto>         free)
    {
        static int RankScore(int rank) => rank switch { 1 => 30, 2 => 20, 3 => 10, _ => 0 };

        var rows = prefs.Select((p, i) => new TeamProjectMatchDto
        {
            TeamId          = TeamId,
            TeamName        = SampleTeamName,
            ProjectId       = p.ProjectId,
            ProjectName     = p.ProjectTitle,
            ProjectType     = p.ProjectType,
            PreferenceRank  = p.Priority,
            PreferenceScore = RankScore(p.Priority),
            SkillScore      = SkillScores[i],
            TotalMatchScore = RankScore(p.Priority) + SkillScores[i]
        }).ToList();

        var ranked = prefs.Select(p => p.ProjectId).ToHashSet();

        // Deterministic per project — the id decides the score, never a clock
        // or a random, so the alternatives panel is the same on every render.
        foreach (var proj in free.Where(p => !ranked.Contains(p.ProjectId)).Take(6))
            rows.Add(new TeamProjectMatchDto
            {
                TeamId          = TeamId,
                TeamName        = SampleTeamName,
                ProjectId       = proj.ProjectId,
                ProjectName     = proj.ProjectName,
                ProjectType     = proj.ProjectType,
                PreferenceRank  = null,
                PreferenceScore = 0,
                SkillScore      = 10 + ((proj.ProjectId * 7) % 5) * 4,
                TotalMatchScore = 10 + ((proj.ProjectId * 7) % 5) * 4
            });

        return rows.OrderByDescending(m => m.TotalMatchScore).ToList();
    }
}
