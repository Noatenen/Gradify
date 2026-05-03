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

    /// <summary>All statuses a staff member can set.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { New, InProgress, NeedsInfo, WaitingForStaff, Resolved, Closed };

    public static string Label(string status) => status switch
    {
        New             => "חדש",
        InProgress      => "בטיפול",
        NeedsInfo       => "הוחזרה לסטודנט",
        WaitingForStaff => "ממתין למענה אקדמי",
        Resolved        => "טופל",
        Closed          => "סגור",
        _               => status,
    };
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

    // Lecturer-stage states
    public const string NotRequired = "NotRequired";

    public static string Label(string s) => s switch
    {
        Pending     => "ממתין להחלטה",
        Approved    => "אושר",
        Rejected    => "נדחה",
        Escalated   => "הועבר למרצה",
        NotRequired => "לא נדרש",
        _           => s,
    };
}

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
    public string   Status          { get; set; } = RequestStatuses.New;
    public string   Priority        { get; set; } = RequestPriorities.Normal;
    public DateTime CreatedAt       { get; set; }
    public DateTime UpdatedAt       { get; set; }
    public string?  ResolutionNotes { get; set; }
    public int      AttachmentCount { get; set; }
    public List<ProjectRequestEventDto> Events { get; set; } = new();
}