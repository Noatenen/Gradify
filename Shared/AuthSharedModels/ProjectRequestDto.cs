using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  ProjectRequests domain — /api/project-requests
//
//  One unified requests module covers all request types in the academic
//  final-project process.  Request types and statuses are controlled string
//  constants (same pattern as role names / submission statuses in this project)
//  so they survive DB round-trips cleanly with Dapper / SQLite.
//
//  Status lifecycle:  New → InProgress → Resolved | Closed
// ─────────────────────────────────────────────────────────────────────────────

// ── Controlled string constants ───────────────────────────────────────────────

/// <summary>Controlled set of request type identifiers.</summary>
public static class RequestTypes
{
    public const string Extension                 = "Extension";
    public const string SpecialEvent              = "SpecialEvent";
    public const string TechnicalSupport          = "TechnicalSupport";
    public const string Meeting                   = "Meeting";
    public const string ClientChallenge           = "ClientChallenge";
    public const string ContentChallenge          = "ContentChallenge";
    public const string CharacterizationChallenge = "CharacterizationChallenge";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Extension, SpecialEvent, TechnicalSupport, Meeting,
        ClientChallenge, ContentChallenge, CharacterizationChallenge,
    };

    public static string Label(string type) => type switch
    {
        Extension                 => "בקשת דחייה",
        SpecialEvent              => "אירוע מיוחד",
        TechnicalSupport          => "פנייה טכנולוגית",
        Meeting                   => "פגישה / בקשה כללית",
        ClientChallenge           => "אתגר מול לקוח",
        ContentChallenge          => "אתגר תוכן",
        CharacterizationChallenge => "אתגר אפיון",
        _                         => type,
    };
}

/// <summary>Controlled set of request status identifiers.</summary>
public static class RequestStatuses
{
    public const string New              = "New";
    public const string InProgress       = "InProgress";
    public const string NeedsInfo        = "NeedsInfo";        // returned to student — awaiting more info
    public const string WaitingForStaff  = "WaitingForStaff";  // student replied — waiting for academic side
    public const string Resolved         = "Resolved";
    public const string Closed           = "Closed";

    // Extension-only intermediate statuses. The flow is enforced by the
    // extension decision endpoints and is invisible to non-extension types.
    /// <summary>Extension-only — student created the request, awaiting the
    /// mentor's recommendation before it can reach a lecturer.</summary>
    public const string PendingMentorRecommendation = "PendingMentorRecommendation";
    /// <summary>Extension-only — mentor submitted a recommendation, awaiting
    /// the lecturer/admin's final decision.</summary>
    public const string PendingLecturerDecision    = "PendingLecturerDecision";

    /// <summary>All statuses a staff member can set.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { New, InProgress, NeedsInfo, WaitingForStaff, Resolved, Closed,
                PendingMentorRecommendation, PendingLecturerDecision };

    public static string Label(string status) => status switch
    {
        New                          => "חדש",
        InProgress                   => "בטיפול",
        NeedsInfo                    => "הוחזרה לסטודנט",
        WaitingForStaff              => "ממתין למענה אקדמי",
        Resolved                     => "טופל",
        Closed                       => "סגור",
        PendingMentorRecommendation  => "ממתינה להמלצת מנחה",
        PendingLecturerDecision      => "ממתינה להחלטת מרצה",
        _                            => status,
    };
}

/// <summary>
/// WHOSE COURT A REQUEST IS IN — the single answer every "requires your
/// attention" surface asks of a request.
///
/// <para><b>Why this exists.</b> A dashboard's attention section is a work
/// queue, and a work queue may only contain items the viewer can actually move
/// forward. Before this, each dashboard answered "is this mine?" with its own
/// hand-written status list: the student card showed every non-terminal
/// request on the project — including ones it had filed and was merely waiting
/// on — and the lecturer card carried its own three-status literal. Two lists,
/// no shared definition, and nothing tying either to the workflow.</para>
///
/// <para><b>This is a restatement, not a new rule.</b> The owner of each
/// status is already fixed by the API surface, and this mapping only names it:
/// <c>POST /api/project-requests/{id}/mentor-recommendation</c> is
/// Mentor-only and gated on <see cref="RequestStatuses.PendingMentorRecommendation"/>;
/// <c>/handle</c> is Admin+Staff; <c>/reply</c> requires a team member, which
/// is the only move available while a request sits at
/// <see cref="RequestStatuses.NeedsInfo"/> ("returned to student — awaiting
/// more info"). The client-side buckets that already encode this —
/// <c>RequestBuckets.Of</c> for the student workspace and
/// <c>MentorRequestBuckets.Of</c> for the mentor's — agree with it status for
/// status; they stay where they are, because they also carry per-role labels,
/// tones and filter ids that have no business in Shared.</para>
///
/// <para><b>The default arm is Staff, deliberately.</b> NeedsInfo is the only
/// status that hands a request back to the team, PendingMentorRecommendation
/// the only one that hands it to a mentor, and Resolved/Closed the only
/// terminal ones; everything else is in flight on the academic side. Falling
/// through rather than enumerating means a status added to
/// <see cref="RequestStatuses"/> later surfaces on the staff queue by default
/// instead of silently belonging to nobody — a request that wrongly appears is
/// noticed, one that never appears is not. It is the same safety property both
/// existing client mappings were written with.</para>
/// </summary>
public static class RequestOwnership
{
    /// <summary>The party expected to take the next action on a request.</summary>
    public enum Owner
    {
        /// <summary>The student / team must respond before anyone else can act.</summary>
        Student,
        /// <summary>Blocked on a mentor's recommendation.</summary>
        Mentor,
        /// <summary>With the academic side — lecturer / staff / admin.</summary>
        Staff,
        /// <summary>Terminal. Nobody owes anything.</summary>
        None,
    }

    public static Owner NextActionOwner(string status) => status switch
    {
        RequestStatuses.NeedsInfo                   => Owner.Student,
        RequestStatuses.PendingMentorRecommendation => Owner.Mentor,
        RequestStatuses.Resolved or
        RequestStatuses.Closed                      => Owner.None,
        _                                           => Owner.Staff,
    };

    /// <summary>True when the student/team is the one holding this request up.</summary>
    public static bool AwaitsStudent(string status) =>
        NextActionOwner(status) == Owner.Student;

    /// <summary>True when a mentor recommendation is what the request is waiting for.</summary>
    public static bool AwaitsMentor(string status) =>
        NextActionOwner(status) == Owner.Mentor;

    /// <summary>True when the academic side owes the next move.</summary>
    public static bool AwaitsStaff(string status) =>
        NextActionOwner(status) == Owner.Staff;
}

/// <summary>Controlled set of event type identifiers for the request thread.</summary>
public static class RequestEventTypes
{
    public const string Comment        = "Comment";
    public const string StatusChange   = "StatusChange";
    public const string PriorityChange = "PriorityChange";
    public const string AssigneeChange = "AssigneeChange";
}

/// <summary>Controlled set of request priority identifiers.</summary>
public static class RequestPriorities
{
    public const string Low    = "Low";
    public const string Normal = "Normal";
    public const string High   = "High";
    public const string Urgent = "Urgent";

    public static readonly IReadOnlyList<string> All =
        new[] { Low, Normal, High, Urgent };

    public static string Label(string priority) => priority switch
    {
        Low    => "נמוכה",
        Normal => "רגילה",
        High   => "גבוהה",
        Urgent => "דחופה",
        _      => priority,
    };
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Slim request row for list views.
/// Returned by GET /api/project-requests (Admin / Staff).
/// </summary>
public class ProjectRequestRowDto
{
    public int      Id               { get; set; }
    public string   RequestType      { get; set; } = "";
    public string   Title            { get; set; } = "";

    public int      ProjectId        { get; set; }
    public int      ProjectNumber    { get; set; }
    public string   ProjectTitle     { get; set; } = "";

    public int      CreatedByUserId  { get; set; }
    public string   CreatedByName    { get; set; } = "";

    public DateTime CreatedAt        { get; set; }
    public DateTime UpdatedAt        { get; set; }

    public string   Status           { get; set; } = RequestStatuses.New;
    public string   Priority         { get; set; } = RequestPriorities.Normal;

    public string?  AssignedToName   { get; set; }
    public int      AttachmentCount  { get; set; }
    /// <summary>True when the request has activity newer than the caller's
    /// last view (or has never been viewed). Drives the WhatsApp-style dot
    /// in the inbox list.</summary>
    public bool     HasUnread        { get; set; }

    // ── Team quick-info (rendered in the management list popover) ────────────
    // Populated server-side from Teams / TeamMembers / ProjectMentors /
    // ProjectTypes so the client can render the popover without extra calls.
    // Names and emails are kept as parallel lists ordered identically.
    public string?       TeamName       { get; set; }
    public string?       TrackName      { get; set; }
    public List<string>  StudentNames   { get; set; } = new();
    public List<string>  StudentEmails  { get; set; } = new();
    public List<string>  MentorNames    { get; set; } = new();
    public List<string>  MentorEmails   { get; set; } = new();

    // ── What THIS caller may do next ────────────────────────────────────────
    //
    // Computed server-side, per request, for the authenticated caller, by
    // ExtensionWorkflow.Resolve — the same function the POST endpoints gate on,
    // so a button can only appear where the request would actually succeed.
    //
    // THEY LIVE ON THE ROW, NOT ONLY ON THE DETAIL. A list row has to word its
    // own status, and "ממתינה להחלטתך" is a claim about what the reader may do.
    // While the flags existed only on the detail payload, every list re-derived
    // that claim from the status plus the reader's roles — which is exactly how
    // a row came to say "ממתינה להחלטתך" above a surface offering no decision.
    // The list asks the server once, like the detail does.

    /// <summary>The caller is in ProjectMentors for this request's project.
    /// NOT "holds the Mentor role" — a mentor of some OTHER project is false
    /// here, and a dual-role lecturer who genuinely mentors this project is
    /// true.</summary>
    public bool ViewerIsProjectMentor { get; set; }

    /// <summary>The caller may post /mentor-recommendation right now.</summary>
    public bool ViewerCanRecommend { get; set; }

    /// <summary>The caller may post /extension/decision right now.</summary>
    public bool ViewerCanDecide { get; set; }

    /// <summary>
    /// True when <see cref="ViewerCanDecide"/> is reached WITHOUT a prior
    /// mentor recommendation, because this caller is both the assigned mentor
    /// of the project and academic staff. The UI uses it only to explain why no
    /// recommendation block is shown; the authority itself is
    /// <see cref="ViewerCanDecide"/>.
    /// </summary>
    public bool ViewerDecisionIsCombined { get; set; }

    /// <summary>
    /// The request's MENTOR STAGE has completed — a mentor advised, escalated,
    /// or (on legacy rows) decided. See ExtensionWorkflow.MentorStageComplete
    /// for why this is not inferred from the status.
    /// </summary>
    public bool MentorStageComplete { get; set; }
}

/// <summary>
/// Full request detail including description, resolution notes, and attachments.
/// Returned by GET /api/project-requests/{id}.
/// </summary>
public class ProjectRequestDetailDto : ProjectRequestRowDto
{
    public string? Description     { get; set; }
    public string? ResolutionNotes { get; set; }
    public int?    AssignedToUserId { get; set; }

    public List<ProjectRequestAttachmentDto>  Attachments { get; set; } = new();
    public List<ProjectRequestEventDto>       Events      { get; set; } = new();

    /// <summary>Populated only when RequestType = Extension. Null otherwise.</summary>
    public ExtensionRequestInfoDto? Extension { get; set; }
}

// ── Extension request side-data ───────────────────────────────────────────────

/// <summary>Controlled vocabulary for the two-stage extension decision flow.</summary>
public static class ExtensionDecisionStatuses
{
    // Mentor decision states
    public const string Pending     = "Pending";
    public const string Approved    = "Approved";
    public const string Rejected    = "Rejected";
    public const string Escalated   = "Escalated";

    // New flow (recommendation-only mentor stage). The mentor never makes a
    // terminal decision — they advise and the lecturer/admin always decides.
    /// <summary>Mentor recommends approving the extension.</summary>
    public const string Recommended    = "Recommended";
    /// <summary>Mentor recommends rejecting the extension.</summary>
    public const string NotRecommended = "NotRecommended";

    // Lecturer-stage states
    public const string NotRequired = "NotRequired";

    public static string Label(string s) => s switch
    {
        Pending        => "ממתין להחלטה",
        Approved       => "אושר",
        Rejected       => "נדחה",
        Escalated      => "הועבר למרצה",
        Recommended    => "ממליץ",
        NotRecommended => "לא ממליץ",
        NotRequired    => "לא נדרש",
        _              => s,
    };
}

/// <summary>
/// THE EXTENSION FLOW, RESOLVED IN ONE PLACE.
///
/// <para>An extension request passes a MENTOR STAGE (advice) and then a
/// LECTURER STAGE (the decision). Three call sites used to answer "has the
/// mentor stage completed?" independently, by listing the values that count —
/// and all three listed the same three: Recommended, NotRecommended,
/// Escalated. That list is incomplete, and the omission was not cosmetic.</para>
///
/// <para><b>THE LEGACY VALUES ARE REAL DATA.</b> The mentor stage used to be a
/// terminal DECISION, and it wrote <see cref="ExtensionDecisionStatuses.Approved"/>
/// / <see cref="ExtensionDecisionStatuses.Rejected"/>. An audit of
/// ProjectRequestExtensions found those rows are the LARGEST group still in the
/// table — Approved 5, Recommended 4, Pending 4, NotRecommended 3 — and every
/// non-Pending row carries both MentorDecidedAt and MentorDecidedByUserId, so
/// there is no ambiguity about whether a mentor acted. A request holding the
/// legacy wording had genuinely completed its mentor stage, and both the
/// capability flag and the POST gate refused it: the workspace said
/// "ממתינה להחלטתך" and offered nothing, and the endpoint answered
/// "הבקשה ממתינה להמלצת מנחה". A UI patch would have produced a button that
/// 403s, which is why the rule is normalised here and read from both.</para>
///
/// <para><b>THE STATUS IS NOT A SUBSTITUTE FOR THE DATA.</b> It is tempting to
/// read <c>Status == PendingLecturerDecision</c> as "the mentor stage
/// finished", since that is the only status the recommendation POST writes.
/// The table says otherwise: a live row sits at PendingLecturerDecision with
/// MentorDecision still Pending. Its mentor never advised, nobody may decide
/// it, and it must not be labelled as waiting on anyone's decision — so the
/// stage question is answered from the mentor column, never from the
/// status.</para>
/// </summary>
public static class ExtensionWorkflow
{
    /// <summary>
    /// Has a mentor finished with this request?
    ///
    /// <para>Stated as "anything other than undecided" rather than as a list of
    /// the values that qualify — a list is what went stale. The column is
    /// <c>NOT NULL DEFAULT 'Pending'</c>, so undecided has exactly one
    /// spelling; an empty string is treated as the same thing defensively.</para>
    ///
    /// <para>Escalated counts: a mentor who pushed the request up without
    /// advising has finished with it, which is precisely what the lecturer
    /// stage needs to know.</para>
    /// </summary>
    public static bool MentorStageComplete(string? mentorDecision) =>
        !string.IsNullOrWhiteSpace(mentorDecision)
        && mentorDecision != ExtensionDecisionStatuses.Pending;

    /// <summary>
    /// The mentor's recorded advice, in the vocabulary the product speaks now.
    ///
    /// <para>The old stage's Approved/Rejected mean "recommends approving" /
    /// "recommends rejecting" — they were never a final verdict, because the
    /// lecturer's own decision is what closes a request. Displaying them raw
    /// prints "אושר" on a request that has not been approved by anybody.</para>
    /// </summary>
    public static string NormalizeMentorDecision(string? mentorDecision) => mentorDecision switch
    {
        null or "" => ExtensionDecisionStatuses.Pending,
        ExtensionDecisionStatuses.Approved => ExtensionDecisionStatuses.Recommended,
        ExtensionDecisionStatuses.Rejected => ExtensionDecisionStatuses.NotRecommended,
        _ => mentorDecision,
    };

    /// <summary>
    /// WHAT THIS CALLER MAY DO WITH THIS REQUEST — the single resolver behind
    /// the Viewer* flags, the POST gate on /extension/decision, and every list
    /// row that has to word a status honestly.
    ///
    /// <para>Pure: it is handed the facts and returns the answer, so the
    /// detail endpoint, the requests list and the project overview cannot
    /// drift from the endpoint that enforces it.</para>
    ///
    /// <para><paramref name="isProjectMentor"/> must come from the caller's real
    /// ProjectMentors row for THIS project, never from role membership — a
    /// dual-role account holds the Mentor role on every project in the
    /// system.</para>
    /// </summary>
    public static ExtensionCapabilities Resolve(
        string? requestType,
        string? status,
        string? mentorDecision,
        string? lecturerDecision,
        string? finalDecision,
        bool isAdminOrStaff,
        bool holdsMentorRole,
        bool isProjectMentor)
    {
        bool stageComplete = MentorStageComplete(mentorDecision);

        // Only extensions have the two-stage flow. Everything else is answered
        // through /handle and /reply, which have their own gates.
        if (requestType != RequestTypes.Extension)
            return new ExtensionCapabilities(false, false, false, stageComplete);

        // The same three conditions SubmitMentorRecommendation checks: right
        // status, a real ProjectMentors row, and the Mentor role the endpoint's
        // own [Authorize] requires.
        bool canRecommend =
            holdsMentorRole
            && isProjectMentor
            && status == RequestStatuses.PendingMentorRecommendation;

        // "Not decided yet" is NOT the same as LecturerDecision == Pending. A
        // fresh extension row is born NotRequired and only becomes Pending once
        // a recommendation opens the lecturer stage; both mean undecided, and
        // only Approved/Rejected are terminal.
        bool lecturerHasDecided =
            lecturerDecision == ExtensionDecisionStatuses.Approved ||
            lecturerDecision == ExtensionDecisionStatuses.Rejected;

        bool stillOpen =
            finalDecision == ExtensionDecisionStatuses.Pending
            && !lecturerHasDecided
            && status is not (RequestStatuses.Resolved or RequestStatuses.Closed);

        // THE DUAL-ROLE SHORT-CIRCUIT. The recommendation stage exists so a
        // decision carries a second person's judgement. When the caller IS this
        // project's assigned mentor and also holds final authority there is no
        // second person, so requiring them to advise themselves adds a step and
        // no safeguard.
        bool canDecide =
            isAdminOrStaff
            && stillOpen
            && (stageComplete || isProjectMentor);

        return new ExtensionCapabilities(
            CanRecommend:        canRecommend,
            CanDecide:           canDecide,
            DecisionIsCombined:  canDecide && !stageComplete && isProjectMentor,
            MentorStageComplete: stageComplete);
    }
}

/// <summary>The answer <see cref="ExtensionWorkflow.Resolve"/> returns.</summary>
public readonly record struct ExtensionCapabilities(
    bool CanRecommend,
    bool CanDecide,
    bool DecisionIsCombined,
    bool MentorStageComplete);

/// <summary>Carried inside ProjectRequestDetailDto when the request is an Extension.</summary>
public class ExtensionRequestInfoDto
{
    public int       Id                       { get; set; }
    public int       RequestId                { get; set; }
    /// <summary>Specific Task this extension targets (mutually exclusive with ProjectMilestoneId).</summary>
    public int?      TaskId                   { get; set; }
    public string?   TaskTitle                { get; set; }
    public int?      ProjectMilestoneId       { get; set; }
    public string?   MilestoneTitle           { get; set; }
    /// <summary>Snapshot of the global due date when the request was filed (display only).</summary>
    public DateTime? CurrentDueDate           { get; set; }
    /// <summary>Date the student requested.</summary>
    public DateTime  RequestedDueDate         { get; set; }
    public string?   Reason                   { get; set; }

    public string    MentorDecision           { get; set; } = ExtensionDecisionStatuses.Pending;
    public DateTime? MentorDecidedAt          { get; set; }
    public string?   MentorDecidedByName      { get; set; }
    public string?   MentorNotes              { get; set; }

    public string    LecturerDecision         { get; set; } = ExtensionDecisionStatuses.NotRequired;
    public DateTime? LecturerDecidedAt        { get; set; }
    public string?   LecturerDecidedByName    { get; set; }
    public string?   LecturerNotes            { get; set; }

    public string    FinalDecision            { get; set; } = ExtensionDecisionStatuses.Pending;
    /// <summary>Final approved date — written when a decision-maker approves with a chosen date.</summary>
    public DateTime? ApprovedDueDate          { get; set; }
}

/// <summary>One pickable target (task or milestone) for the student's
/// extension-request modal. Returned by GET /api/project-requests/extension-targets.</summary>
public class ExtensionTargetDto
{
    /// <summary>"Task" | "Milestone"</summary>
    public string    Kind            { get; set; } = "";
    public int       Id              { get; set; }
    public string    Title           { get; set; } = "";
    /// <summary>Parent milestone title for tasks. Empty for milestone-kind rows.</summary>
    public string?   MilestoneTitle  { get; set; }
    public DateTime? CurrentDueDate  { get; set; }
    /// <summary>Internal status string (e.g., "Open"/"InProgress" for tasks,
    /// "NotStarted"/"InProgress" for milestones). Surfaced in the student
    /// extension-modal details panel so the student can confirm what state
    /// they're requesting a deadline change for.</summary>
    public string    Status          { get; set; } = "";
}

/// <summary>Mentor or lecturer decision payload.</summary>
public class ExtensionDecisionRequest
{
    /// <summary>"Mentor" | "Lecturer" — server-validated against the caller's role.</summary>
    public string    Stage           { get; set; } = "Mentor";
    /// <summary>"Approved" | "Rejected" | "Escalated" (Escalated only valid at Stage=Mentor).</summary>
    public string    Decision        { get; set; } = "";
    /// <summary>Required when Decision = Approved AND the request targets a specific Task or Milestone.</summary>
    public DateTime? ApprovedDueDate { get; set; }
    public string?   Notes           { get; set; }
}

/// <summary>
/// Payload for POST /api/project-requests/{id}/mentor-recommendation.
/// The mentor never makes a terminal decision — they recommend, and the
/// request always escalates to the lecturer/admin for the final call.
/// Notes are an INTERNAL note, visible to mentors and academic staff only —
/// never to the student that owns the request.
/// </summary>
public class MentorRecommendationRequest
{
    /// <summary>"Recommended" or "NotRecommended".</summary>
    public string  Recommendation { get; set; } = "";
    public string? Notes          { get; set; }
}

/// <summary>Image attachment metadata for a project request.</summary>
public class ProjectRequestAttachmentDto
{
    public int      Id               { get; set; }
    public int      RequestId        { get; set; }
    public string   OriginalFileName { get; set; } = "";
    /// <summary>GUID-based stored filename under wwwroot/request-attachments/.</summary>
    public string   StoredFileName   { get; set; } = "";
    public string   ContentType      { get; set; } = "";
    public long     SizeBytes        { get; set; }
    public DateTime UploadedAt       { get; set; }
}

/// <summary>File attached to a specific request thread event/comment.</summary>
public class ProjectRequestEventAttachmentDto
{
    public int      Id               { get; set; }
    public int      EventId          { get; set; }
    public string   OriginalFileName { get; set; } = "";
    /// <summary>GUID-based stored filename under wwwroot/request-attachments/.</summary>
    public string   StoredFileName   { get; set; } = "";
    public string   ContentType      { get; set; } = "";
    public long     SizeBytes        { get; set; }
    public DateTime UploadedAt       { get; set; }
}

// ── Request models ────────────────────────────────────────────────────────────

/// <summary>
/// Payload for POST /api/project-requests.
/// Priority is intentionally not a student-controlled field — it defaults to
/// Normal on the server and can be updated later by Admin / Staff.
///
/// When RequestType = "Extension" the extension-specific fields below are
/// consumed and a side-row is written into ProjectRequestExtensions in the
/// same insert batch. For all other request types these fields are ignored.
/// </summary>
public class CreateProjectRequestRequest
{
    public int     ProjectId   { get; set; }
    public string  RequestType { get; set; } = "";
    public string  Title       { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Image attachments (jpg/png/webp, max 5 MB each, max 5 images).</summary>
    public List<RequestAttachmentUploadRequest> Attachments { get; set; } = new();

    // ── Extension-only fields (used only when RequestType = "Extension") ───
    /// <summary>Target task — mutually exclusive with TargetMilestoneId.</summary>
    public int?      TargetTaskId       { get; set; }
    /// <summary>Target milestone — mutually exclusive with TargetTaskId.</summary>
    public int?      TargetMilestoneId  { get; set; }
    /// <summary>Required for Extension when a target is set; the new date the student is asking for.</summary>
    public DateTime? RequestedDueDate   { get; set; }
}

/// <summary>One image file within a CreateProjectRequestRequest.</summary>
public class RequestAttachmentUploadRequest
{
    public string OriginalFileName { get; set; } = "";
    /// <summary>Base64-encoded file content — same encoding pattern as ResourceFiles.</summary>
    public string FileBase64       { get; set; } = "";
    public string ContentType      { get; set; } = "";
    public long   SizeBytes        { get; set; }
}

/// <summary>
/// Payload for PATCH /api/project-requests/{id}.
/// Used by Admin / Staff to update status, priority, and / or resolution notes.
/// </summary>
public class UpdateProjectRequestRequest
{
    public string  Status          { get; set; } = "";
    public string? Priority        { get; set; }
    public string? ResolutionNotes { get; set; }
}

/// <summary>
/// Payload for POST /api/project-requests/{id}/handle.
/// One atomic handling action — records events for every detected change.
/// Comment is optional but recommended so the thread stays meaningful.
/// Attachments are linked to the comment event (if a comment is provided).
/// </summary>
public class HandleProjectRequestRequest
{
    public string  NewStatus         { get; set; } = "";
    public string  NewPriority       { get; set; } = RequestPriorities.Normal;
    public int?    AssignedToUserId  { get; set; }
    public string? Comment           { get; set; }
    /// <summary>Files to attach to the comment event (images / PDF / docx, max 3, max 5 MB each).</summary>
    public List<RequestAttachmentUploadRequest> Attachments { get; set; } = new();
}

/// <summary>A single event in the request thread / audit log.</summary>
public class ProjectRequestEventDto
{
    public int      Id        { get; set; }
    public int      RequestId { get; set; }
    public int      UserId    { get; set; }
    public string   UserName  { get; set; } = "";
    /// <summary>Role of the author — used on the client to distinguish student vs staff messages.</summary>
    public string   UserRole  { get; set; } = "";
    /// <summary>Comment | StatusChange | PriorityChange | AssigneeChange</summary>
    public string   EventType { get; set; } = "";
    /// <summary>Comment text, or null for pure-event rows.</summary>
    public string?  Content   { get; set; }
    /// <summary>Human-readable previous value (for change events).</summary>
    public string?  OldValue  { get; set; }
    /// <summary>Human-readable new value (for change events).</summary>
    public string?  NewValue  { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Files attached to this comment event (only populated for Comment events).</summary>
    public List<ProjectRequestEventAttachmentDto> Attachments { get; set; } = new();
}

/// <summary>
/// Payload for POST /api/project-requests/{id}/reply.
/// Lets a student append a comment to an existing request thread.
/// Attachments are linked to the created comment event.
/// </summary>
public class StudentReplyRequest
{
    public string Comment { get; set; } = "";
    /// <summary>Files to attach to the reply (images / PDF / docx, max 3, max 5 MB each).</summary>
    public List<RequestAttachmentUploadRequest> Attachments { get; set; } = new();
}

/// <summary>Slim user row for the assignee dropdown.</summary>
public class AssignableUserDto
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
}

/// <summary>
/// Slim request row for the student's own requests list.
/// Returned by GET /api/project-requests/my.
/// </summary>
public class StudentOwnRequestDto
{
    public int      Id              { get; set; }
    public string   RequestType     { get; set; } = "";
    public string   Title           { get; set; } = "";
    /// <summary>Student-supplied body text. Was missing from the DTO until
    /// 2026-05-19, which is why the student's detail view didn't show the
    /// original text they submitted.</summary>
    public string?  Description     { get; set; }
    public string   Status          { get; set; } = RequestStatuses.New;
    public string   Priority        { get; set; } = RequestPriorities.Normal;
    public DateTime CreatedAt       { get; set; }
    public DateTime UpdatedAt       { get; set; }
    public string?  ResolutionNotes { get; set; }
    public int      AttachmentCount { get; set; }
    /// <summary>True when the request has activity newer than this student's
    /// last view of it. Cleared by POST /api/project-requests/{id}/mark-read
    /// (called automatically when the detail is opened).</summary>
    public bool     HasUnread       { get; set; }
    public List<ProjectRequestEventDto> Events { get; set; } = new();

    /// <summary>Populated only when RequestType = Extension. Null otherwise.</summary>
    public ExtensionRequestInfoDto? Extension { get; set; }
}