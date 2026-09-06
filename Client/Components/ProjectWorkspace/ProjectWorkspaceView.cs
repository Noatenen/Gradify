using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Components;

// ─────────────────────────────────────────────────────────────────────────────
//  מרחב הפרויקט — the role-blind view model
//
//  ONE COMPOSITION, TWO ROLES. A lecturer and a mentor open the same project
//  and ask the same five questions in the same order:
//
//      1. what project / team am I looking at        → Identity
//      2. where are they in the journey              → Identity.StageName + Stages
//      3. what is happening now                      → Work
//      4. what has just changed                      → Activity
//
//  There is no "upcoming dates" section, deliberately. A generic list of
//  future dates answers none of those questions on its own: a milestone's
//  date is on the journey strip, a deliverable's is on its own row in
//  הגשות, a task's is on its row in משימות, and a request's age is on the
//  attention row that is waiting for it. A date matters HERE, beside the
//  thing it constrains — a second card repeating them was a calendar
//  standing in for a workspace.
//
//  What DIFFERS between the two roles is which rows those sections contain and
//  which of them can be acted on — never the shape of the page. So this file
//  carries no data access and no role check: each host builds this record from
//  the endpoints its own role is authorised for, and ProjectWorkspace.razor
//  draws whatever it is handed.
//
//  EVERY STRING HERE IS ALREADY DISPLAY-READY. The view model deliberately
//  holds no DTOs, no status codes and no dates-to-be-formatted: a section that
//  formats its own values is a section that can format them differently from
//  the one beside it. Hosts translate once, here, on the way in.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// WHICH WORKSPACE THIS IS — the entry context, and the only thing that
/// decides role-specific wording and controls on this page.
///
/// <para><b>It comes from the ROUTE, never from the role list.</b> The two
/// workspaces are two routes: <c>/projects/{id}/overview</c> is the lecturer's
/// and <c>/mentor/projects/{id}</c> is the mentor's. Each host declares its
/// context as a literal constant, so a user who holds BOTH Staff and Mentor
/// gets the workspace they navigated into rather than whichever role a
/// predicate happened to test first.</para>
///
/// <para><b>Why not <c>IsInRole</c>.</b> A dual-role account satisfies
/// <c>IsMentor</c> AND <c>IsAdminOrStaff</c> at the same time, so any check of
/// the form "if they are a mentor, show mentor wording" resolves to mentor on
/// BOTH routes and the lecturer's own workspace starts addressing them as
/// somebody else. Roles still decide what a user MAY do — the server's
/// [Authorize] gates and each host's own capability checks are unchanged.
/// Context decides only which of the two workspaces they are looking at.</para>
///
/// <para><b>Why not UserModeService.</b> The מצב מרצה / מצב מנחה toggle drives
/// the shell's navigation and the landing route, and it is global and sticky.
/// A mentor-mode user who follows a lecturer link to a project has still
/// entered the lecturer workspace, and the page must say so. The route is the
/// narrower and more recent signal, so it wins.</para>
/// </summary>
public enum PwContext
{
    /// <summary>Reached through /projects/{id}/overview — הצוותים שלי and the
    /// rest of the lecturer flow.</summary>
    Lecturer,

    /// <summary>Reached through /mentor/projects/{id} — פרויקטים בהנחייתי and
    /// the rest of the mentor flow.</summary>
    Mentor,
}

/// <summary>
/// What this viewer may do with ONE request — the server's answer, carried
/// verbatim.
///
/// <para><b>It is per ROW, not per page.</b> It used to be
/// <c>(IsProjectMentor, IsStaff)</c>: two facts about the READER, built once
/// per page, from which the wording re-derived whether the reader could act.
/// That derivation cannot be right, because whether a request can be decided
/// also depends on the REQUEST — whether its mentor stage completed, whether
/// the lecturer already ruled, whether it is closed. The row said
/// "ממתינה להחלטתך" from the reader's roles alone while the detail surface
/// offered no decision, because only the surface had asked the server.</para>
///
/// <para><paramref name="CanRecommend"/> and <paramref name="CanDecide"/> come
/// from <c>ExtensionWorkflow.Resolve</c>, which also gates the two POST
/// endpoints — so a label promising an action and the action itself are one
/// answer. <paramref name="MentorStageComplete"/> is carried so a request that
/// never reached the lecturer can say what is really holding it up rather than
/// naming a stage it has not got to. <paramref name="IsStaff"/> stays a fact
/// about the reader and is used only where no capability applies: the
/// non-extension statuses academic staff answer through their own flows.</para>
///
/// <para><paramref name="IsProjectMentor"/> is the server's ProjectMentors
/// answer for THIS request's project — not "holds the Mentor role", so a mentor
/// of some other project is false here. It exists because the two capability
/// flags above are Extension-only by construction: ExtensionWorkflow.Resolve
/// returns (false, false) for every requestType that is not an Extension, which
/// left a plain mentor reading the third-person "ממתינה להמלצת מנחה" about a
/// non-extension request that was waiting on nobody but them. Defaulted so the
/// lecturer workspace's construction is unchanged; see AwaitsThisMentor for the
/// single, staff-excluded rule it feeds.</para>
/// </summary>
public readonly record struct PwRequestViewer(
    bool IsStaff,
    bool CanRecommend,
    bool CanDecide,
    bool MentorStageComplete,
    bool IsProjectMentor = false);

/// <summary>
/// Request status wording, resolved from WHAT THIS VIEWER MAY ACTUALLY DO.
///
/// <para><b>The rule that was wrong the first time.</b> The label was chosen
/// from the workspace's PwContext, so the lecturer's page said "ממתינה להחלטתך"
/// for anything a lecturer could SEE. Visibility is not ownership.</para>
///
/// <para><b>The rule that was still wrong.</b> Ownership was then re-derived on
/// the client from the status plus the reader's roles. That is right about which
/// stage owns a request and silent about whether the request ever reached it —
/// so one sitting at PendingLecturerDecision whose mentor had advised in the
/// RETIRED vocabulary read as "ממתינה להחלטתך" while the endpoint refused the
/// decision, and one pushed to that status with no recommendation at all read
/// the same way while nobody could act on it.</para>
///
/// <para><b>The rule now.</b> The second person is spoken only where the server
/// has said this viewer may act: "ממתינה להחלטתך" if and only if
/// <see cref="PwRequestViewer.CanDecide"/>, "ממתינה להמלצתך" if and only if
/// <see cref="PwRequestViewer.CanRecommend"/>. Everything else is third person,
/// naming the party that owes the move. One implementation for both workspaces
/// and every request type.</para>
/// </summary>
public static class PwRequestVocabulary
{
    /// <summary>
    /// THE ONE MENTOR-ONLY RULE. True when the reader is the plain (non-staff)
    /// assigned mentor of this project and the request's status says the mentor
    /// stage is what everyone is waiting for.
    ///
    /// <para><b>Why it is needed.</b> ExtensionWorkflow.Resolve short-circuits
    /// to <c>(CanRecommend: false, CanDecide: false)</c> for every requestType
    /// that is not an Extension. A plain mentor also has IsStaff false, so all
    /// three arms of <see cref="OwnedByViewer"/> were dead for them and a
    /// non-extension request sitting at PendingMentorRecommendation — waiting on
    /// that very reader — was worded in the third person, "ממתינה להמלצת מנחה",
    /// carried PwTone.Waiting instead of Attention, and ranked as context.</para>
    ///
    /// <para><b>Why it is safe.</b> Three conjuncts, each doing real work.
    /// <c>!IsStaff</c> makes the rule structurally inert for every Admin/Staff
    /// reader, so no lecturer result can move. <c>IsProjectMentor</c> is the
    /// server's ProjectMentors row, so a mentor looking at a request awaiting a
    /// DIFFERENT project's mentor stays false — ownership is never inferred from
    /// the status alone. And pinning the status to PendingMentorRecommendation
    /// keeps every other status, terminal ones included, exactly where it
    /// was.</para>
    ///
    /// <para><b>Why Extension behaviour cannot change.</b> For an Extension at
    /// this status the assigned mentor already has CanRecommend true, and every
    /// consumer below tests the capability flags FIRST — so this rule is only
    /// ever reached where the flags are false, and returns the same answer the
    /// capability would have given.</para>
    /// </summary>
    private static bool AwaitsThisMentor(string status, PwRequestViewer viewer) =>
        !viewer.IsStaff
        && viewer.IsProjectMentor
        && status is RequestStatuses.PendingMentorRecommendation;

    public static string StatusLabel(string status, PwRequestViewer viewer) => status switch
    {
        // New requests show no state badge — the action label already
        // communicates that this is waiting on the viewer.
        RequestStatuses.New => "",

        // Owner: the assigned mentor — unless this viewer also holds final
        // authority, in which case the server opens the combined decision and
        // the label says so.
        RequestStatuses.PendingMentorRecommendation =>
            viewer.CanDecide      ? "ממתינה להחלטתך"
            : viewer.CanRecommend ? "ממתינה להמלצתך"
            // Same second person, same words: this reader IS the mentor the
            // third-person arm below would otherwise be describing to them.
            : AwaitsThisMentor(status, viewer) ? "ממתינה להמלצתך"
            :                       "ממתינה להמלצת מנחה",

        // Owner: academic staff — but ONLY once the request actually reached
        // them. A row whose mentor stage never completed names what is really
        // holding it up, which is the same thing the endpoint answers.
        RequestStatuses.PendingLecturerDecision =>
            viewer.CanDecide             ? "ממתינה להחלטתך"
            : !viewer.MentorStageComplete ? "ממתינה להמלצת מנחה"
            :                              "ממתינה להחלטת מרצה",

        RequestStatuses.WaitingForStaff =>
            viewer.IsStaff ? "ממתינה למענה שלך" : "ממתינה למענה אקדמי",

        // Owner: the team. Neither reader, ever.
        RequestStatuses.NeedsInfo => "ממתינה להשלמת פרטים מהצוות",

        RequestStatuses.Resolved => "טופלה",
        RequestStatuses.Closed   => "נסגרה",

        // No stage owns these; the shared neutral label already reads
        // correctly for everyone.
        _ => RequestStatuses.Label(status),
    };

    /// <summary>True when the next move on this request is THIS viewer's — the
    /// server's own answer, plus the non-extension statuses academic staff owe
    /// a reply to and for which there is no capability flag.</summary>
    public static bool OwnedByViewer(string status, PwRequestViewer viewer) =>
        viewer.CanDecide
        || viewer.CanRecommend
        || (viewer.IsStaff && status is RequestStatuses.WaitingForStaff
                                     or RequestStatuses.New
                                     or RequestStatuses.InProgress)
        // The plain mentor this request is actually waiting on. Staff-excluded,
        // so nothing an Admin/Staff reader sees can move — see AwaitsThisMentor.
        || AwaitsThisMentor(status, viewer);

    /// <summary>
    /// The row's call to action, or null when the next move is not this
    /// viewer's.
    ///
    /// <para>Derived from the same capability as the label, so a row cannot
    /// offer a button the endpoint would refuse — nor withhold one the endpoint
    /// would accept, which is the failure this pass fixed.</para>
    ///
    /// <para>CanDecide is tested FIRST, and the order is load-bearing: a
    /// dual-role assigned mentor holds BOTH capabilities on a request awaiting
    /// recommendation, and the detail surface resolves that overlap by drawing
    /// the decision (there is no one to recommend to). Testing CanRecommend
    /// first put "להמלצה" on a row whose status said "ממתינה להחלטתך" and whose
    /// surface offered a decision — the same three-way disagreement in
    /// miniature.</para>
    /// </summary>
    public static string? ActionLabel(string status, PwRequestViewer viewer) =>
        !OwnedByViewer(status, viewer) ? null
        // New requests: the viewer hasn't interacted yet — "waiting for your
        // response" is more accurate than "to decision" at this stage.
        : status is RequestStatuses.New ? "ממתינה לתגובתך"
        : viewer.CanDecide             ? "להחלטה"
        : viewer.CanRecommend          ? "להמלצה"
        // OWNED, BUT DELIBERATELY BUTTONLESS. This mentor is who the request
        // waits on, yet no endpoint would take a decision from them here:
        // /mentor-recommendation answers "המלצת מנחה תקפה רק לבקשות דחייה" for
        // anything that is not an Extension, and /handle is [Authorize(Admin,
        // Staff)]. Offering "להמלצה" or "להחלטה" would be exactly the promise
        // this class exists to prevent, so the row carries the second-person
        // STATE and no action; it stays clickable, and the thread it opens has
        // the reply that genuinely is available.
        : AwaitsThisMentor(status, viewer) ? null
        // Staff-owned non-extension statuses: they owe a reply through their
        // own flow, and the wording they have always had is kept. Reachable
        // only with IsStaff true, since the arm above claims the other case.
        :                                "להחלטה";

    /// <summary>Row tone from the same predicate, in the shared vocabulary:
    /// Done when the thread is closed, Attention when the next move is THIS
    /// viewer's, and Waiting for everything sitting with somebody else —
    /// ממתינה להמלצת מנחה, ממתינה להחלטת מרצה, ממתינה לצוות. Those are workflow
    /// state, not an alert for the person reading them.</summary>
    public static PwTone Tone(string status, PwRequestViewer viewer) =>
        status is RequestStatuses.Resolved or RequestStatuses.Closed ? PwTone.Done
        : OwnedByViewer(status, viewer)                              ? PwTone.Attention
        :                                                              PwTone.Waiting;
}

/// <summary>
/// The workspace's semantic colour vocabulary — FIVE ROLES, NAMED BY MEANING.
///
/// <para><b>They used to be named by colour</b> (Blue / Violet / Teal), and
/// that is most of why the three work types drifted apart: nothing stopped
/// "submitted, now with the mentor" being Blue under הגשות while the same
/// idea was Quiet under בקשות, or "waiting for MY review" being Late here and
/// Violet on the mentor's copy of this very page. A role you can only name by
/// its colour is a role you can assign by taste.</para>
///
/// <para><b>Each one resolves to a treatment the shared system already
/// owns</b> — MStatusBadge's own rungs, which the mentor and lecturer screens
/// render. Mapped to CSS in exactly one place per surface
/// (ProjectWorkspace.razor.css, PwDetail.razor.css) so a row cannot invent a
/// sixth colour, and no new token or hex is introduced anywhere.</para>
///
/// <para><b>Blue is gone as a status role.</b> Inside the Motiva scope
/// <c>--motiva-color-info</c> is aliased to <c>--motiva-color-violet</c>
/// (motiva-tokens.css), so an "info" chip and a "brand" chip were two violets
/// standing for two different meanings on the same row.</para>
/// </summary>
public enum PwTone
{
    /// <summary>Ordinary information — a title, a date, a state nobody is
    /// blocked on. Neutral surface, secondary ink. (m-badge-neutral)</summary>
    Quiet,

    /// <summary>In flight with SOMEBODY ELSE — submitted and with the mentor,
    /// returned to the team, awaiting another role's decision. Real workflow
    /// state, and none of the reader's business to act on, so it is the quiet
    /// rung with a hairline rather than a colour. (m-badge-approaching)</summary>
    Waiting,

    /// <summary>The CURRENT viewer owns the next action — ממתין לאישורך,
    /// ממתין לבדיקה שלך, a request awaiting their decision. The system's own
    /// attention rung: a brand wash under dark ink, documented in
    /// MStatusBadge as "deliberately BELOW Rose in meaning" precisely because
    /// none of these has a deadline to be past. (m-badge-attention)</summary>
    Attention,

    /// <summary>Completed or closed. The quiet success treatment every other
    /// Lecturer/Mentor screen uses. (m-badge-teal)</summary>
    Done,

    /// <summary>A genuine negative state — overdue, missing, blocked. Rose,
    /// and nothing else in this vocabulary is. (m-badge-rose)</summary>
    Late,
}

public enum PwStageState { Future, Done, Current, Late }

/// <summary>One chip on the identity line — project type, mentors, anything a
/// role wants named beside the team.</summary>
public sealed record PwMeta(string Text, string? Icon = null);

/// <summary>
/// One of the team's working links — חומרי הפרויקט.
///
/// <para>These are the team's OWN ProjectResources rows, read-only here: the
/// Drive folder, the spec, the design file, the repository. Nothing is
/// invented and nothing is uploaded from this page — a supervisor gets to the
/// materials they are supervising, which until now only the team could
/// reach.</para>
///
/// <para><paramref name="IsPrimary"/> marks the one worth a direct click. It
/// is the shared folder when the team recorded one, recognised from the URL
/// rather than from a label anyone has to type a particular way.</para>
/// </summary>
public sealed record PwMaterial(string Label, string Url, bool IsPrimary);

public static class PwMaterials
{
    /// <summary>
    /// Turns the team's links into the header's list, marking at most one as
    /// the direct action.
    ///
    /// <para>The folder is recognised by host — a Google Drive folder URL —
    /// because that is a fact about the link, whereas its label is free text
    /// the team typed ("תיקיית הפרויקט", "Drive", "הכל נמצא פה"). Matching on
    /// the label would work for one team and quietly fail for the next.</para>
    /// </summary>
    public static IReadOnlyList<PwMaterial> From(IEnumerable<ProjectResourceDto>? rows)
    {
        var list = (rows ?? Enumerable.Empty<ProjectResourceDto>()).ToList();
        if (list.Count == 0) return Array.Empty<PwMaterial>();

        var folder = list.FirstOrDefault(r => IsSharedFolder(r.Url));

        return list
            .Select(r => new PwMaterial(r.Label, r.Url, folder is not null && r.Id == folder.Id))
            .ToList();
    }

    private static bool IsSharedFolder(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && url.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase)
        && url.Contains("/folders/", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Who this is, where they are, and how far along.
///
/// <para><paramref name="ProgressPct"/> is MILESTONE completion on both roles —
/// the project's primary progress figure. Task completion is secondary and
/// belongs inside the work area, which is where <see cref="PwSection.Note"/>
/// carries it.</para>
/// </summary>
public sealed record PwIdentity(
    string Title,
    string? TeamName,
    string StageName,
    int ProgressPct,
    string StatusText,
    MStatusBadge.BadgeVariant StatusVariant,
    IReadOnlyList<string> Members,
    IReadOnlyList<PwMeta> Meta,
    IReadOnlyList<PwMaterial> Materials)
{
    public static readonly PwIdentity Empty = new(
        "", null, "", 0, "", MStatusBadge.BadgeVariant.Neutral,
        Array.Empty<string>(), Array.Empty<PwMeta>(), Array.Empty<PwMaterial>());
}

/// <summary>One cell of the roadmap strip. <paramref name="Date"/> is already
/// worded ("עד 14.03", "באיחור מ־02.03", "הושלם", "—").</summary>
public sealed record PwStage(string Name, string Date, PwStageState State);

/// <summary>
/// WHAT A ROW IS FOR, and therefore whether it is drawn at all.
///
/// <para>The workspace is an operational surface, not the project database
/// rendered as lists. Every row in every section is classified into one of
/// four ranks by the host that built it, and the rank — not the row's own
/// order in the payload — decides its prominence:</para>
///
/// <list type="table">
/// <item><term>Waiting</term><description>the next move is THIS viewer's.
/// Always drawn, always first.</description></item>
/// <item><term>Exception</term><description>late, missing or stuck, whoever
/// owns it. Always drawn, straight after the viewer's own.</description></item>
/// <item><term>Context</term><description>already handled — submitted,
/// reviewed, resolved, in progress. Useful for orientation, so a couple are
/// drawn quietly; the rest wait behind "לכל…".</description></item>
/// <item><term>Dormant</term><description>nothing has happened and nothing is
/// required: a deliverable that is not due yet, a task nobody has started, an
/// old resolved item. NOT DRAWN. It is reachable through "לכל…" and nowhere
/// else.</description></item>
/// </list>
///
/// <para>The rank a row gets is the SAME fact its label and its tone already
/// state — hosts derive it from the predicate they already had, never from a
/// second rule that could disagree with the first.</para>
/// </summary>
public enum PwRowRank
{
    Waiting,
    Exception,
    Context,

    /// <summary>Hidden by default. This is the rank most rows on a healthy
    /// project have, and drawing them is what turned a workspace into an
    /// archive.</summary>
    Dormant,
}

/// <summary>
/// One row inside a work section.
///
/// <para>Deliberately loose: הגשות, בקשות and משימות genuinely carry different
/// facts, and forcing them into fixed columns would either drop one or pad the
/// others. Each host fills the slots its rows actually have, and the ones left
/// null simply do not render.</para>
///
/// <para>FILL AS FEW OF THEM AS THE ROW NEEDS. A title, a state and — when it
/// is genuinely late — a date are enough to know what something is and what is
/// wrong with it. A milestone name, a request type and an assignee beside them
/// are four facts competing for one line, three of which the detail surface
/// states properly the moment the row is opened.</para>
///
/// <para>There is no <c>Lead</c> chip any more. It printed "שלי" / "צוות" next
/// to an assignee line that already said which — two ownership signals on one
/// row — and no host had filled it since.</para>
///
/// <para><c>ActionLabel</c> is the row's own call to action, null when it has
/// none; <c>ActionIsPrimary</c> distinguishes a real action (the gradient CTA)
/// from a quiet affordance. <c>IsClickable</c> means the whole row opens
/// something, and <c>IsOpen</c> that the detail surface currently on screen is
/// THIS row's — which is what keeps "which item am I looking at" answerable
/// while a modal covers the page.</para>
///
/// <para><c>Rank</c> defaults to <see cref="PwRowRank.Context"/>: a row whose
/// host forgot to classify it degrades to "shown, quietly" rather than
/// vanishing from the page.</para>
/// </summary>
public sealed record PwWorkRow(
    string Key,
    string Title,
    string? Meta = null,
    string? Trailing = null,
    bool TrailingLate = false,
    string? State = null,
    PwTone StateTone = PwTone.Quiet,
    string? ActionLabel = null,
    bool ActionIsPrimary = false,
    bool IsClickable = false,
    bool IsOpen = false,
    PwRowRank Rank = PwRowRank.Context);

/// <summary>
/// One of the three work sections — הגשות, בקשות, משימות.
///
/// <para>THEY ARE NOT TABS, AND THEY ARE NOT LISTS OF EVERYTHING. Three
/// categories behind one tab bar meant a reader had to visit each in turn to
/// reconstruct the project in front of them. Three complete categories on the
/// page meant reading twenty rows to find the two that mattered. What a
/// section draws by default is: everything waiting on this viewer, everything
/// late, and up to <paramref name="ContextPreview"/> recently-handled rows for
/// orientation. Nothing else — see <see cref="PwRowRank"/>.</para>
///
/// <para><paramref name="AllHref"/> is the CROSS-project queue this category
/// belongs to. The section's own "לכל…" is a separate, local affordance that
/// reveals every row it holds, including the dormant ones — full history stays
/// one click away, it just stops competing with live work.</para>
///
/// <para><paramref name="Icon"/> is the open-iconic glyph on the heading's
/// chip. Every section on the reference screens is introduced by one, and it
/// is what lets a reader find הגשות on the page by shape before they have read
/// a word of it.</para>
///
/// <para><paramref name="MoreLabel"/> is the wording of that local affordance
/// ("לכל ההגשות"), and the host writes it out. It was briefly derived by
/// prefixing the title with the Hebrew definite article, which is right for
/// בקשות → הבקשות and wrong for הגשות: that word BEGINS with ה, so a guard
/// against double-prefixing skipped it and the button read "לכל הגשות".
/// Morphology is not something a layout component should be inferring.</para>
///
/// <para><paramref name="Summary"/> is the small count/status line beside the
/// title, and it is what a section says when it has nothing to show.
/// <paramref name="Note"/> is the section's own secondary line, where
/// task-completion counts live.</para>
/// </summary>
public sealed record PwSection(
    string Key,
    string Title,
    string Icon,
    IReadOnlyList<PwWorkRow> Rows,
    string EmptyText,
    string? Summary = null,
    PwTone SummaryTone = PwTone.Quiet,
    string? AllHref = null,
    string? AllLabel = null,
    string? MoreLabel = null,
    string? Note = null,
    int ContextPreview = 2)
{
    /// <summary>Everything, by rank. OrderBy is stable, so the order the host
    /// chose inside each rank survives untouched — which is how "newest first"
    /// and "milestone order" keep working without this file knowing about
    /// either.</summary>
    public IReadOnlyList<PwWorkRow> Ordered =>
        Rows.OrderBy(r => (int)r.Rank).ToList();

    /// <summary>The viewer's own items and the exceptions — the section's
    /// reason to exist. Never capped: capping the things that need doing is
    /// the one economy this design cannot make.</summary>
    public IReadOnlyList<PwWorkRow> Active =>
        Rows.Where(r => r.Rank is PwRowRank.Waiting or PwRowRank.Exception)
            .OrderBy(r => (int)r.Rank)
            .ToList();

    public IReadOnlyList<PwWorkRow> Recent =>
        Rows.Where(r => r.Rank == PwRowRank.Context).ToList();

    /// <summary>What the section draws when it has not been expanded.</summary>
    public IReadOnlyList<PwWorkRow> Default =>
        Active.Concat(Recent.Take(ContextPreview)).ToList();

    public int WaitingCount   => Rows.Count(r => r.Rank == PwRowRank.Waiting);
    public int ExceptionCount => Rows.Count(r => r.Rank == PwRowRank.Exception);
    public int ActiveCount    => WaitingCount + ExceptionCount;

    /// <summary>How many rows the "לכל…" affordance would add. Zero means the
    /// card is already showing everything it holds.</summary>
    public int HiddenCount => Math.Max(0, Rows.Count - Default.Count);
}

/// <summary>One line of פעילות אחרונה.</summary>
public sealed record PwActivityRow(
    string Key,
    string Text,
    string Meta,
    PwTone Tone,
    bool IsClickable);

/// <summary>Everything ProjectWorkspace.razor draws.
///
/// <para>There is no ActivityEmptyText. A project with no unique events draws
/// no activity block at all rather than a card whose entire content is a
/// sentence saying it has none — see the timeline note in
/// ProjectWorkspace.razor for what an entry has to be to earn a line.</para>
/// </summary>
public sealed record ProjectWorkspaceView(
    PwIdentity Identity,
    IReadOnlyList<PwStage> Stages,
    string StagesSummary,
    string StagesEmptyText,
    IReadOnlyList<PwSection> Sections,
    IReadOnlyList<PwActivityRow> Activity);

// ─────────────────────────────────────────────────────────────────────────────
//  Builders shared by both hosts
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A milestone reduced to the four facts the roadmap strip needs.
/// Both roles' milestone DTOs project onto this, which is what lets one stage
/// builder serve both.</summary>
public sealed record PwMilestoneLite(
    int Id, string Title, int OrderIndex, string Status, DateTime? DueDate);

public static class PwStages
{
    /// <summary>
    /// The strip, from whichever source the cycle actually has.
    ///
    /// <para>Roadmap stages when a cycle configures them; the project's own
    /// milestones when it does not — the identical shape from the other side.
    /// Lifted out of MentorProjectDetailPage unchanged so the lecturer's strip
    /// cannot drift from the mentor's: same precedence, same fallback date,
    /// same "הושלם carries no date" rule.</para>
    /// </summary>
    public static IReadOnlyList<PwStage> Build(
        ProjectRoadmapProgressDto? roadmap,
        IReadOnlyList<PwMilestoneLite> milestones)
    {
        var today = DateTime.Today;

        if (roadmap is { Stages.Count: > 0 })
        {
            return roadmap.Stages
                .OrderBy(s => s.DisplayOrder)
                .Select(s =>
                {
                    bool done    = s.Status == RoadmapStageStatuses.Completed;
                    bool current = s.Status == RoadmapStageStatuses.Current;

                    // A stage's own SuggestedEndDate first; when a cycle left it
                    // blank — the common case — the last due date among the
                    // milestones bound to that stage is the same fact from the
                    // other side, and it is already in this payload.
                    DateTime? end = s.SuggestedEndDate
                        ?? s.Milestones.Where(m => m.DueDate is not null)
                                       .Max(m => m.DueDate);

                    bool late = current && end is DateTime d && d.Date < today;

                    return new PwStage(s.Name, DateText(done, end, late), State(done, current, late));
                })
                .ToList();
        }

        var firstOpen = milestones.FirstOrDefault(m => !IsDone(m.Status));

        return milestones
            .OrderBy(m => m.OrderIndex)
            .Select(m =>
            {
                bool done    = IsDone(m.Status);
                bool current = !done && m.Id == firstOpen?.Id;
                bool late    = !done && m.DueDate is DateTime d && d.Date < today;

                return new PwStage(m.Title, DateText(done, m.DueDate, late), State(done, current, late));
            })
            .ToList();
    }

    /// <summary>"שלב 3 מתוך 7", prefixed when something in it is late.</summary>
    public static string Summary(IReadOnlyList<PwStage> stages)
    {
        if (stages.Count == 0) return "";

        var idx  = stages.ToList().FindIndex(c => c.State == PwStageState.Current);
        var late = stages.Count(c => c.State == PwStageState.Late);

        var position = idx >= 0 ? $"שלב {idx + 1} מתוך {stages.Count}" : $"{stages.Count} שלבים";
        return late > 0 ? $"אבן דרך באיחור · {position}" : position;
    }

    public static bool IsDone(string status) => status is "Completed" or "Done";

    private static PwStageState State(bool done, bool current, bool late) =>
        late    ? PwStageState.Late
        : current ? PwStageState.Current
        : done    ? PwStageState.Done
        :           PwStageState.Future;

    // "הושלם" carries no date: nothing stores the day a stage was actually
    // finished, and dating it with the plan would state something untrue.
    private static string DateText(bool done, DateTime? end, bool late) =>
        done                          ? "הושלם"
        : end is not DateTime endDate ? "—"
        : late                        ? $"באיחור מ־{endDate:dd.MM}"
        :                               $"עד {endDate:dd.MM}";
}

/// <summary>
/// Hebrew counted phrases for a section summary.
///
/// <para>Hebrew does not tolerate the "1 items" shape English shrugs at:
/// "1 ממתינות לבדיקתך" is wrong in both number and gender, and a project with
/// exactly one thing waiting is the commonest case a supervisor sees. Both
/// workspaces build the same six phrases, so the rule lives once.</para>
///
/// <para><paramref name="plural"/> takes the count as {0}; <paramref
/// name="singular"/> is written out, because the singular form is a different
/// sentence rather than the same one with a 1 in it.</para>
/// </summary>
public static class PwCount
{
    public static string Phrase(int n, string singular, string plural) =>
        n == 1 ? singular : string.Format(plural, n);
}

public static class PwDates
{
    /// <summary>היום / מחר / dd.MM — the one deadline wording both workspaces
    /// use in a row's trailing slot.
    ///
    /// <para>Load-bearing since the dates card was removed: a deliverable's
    /// deadline now reads on its own row in הגשות rather than in a separate
    /// list of dates, and this is what words it there.</para></summary>
    public static string DueLabel(DateTime? due)
    {
        if (due is null) return "—";
        var d = due.Value.ToLocalTime().Date;
        if (d == DateTime.Today)            return "היום";
        if (d == DateTime.Today.AddDays(1)) return "מחר";
        return d.ToString("dd.MM");
    }
}
