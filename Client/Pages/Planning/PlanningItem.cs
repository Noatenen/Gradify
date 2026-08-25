using System;
using System.Collections.Generic;
using System.Linq;
using AuthWithAdmin.Client.Pages.Tasks;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Pages.Planning;

// ─────────────────────────────────────────────────────────────────────────────
//  PlanningItem — the Planning workspace's view model.
//
//  NOT a DTO. Lives in Client/ deliberately: it is a normalization of three
//  existing payloads (TaskItemDto, TeamTaskDto, PersonalTaskDto) into the one
//  shape the Month / Week / Timeline views all render. Nothing in Shared/ or
//  Server/ changes to support it.
//
//  Every field here traces to a real column. There is no start date, no
//  duration and no dependency field, because the domain has none for tasks —
//  a planning item is a POINT IN TIME. Only milestones carry a real range
//  (AcademicYearMilestones.OpenDate → effective DueDate), and those are
//  modelled separately by PlanningMilestone below.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The workspace's three views. The Design's segmented switch, with
/// גאנט relabelled ציר זמן — see PlanningToolbar for why.</summary>
public enum PlanningView { Month, Week, Timeline }

/// <summary>
/// The five planning types the student workspace can truthfully show.
///
/// The finalized Motiva Calendar defines four (submission / system / team /
/// event). `Event` is absent here because no meetings table, entity, DTO or
/// endpoint exists — see the phase audit. `Mentor` and `Personal` are present
/// because both are backed by real, already-consumed data that the prototype's
/// four-type taxonomy did not account for.
/// </summary>
public enum PlanningKind
{
    /// <summary>Tasks.IsSubmission = 1. The Design's `submission` type.</summary>
    Submission,
    /// <summary>Tasks.TaskType = 'System'. The Design's `system` type.</summary>
    System,
    /// <summary>Tasks.TaskType = 'Mentor'. Net new — not in the Design.</summary>
    Mentor,
    /// <summary>TeamTasks. The Design's `team` type.</summary>
    Team,
    /// <summary>PersonalTasks — student-private. Net new — not in the Design.</summary>
    Personal
}

/// <summary>
/// One dated thing on the student's calendar, normalized from whichever
/// payload it came from. Immutable by construction — rebuilt on every reload.
/// </summary>
public sealed class PlanningItem
{
    /// <summary>Stable render/dedupe key. Kind-prefixed because a Task id and a
    /// TeamTask id can collide — they are different tables.</summary>
    public string Key { get; init; } = "";

    /// <summary>Row id in the source table. Meaningful only together with Kind.</summary>
    public int SourceId { get; init; }

    public PlanningKind Kind { get; init; }

    public string Title { get; init; } = "";

    /// <summary>The due date, date-part only. Items with no due date never
    /// become PlanningItems at all — an undated thing cannot be placed on a
    /// calendar, and inventing a date for it would be fabrication.</summary>
    public DateTime Date { get; init; }

    public bool IsDone { get; init; }

    /// <summary>Past due and still actionable. For project tasks this defers to
    /// TaskStatusHelpers.IsOverdue, which excludes tasks sitting with the
    /// mentor — a delay that is not the student's to resolve. For team and
    /// personal tasks the rule is the simple one their data supports.</summary>
    public bool IsOverdue { get; init; }

    /// <summary>Compact Hebrew status for chips and the detail popover.</summary>
    public string StatusLabel { get; init; } = "";

    /// <summary>Parent milestone. Null for Team and Personal — those two are
    /// structurally unrelated to milestones (TeamTaskDto states this outright),
    /// and the Timeline must never render them inside a milestone band.</summary>
    public int? MilestoneId { get; init; }

    public string? MilestoneTitle { get; init; }

    /// <summary>
    /// Optional work block on <see cref="Date"/>, as Israel wall-clock "HH:mm".
    ///
    /// <para>Only PersonalTaskDto carries this pair today — it is the one kind
    /// whose rows answer "when on that day do I plan to sit with it". Every
    /// other kind is date-only, which is exactly the split the reference draws:
    /// timed items are positioned in the hour grid, undated-in-time items sit
    /// in the כל היום band above it. Both are set together or both are null;
    /// the server refuses a half-set pair.</para>
    /// </summary>
    public string? StartTime { get; init; }

    /// <summary>End of the work block. See <see cref="StartTime"/>.</summary>
    public string? EndTime { get; init; }

    /// <summary>True when this item belongs in the Week view's hour grid rather
    /// than its כל היום band.</summary>
    public bool HasTime => !string.IsNullOrWhiteSpace(StartTime);

    /// <summary>Minutes from midnight, or null when the item is date-only.</summary>
    public int? StartMinutes => ParseMinutes(StartTime);

    /// <summary>Minutes from midnight for the block's end. Falls back to the
    /// start when no end is stored, so a degenerate block still renders at the
    /// grid's minimum height rather than collapsing to zero.</summary>
    public int? EndMinutes => ParseMinutes(EndTime) ?? StartMinutes;

    private static int? ParseMinutes(string? hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return null;
        var parts = hhmm.Split(':');
        if (parts.Length < 2) return null;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return null;
        return h * 60 + m;
    }

    /// <summary>True when this item can open the existing TaskDetailModal.</summary>
    public bool OpensTaskDetail =>
        Kind is PlanningKind.Submission or PlanningKind.System or PlanningKind.Mentor;
}

/// <summary>
/// A point on the calendar the student clicked in EMPTY space, and therefore
/// wants to create something at.
///
/// <para>One type for both grids so a single page handler serves them:
/// <see cref="StartTime"/> is null from the Month grid, which has no hour axis,
/// and "HH:mm" from the Week grid's hour slots.</para>
/// </summary>
/// <param name="Date">The day clicked, date-part only.</param>
/// <param name="StartTime">Israel wall-clock "HH:mm", or null for a whole day.</param>
public sealed record PlanningSlot(DateTime Date, string? StartTime = null);

/// <summary>
/// A milestone as the Timeline needs it: the one entity in the domain with a
/// genuine date RANGE. OpenDate is nullable in the schema, so Start is too —
/// a milestone without one degrades to a marker rather than getting an
/// invented start.
/// </summary>
public sealed class PlanningMilestone
{
    public int      ProjectMilestoneId { get; init; }
    public string   Title              { get; init; } = "";
    /// <summary>AcademicYearMilestones.OpenDate, from /my-tasks. Null → render
    /// as a marker; never substitute a fabricated start.</summary>
    public DateTime? Start             { get; init; }
    /// <summary>Effective due date — per-team override already applied server-side.</summary>
    public DateTime? Due               { get; init; }
    /// <summary>"NotStarted" | "InProgress" | "Completed" | "Delayed"</summary>
    public string   Status             { get; init; } = "";
    public int      OrderIndex         { get; init; }
    /// <summary>Server-derived: today is inside the OpenDate…CloseDate window.</summary>
    public bool     IsCurrentlyOpen    { get; init; }
    /// <summary>The single milestone the server marks as "current".</summary>
    public bool     IsCurrent          { get; init; }
    /// <summary>Any task in this milestone has a returned submission.</summary>
    public bool     HasReturnedTask    { get; init; }
    public int      DoneCount          { get; init; }
    public int      TotalCount         { get; init; }

    /// <summary>Pre-calculated server-side. Taken rather than recomputed: the
    /// server applies the "Completed with zero tasks → 100" rule, which a local
    /// DoneCount/TotalCount division gets wrong (a completed milestone with
    /// 0/1 counted tasks is 100%, not 0%).</summary>
    public int      ProgressPct        { get; init; }

    /// <summary>True only when a real range exists and is non-degenerate.</summary>
    public bool HasBand => Start.HasValue && Due.HasValue && Due.Value.Date >= Start.Value.Date;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Type metadata — the Design's chip semantics, expressed as CSS class stems.
//
//  Colours are NOT hardcoded here. Each kind maps to a class stem
//  (`pl-k-submission` …) and the stylesheet binds that stem to an existing
//  Motiva categorical token. Keeping the colour in CSS is what lets the
//  palette stay theme-aware without this file knowing about themes.
// ─────────────────────────────────────────────────────────────────────────────

public static class PlanningKinds
{
    public static string Slug(PlanningKind k) => k switch
    {
        PlanningKind.Submission => "submission",
        PlanningKind.System     => "system",
        PlanningKind.Mentor     => "mentor",
        PlanningKind.Team       => "team",
        _                       => "personal"
    };

    public static string Label(PlanningKind k) => k switch
    {
        PlanningKind.Submission => "הגשה",
        PlanningKind.System     => "משימת מערכת",
        PlanningKind.Mentor     => "משימת מנחה",
        PlanningKind.Team       => "משימת צוות",
        _                       => "אישי"
    };

    /// <summary>Filter-chip label — plural, shorter than the singular type name.</summary>
    public static string FilterLabel(PlanningKind k) => k switch
    {
        PlanningKind.Submission => "הגשות",
        PlanningKind.System     => "משימות מערכת",
        PlanningKind.Mentor     => "משימות מנחה",
        PlanningKind.Team       => "משימות צוות",
        _                       => "אישי"
    };

    /// <summary>
    /// The SVG path the Design assigns each type (Motiva Calendar.dc.html:454–459).
    /// Submission keeps its document glyph, system its lines, team its check.
    /// Mentor and Personal are net new: a speech bubble (guidance arriving from
    /// someone else) and a bookmark (private, self-set).
    /// </summary>
    public static string IconPath(PlanningKind k) => k switch
    {
        PlanningKind.Submission => "M7 3h7l5 5v13H7z",
        PlanningKind.System     => "M4 6h16M4 12h16M4 18h10",
        PlanningKind.Team       => "M5 12.5 9.5 17 19 7",
        PlanningKind.Mentor     => "M21 11.5a8.4 8.4 0 0 1-9 8.4L3 21l1.1-8A8.4 8.4 0 1 1 21 11.5z",
        _                       => "M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"
    };

    /// <summary>
    /// The type palette, emitted as inline custom properties.
    ///
    /// WHY INLINE AND NOT A STYLESHEET RULE
    /// Blazor CSS isolation stamps every scoped rule with the defining
    /// component's [b-*] attribute, so a `.pl-k-submission` class declared in
    /// PlanningItemChip.razor.css matches nothing inside PlanningFilterChips or
    /// PlanningTimeline. Custom properties set inline are unscoped and inherit
    /// normally, so all four components read one palette from one place.
    ///
    /// The five slots are the FINAL Calendar's own five types, taken at their
    /// exact ink/tint pairs. Motiva's five kinds map onto them one-for-one,
    /// which is why no slot is invented and none is left over:
    ///
    ///   הגשה        submit    #C42B52 — the reference's own הגשה
    ///   משימת מערכת task      #2E63E8 — the reference's own משימה
    ///   משימת מנחה  meeting   #6D28F5 — the violet "guidance" slot the
    ///                                   reference fills with פגישת הנחיה
    ///   משימת צוות  team      #0F8FA6 — the reference's own משימת צוות
    ///   אישי        milestone #4A4657 — the reference's quietest slot
    ///
    /// Values are literal rather than token references because these are
    /// CATEGORICAL, not semantic: --motiva-color-danger and the הגשה tint would
    /// resolve to the same rose while meaning two different things, and a
    /// future change to the danger role must not silently repaint a task type.
    /// The literals are the student scope's own palette either way (see
    /// motiva-tokens.css: the four roles plus their ink weights).
    /// </summary>
    public static string PaletteStyle(PlanningKind k) => k switch
    {
        PlanningKind.Submission =>
            "--pl-ink:#C42B52;--pl-tint:rgba(224,51,95,.10);",
        PlanningKind.System =>
            "--pl-ink:#2E63E8;--pl-tint:rgba(46,99,232,.10);",
        PlanningKind.Mentor =>
            "--pl-ink:#6D28F5;--pl-tint:rgba(109,40,245,.10);",
        PlanningKind.Team =>
            "--pl-ink:#0F8FA6;--pl-tint:rgba(15,168,194,.12);",
        _ =>
            "--pl-ink:#4A4657;--pl-tint:rgba(27,26,34,.07);"
    };

    /// <summary>Display order — mirrors the filter row, most official first.</summary>
    public static readonly PlanningKind[] All =
    {
        PlanningKind.Submission,
        PlanningKind.System,
        PlanningKind.Mentor,
        PlanningKind.Team,
        PlanningKind.Personal
    };
}

// ─────────────────────────────────────────────────────────────────────────────
//  Normalization — the single place raw payloads become PlanningItems.
//
//  Month, Week and Timeline all consume the output of this class and nothing
//  else, which is what keeps "the same items, the same filters" true across
//  the three views instead of a promise three code paths have to keep.
// ─────────────────────────────────────────────────────────────────────────────

public static class PlanningMapper
{
    /// <summary>
    /// Flattens a TasksPageDto into planning items.
    ///
    /// Reads BOTH ActiveGroups and CompletedGroups: the endpoint pre-splits
    /// them (active groups carry only non-Done tasks, completed groups only
    /// Done ones), so a calendar that read one list would silently answer
    /// "מה כבר הושלם?" with nothing.
    ///
    /// Tasks with no DueDate are skipped — they have no position on a calendar.
    /// </summary>
    public static List<PlanningItem> FromTasks(TasksPageDto? tasks)
    {
        var result = new List<PlanningItem>();
        if (tasks is null) return result;

        var seen = new HashSet<int>();

        foreach (var group in tasks.ActiveGroups.Concat(tasks.CompletedGroups))
        {
            foreach (var t in group.Tasks)
            {
                if (!t.DueDate.HasValue) continue;
                if (!seen.Add(t.Id)) continue;   // a milestone can appear in both lists

                result.Add(new PlanningItem
                {
                    Key            = $"task-{t.Id}",
                    SourceId       = t.Id,
                    Kind           = ResolveTaskKind(t),
                    Title          = t.Title,
                    Date           = t.DueDate.Value.Date,
                    IsDone         = TaskStatusHelpers.GetCategory(t) == TaskStatusHelpers.TaskCategory.Completed,
                    IsOverdue      = TaskStatusHelpers.IsOverdue(t),
                    StatusLabel    = StatusLabelFor(t),
                    MilestoneId    = group.ProjectMilestoneId,
                    MilestoneTitle = group.MilestoneTitle
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Submission wins over TaskType: a submission task is a submission first,
    /// which is how the Design colours it and how the student thinks about it.
    /// A 'Personal' TaskType row is a project task the student owns — it is
    /// NOT a PersonalTask (different table entirely), so it maps to System
    /// rather than to the personal kind.
    /// </summary>
    private static PlanningKind ResolveTaskKind(TaskItemDto t)
    {
        if (t.IsSubmission)          return PlanningKind.Submission;
        if (t.TaskType == "Mentor")  return PlanningKind.Mentor;
        return PlanningKind.System;
    }

    /// <summary>
    /// Compact chip-sized status. Delegates the LOGIC to TaskStatusHelpers —
    /// this only shortens the result for a 12px chip, where the full labels
    /// used by TaskDetailModal ("הוחזר לתיקונים" etc.) do not fit.
    /// </summary>
    private static string StatusLabelFor(TaskItemDto t) =>
        TaskStatusHelpers.ResolveDisplayStatus(t) switch
        {
            "Done"                  => "הושלם",
            "ReturnedByMentor"      => "הוחזר",
            "ReturnedForRevision"   => "הוחזר",
            "PendingMentorReview"   => "בבדיקה",
            "ApprovedByMentor"      => "מאושר להגשה",
            "InProgress"            => "בתהליך",
            "SubmittedToMentor"     => "נשלח למנחה",
            "RevisionSubmitted"     => "תוקן ונשלח",
            "ApprovedForSubmission" => "מאושר להגשה",
            _                       => "פתוח"
        };

    public static List<PlanningItem> FromTeamTasks(IEnumerable<TeamTaskDto>? teamTasks)
    {
        var result = new List<PlanningItem>();
        if (teamTasks is null) return result;

        foreach (var t in teamTasks)
        {
            if (!t.DueDate.HasValue) continue;

            var date = t.DueDate.Value.Date;
            result.Add(new PlanningItem
            {
                Key         = $"team-{t.Id}",
                SourceId    = t.Id,
                Kind        = PlanningKind.Team,
                Title       = t.Title,
                Date        = date,
                IsDone      = t.IsDone,
                IsOverdue   = !t.IsDone && date < DateTime.Today,
                StatusLabel = t.IsDone ? "הושלם" : "פתוח"
                // MilestoneId deliberately null: TeamTasks have no milestone
                // relationship in the schema, and the Timeline relies on that.
            });
        }

        return result;
    }

    public static List<PlanningItem> FromPersonalTasks(IEnumerable<PersonalTaskDto>? personalTasks)
    {
        var result = new List<PlanningItem>();
        if (personalTasks is null) return result;

        foreach (var t in personalTasks)
        {
            if (!t.DueDate.HasValue) continue;

            var date = t.DueDate.Value.Date;
            result.Add(new PlanningItem
            {
                Key         = $"personal-{t.Id}",
                SourceId    = t.Id,
                Kind        = PlanningKind.Personal,
                Title       = t.Title,
                Date        = date,
                IsDone      = t.IsDone,
                IsOverdue   = !t.IsDone && date < DateTime.Today,
                StatusLabel = t.IsDone ? "הושלם" : "פתוח",
                // The only kind with a time of day. Carried through so the Week
                // view can place it in the hour grid instead of the כל היום band.
                StartTime   = t.StartTime,
                EndTime     = t.EndTime
            });
        }

        return result;
    }

    /// <summary>
    /// Milestones, built from BOTH student endpoints because neither is
    /// sufficient alone:
    ///
    ///   /my-milestones  is the complete LIST. It returns every milestone on
    ///                   the project, and carries the server's ProgressPct,
    ///                   IsCurrent and HasReturnedTask. What it does NOT carry
    ///                   is OpenDate — only DueDate.
    ///
    ///   /my-tasks       carries OpenDate (and CloseDate, and the team-effective
    ///                   DueDate) per milestone — the range the Timeline's bands
    ///                   are made of. But it groups BY TASK, so a milestone with
    ///                   zero tasks appears in neither ActiveGroups nor
    ///                   CompletedGroups and would silently vanish.
    ///
    /// So: take the roster from /my-milestones, then left-join the date window
    /// from /my-tasks. A task-less milestone keeps its place on the timeline and
    /// simply has no OpenDate, which degrades it to a marker — the documented
    /// fallback, not a special case.
    /// </summary>
    public static List<PlanningMilestone> FromMilestones(
        MilestonesPageDto? milestones, TasksPageDto? tasks)
    {
        if (milestones is null) return new List<PlanningMilestone>();

        // Window lookup, merged across the endpoint's active/completed split —
        // one milestone can legitimately appear in both lists.
        var windows = new Dictionary<int, TaskMilestoneGroupDto>();
        if (tasks is not null)
        {
            foreach (var g in tasks.ActiveGroups.Concat(tasks.CompletedGroups))
                windows.TryAdd(g.ProjectMilestoneId, g);
        }

        return milestones.Milestones
            .Select(m =>
            {
                windows.TryGetValue(m.ProjectMilestoneId, out var w);
                return new PlanningMilestone
                {
                    ProjectMilestoneId = m.ProjectMilestoneId,
                    Title              = m.Title,
                    Start              = w?.OpenDate?.Date,
                    // Prefer the team-effective due date from /my-tasks when it
                    // exists; fall back to the roster's own value otherwise.
                    Due                = (w?.DueDate ?? m.DueDate)?.Date,
                    Status             = m.Status,
                    OrderIndex         = m.OrderIndex,
                    IsCurrentlyOpen    = w?.IsCurrentlyOpen ?? false,
                    IsCurrent          = m.IsCurrent,
                    HasReturnedTask    = m.HasReturnedTask,
                    DoneCount          = m.CompletedTasks,
                    TotalCount         = m.TotalTasks,
                    ProgressPct        = m.ProgressPct
                };
            })
            .OrderBy(m => m.OrderIndex)
            .ToList();
    }
}
