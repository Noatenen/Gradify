using System;
using System.Collections.Generic;
using System.Linq;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Pages.Assignments;

/// <summary>
/// Seven submitted assignment forms with nothing behind them, laid OVER the
/// real board so the waiting queue can be evaluated on a cohort that is almost
/// entirely settled.
///
/// <para><b>Why it is an overlay and not a dataset.</b> This used to return a
/// whole fabricated board — its own catalogue of nine invented projects, its own
/// mentors, its own three assignments — and turning it on replaced the screen.
/// That answered "does the layout hold" and nothing else: the demand counts,
/// the availability and the recommendations were all computed over invented
/// projects, so none of them told you how the real cohort behaves. Now the only
/// thing invented is the SUBMISSIONS. Every project a demo team ranks is a real
/// row from the board the API returned, so demand, availability, conflicts and
/// recommendations are all derived against real state, and assigning a demo team
/// really does change what the next one is offered.</para>
///
/// <para><b>Nothing here is ever written.</b> Every id below is invented and the
/// page refuses to reach the service at all while demo mode is on — see
/// <c>AssignmentsPage._demo</c>, which guards every mutating handler. Turning
/// demo mode off re-fetches from the API, so the board returns to exactly what
/// the server says with no restore bookkeeping to get wrong.</para>
///
/// <para><b>Deleting this.</b> One file, one call site
/// (<c>AssignmentsPage.DemoCohort</c>), one field (<c>_demo</c>) and the two
/// chips on the mode bar. Nothing else in the product references it.</para>
///
/// <para><b>The cohort is shaped around the DECISION.</b> Seven teams that are
/// deliberately not interchangeable — a lecturer working this queue top to
/// bottom should meet a different kind of problem in each one:</para>
/// <list type="bullet">
///   <item><b>אלון · ברקת · גפן</b> all put the SAME project first. That project
///         is the contested one, and its badge says so: three teams, three
///         first choices.</item>
///   <item><b>דקל</b> is the easy one — a strong first choice nobody else ranked
///         first, free. The decision that should take two seconds.</item>
///   <item><b>הדס</b> ranked an already-assigned project first, so its best row
///         is the one it cannot have; the recommendation must fall to #2 on its
///         own.</item>
///   <item><b>ורד</b> scores badly everywhere. A cohort always has one, and a
///         demo without it makes the screen look easier than it is.</item>
///   <item><b>זית</b> is the genuinely hard one: its #2 outscores its #1. The
///         recommender still honours the team's own ranking — that is the
///         ruling — and the workspace shows the stronger option right under it
///         so the lecturer can overrule with the numbers in front of them.</item>
/// </list>
///
/// <para><b>Scores are built the way the server builds them.</b>
/// <c>preference(30/20/10 by rank) + skill</c>, so a demo score and a real one
/// mean the same thing and land in the same bands.</para>
/// </summary>
internal static class AssignmentsDemoData
{
    /// <summary>Demo team ids start here. Far outside anything the database can
    /// produce, so a demo row can never be mistaken for — or collide with — a
    /// real team, and <see cref="IsDemoTeam"/> is a range check rather than a
    /// lookup.</summary>
    public const int TeamIdBase = 900_001;

    private const int MemberIdBase = 910_001;

    public static bool IsDemoTeam(int teamId) => teamId >= TeamIdBase;

    /// <summary>How many free projects the overlay needs before it can lay out
    /// its scenarios. Below this the pool would have to reuse a project for two
    /// different roles, and the competition it is built to show would be an
    /// artefact of the padding.</summary>
    public const int RequiredOpenProjects = 5;

    public sealed record Cohort(
        List<AssignmentSubmissionListItemDto>      Submissions,
        Dictionary<int, List<TeamProjectMatchDto>> Matches)
    {
        public static readonly Cohort Empty = new(new(), new());
        public bool IsEmpty => Submissions.Count == 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  The cohort
    //
    //  Preferences are stated as POOL SLOTS, not project ids: this file cannot
    //  know what is in anyone's catalogue. Slot -1 is "a project that is already
    //  assigned"; 0..4 index the free projects, lowest number first. The page
    //  resolves them against the live board, so the same seven scenarios hold on
    //  any database with enough open projects.
    // ─────────────────────────────────────────────────────────────────────────

    private const int TakenSlot = -1;

    private sealed record Member(string Name, string Mail, string[] Strengths);

    private sealed record TeamSpec(
        string   Name,
        string   SubmittedAt,
        int[]    PrefSlots,
        int[]    SkillScores,
        Member[] Members,
        string?  Notes = null);

    private static readonly TeamSpec[] Specs =
    {
        // ── The contested project: three teams, three first choices ─────────
        new("צוות אלון", "2026-04-20T09:12:00",
            new[] { 0, 1, 2 }, new[] { 22, 12, 8 },
            new[]
            {
                new Member("נועם אשכנזי",  "noam.ashkenazi@stud.ac.il",  new[] { "Technology", "Design" }),
                new Member("יעל מזרחי",    "yael.mizrahi@stud.ac.il",    new[] { "ProjectManagement" }),
                new Member("איתי שרון",    "itay.sharon@stud.ac.il",     new[] { "Content" })
            },
            "הצוות מעוניין בפרויקט עם ממשק משתמש משמעותי ולא רק בצד השרת."),

        new("צוות ברקת", "2026-04-21T14:38:00",
            new[] { 0, 2, 3 }, new[] { 16, 14, 9 },
            new[]
            {
                new Member("רוני אלמוג",   "roni.almog@stud.ac.il",      new[] { "Technology" }),
                new Member("הילה בראון",   "hila.braun@stud.ac.il",      new[] { "Design", "Content" })
            }),

        new("צוות גפן", "2026-04-22T11:05:00",
            new[] { 0, 3, 4 }, new[] { 12, 18, 10 },
            new[]
            {
                new Member("אורי כספי",    "ori.caspi@stud.ac.il",       new[] { "ProjectManagement", "Technology" }),
                new Member("שקד ניר",      "shaked.nir@stud.ac.il",      new[] { "Content" }),
                new Member("טל אבוקסיס",   "tal.abukasis@stud.ac.il",    new[] { "Design" })
            },
            "שני חברי הצוות עבדו בעבר על מערכות דומות בהתמחות."),

        // ── The easy one: strong, free, nobody else put it first ────────────
        new("צוות דקל", "2026-04-23T08:47:00",
            new[] { 4, 2, 1 }, new[] { 25, 11, 6 },
            new[]
            {
                new Member("מאיה לוינר",   "maya.leviner@stud.ac.il",    new[] { "Technology", "ProjectManagement" }),
                new Member("אלון וקנין",   "alon.vaknin@stud.ac.il",     new[] { "Technology" })
            }),

        // ── First choice already taken: the recommendation must move down ───
        new("צוות הדס", "2026-04-26T16:20:00",
            new[] { TakenSlot, 1, 3 }, new[] { 20, 17, 7 },
            new[]
            {
                new Member("בר סלומון",    "bar.solomon@stud.ac.il",     new[] { "Design" }),
                new Member("ניצן אוחיון",  "nitzan.ohayon@stud.ac.il",   new[] { "Content", "ProjectManagement" }),
                new Member("עידו רווה",    "ido.rave@stud.ac.il",        new[] { "Technology" })
            },
            "הצוות ביקש במפורש פרויקט בתחום הבריאות."),

        // ── Weak everywhere ─────────────────────────────────────────────────
        new("צוות ורד", "2026-04-28T10:31:00",
            new[] { 3, 4, 2 }, new[] { 4, 3, 2 },
            new[]
            {
                new Member("שירה קרמר",    "shira.kramer@stud.ac.il",    new[] { "Content" }),
                new Member("יהב מור",      "yahav.mor@stud.ac.il",       new[] { "Content" })
            }),

        // ── The hard one: #2 outscores #1 ───────────────────────────────────
        new("צוות זית", "2026-05-02T13:09:00",
            new[] { 2, 1, 4 }, new[] { 10, 35, 5 },
            new[]
            {
                new Member("ליאור אדרי",   "lior.edri@stud.ac.il",       new[] { "Technology", "Design" }),
                new Member("עדי נחמיאס",   "adi.nachmias@stud.ac.il",    new[] { "ProjectManagement" }),
                new Member("גיא שטרן",     "guy.stern@stud.ac.il",       new[] { "Technology" })
            },
            "הצוות ציין שהוא פתוח לשינוי סדר ההעדפות אם יש התאמה טובה יותר.")
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  Resolution against the live board
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Resolves the cohort above onto the projects the API actually
    /// returned. Returns <see cref="Cohort.Empty"/> when the board cannot
    /// support the scenarios — see <see cref="RequiredOpenProjects"/>.</summary>
    public static Cohort Build(AssignmentBoardDto? board)
    {
        if (board is null) return Cohort.Empty;

        var open = board.Projects
            .Where(p => !p.AssignedTeamId.HasValue)
            .OrderBy(p => p.ProjectNumber)
            .Take(RequiredOpenProjects)
            .ToList();

        if (open.Count < RequiredOpenProjects) return Cohort.Empty;

        // The one deliberately unavailable first choice. Falling back to a free
        // project rather than dropping the team keeps the queue's length stable;
        // the scenario is simply softer on a board with nothing assigned.
        var taken = board.Projects.FirstOrDefault(p => p.AssignedTeamId.HasValue) ?? open[0];

        AssignmentBoardProjectDto Resolve(int slot) => slot == TakenSlot ? taken : open[slot];

        var submissions = new List<AssignmentSubmissionListItemDto>();
        var matches     = new Dictionary<int, List<TeamProjectMatchDto>>();

        for (var i = 0; i < Specs.Length; i++)
        {
            var spec   = Specs[i];
            var teamId = TeamIdBase + i;

            var prefs = spec.PrefSlots
                .Select((slot, idx) =>
                {
                    var proj = Resolve(slot);
                    return new AssignmentSubmissionPreferenceDto
                    {
                        Priority      = idx + 1,
                        ProjectId     = proj.ProjectId,
                        ProjectNumber = proj.ProjectNumber,
                        ProjectTitle  = proj.ProjectName,
                        ProjectType   = proj.ProjectType
                    };
                })
                .ToList();

            submissions.Add(new AssignmentSubmissionListItemDto
            {
                TeamId         = teamId,
                TeamName       = spec.Name,
                AcademicYearId = board.AcademicYearId,
                AcademicYear   = board.AcademicYearName,
                SubmittedAt    = spec.SubmittedAt,
                Notes          = spec.Notes,
                Members        = spec.Members.Select((m, mi) => new AssignmentSubmissionMemberDto
                {
                    UserId    = MemberIdBase + (i * 10) + mi,
                    FullName  = m.Name,
                    Email     = m.Mail,
                    Strengths = m.Strengths.ToList()
                }).ToList(),
                Preferences = prefs
            });

            matches[teamId] = BuildMatches(spec, teamId, prefs, open);
        }

        return new Cohort(submissions, matches);
    }

    /// <summary>The team's scored rows: its three preferences at the server's own
    /// <c>rank(30/20/10) + skill</c>, then every other free project in the pool
    /// on skill alone.
    ///
    /// <para>The externals matter: they are what the workspace offers under
    /// חלופות once a team's own list runs out, and a team whose three choices
    /// are all taken would otherwise have no move at all.</para></summary>
    private static List<TeamProjectMatchDto> BuildMatches(
        TeamSpec spec,
        int teamId,
        List<AssignmentSubmissionPreferenceDto> prefs,
        List<AssignmentBoardProjectDto> open)
    {
        static int RankScore(int rank) => rank switch { 1 => 30, 2 => 20, 3 => 10, _ => 0 };

        var rows = prefs.Select((p, idx) => new TeamProjectMatchDto
        {
            TeamId          = teamId,
            TeamName        = spec.Name,
            ProjectId       = p.ProjectId,
            ProjectName     = p.ProjectTitle,
            ProjectType     = p.ProjectType,
            PreferenceRank  = p.Priority,
            PreferenceScore = RankScore(p.Priority),
            SkillScore      = spec.SkillScores[idx],
            TotalMatchScore = RankScore(p.Priority) + spec.SkillScores[idx]
        }).ToList();

        var ranked = prefs.Select(p => p.ProjectId).ToHashSet();

        // Deterministic per team, so a screenshot taken twice is the same
        // screenshot: the id decides the score, never a clock or a random.
        foreach (var proj in open.Where(p => !ranked.Contains(p.ProjectId)))
        {
            var skill = 8 + ((teamId + proj.ProjectId) % 5) * 4;
            rows.Add(new TeamProjectMatchDto
            {
                TeamId          = teamId,
                TeamName        = spec.Name,
                ProjectId       = proj.ProjectId,
                ProjectName     = proj.ProjectName,
                ProjectType     = proj.ProjectType,
                PreferenceRank  = null,
                PreferenceScore = 0,
                SkillScore      = skill,
                TotalMatchScore = skill
            });
        }

        return rows.OrderByDescending(m => m.TotalMatchScore).ToList();
    }
}
