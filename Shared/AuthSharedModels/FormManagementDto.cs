using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>Block type identifiers (canonical, English).
/// Existing five types are kept for backward compatibility with current forms;
/// the three trailing types were added for the builder. Renderers that
/// don't yet recognise the new types should fall back to a read-only display.</summary>
public static class FormBlockTypes
{
    public const string Text         = "Text";          // טקסט קצר
    public const string SingleChoice = "SingleChoice";  // בחירה יחידה
    public const string MultiChoice  = "MultiChoice";   // בחירה מרובה
    public const string Ranking      = "Ranking";       // דירוג פרויקטים
    public const string OpenText     = "OpenText";      // טקסט ארוך
    public const string FileUpload   = "FileUpload";    // העלאת קובץ
    public const string Heading      = "Heading";       // כותרת / טקסט הסבר
    public const string Date         = "Date";          // תאריך
    public const string Rating       = "Rating";        // דירוג (סולם 1–5 / 1–10)

    public static readonly IReadOnlyList<string> All = new[]
    {
        Text, OpenText, SingleChoice, MultiChoice, Ranking, FileUpload, Heading, Date, Rating,
    };

    /// <summary>
    /// The five types the form BUILDER offers, in the reference's order.
    ///
    /// Deliberately a subset of <see cref="All"/>. Text / FileUpload / Date /
    /// Ranking remain valid stored values — existing rows use them and the
    /// renderers still handle them — but they are not offered for new blocks:
    ///   · Ranking is the assignment form's project-ranking block. It is a
    ///     domain type fed from the live catalog, not something an admin
    ///     should be able to attach to an arbitrary question.
    ///   · Text / Date / FileUpload have no student renderer yet, and drawing
    ///     a type that cannot be answered is worse than omitting it.
    /// </summary>
    public static readonly IReadOnlyList<string> BuilderTypes = new[]
    {
        Heading, SingleChoice, MultiChoice, Rating, OpenText,
    };

    public static string Label(string blockType) => blockType switch
    {
        Text         => "טקסט קצר",
        OpenText     => "טקסט ארוך",
        SingleChoice => "בחירה יחידה",
        MultiChoice  => "בחירה מרובה",
        Ranking      => "דירוג פרויקטים",
        FileUpload   => "העלאת קובץ",
        Heading      => "טקסט / מידע",
        Date         => "תאריך",
        Rating       => "דירוג",
        _            => blockType,
    };

    /// <summary>True when the block type uses an options list (single/multi/ranking).</summary>
    public static bool HasOptions(string blockType) =>
        blockType is SingleChoice or MultiChoice or Ranking;

    /// <summary>
    /// True for blocks that present information rather than ask a question.
    /// An informational block is never required and never carries an answer.
    /// </summary>
    public static bool IsInformational(string blockType) =>
        blockType is Heading;

    /// <summary>True when the block stores a numeric rating on a fixed scale.</summary>
    public static bool IsRating(string blockType) =>
        blockType is Rating;

    /// <summary>True when the block's answer is free text.</summary>
    public static bool IsFreeText(string blockType) =>
        blockType is Text or OpenText;
}

/// <summary>
/// Status values STORED on Forms.Status. Exactly three — the column has only
/// ever held these, and every server-side gate switches on them.
/// </summary>
public static class FormStatuses
{
    public const string Draft  = "Draft";
    public const string Open   = "Open";
    public const string Closed = "Closed";

    /// <summary>The three storable statuses, in lifecycle order.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Draft, Open, Closed };

    public static string Label(string status) => status switch
    {
        Draft  => "טיוטה",
        Open   => "פתוח",
        Closed => "סגור",
        _      => status,
    };
}

/// <summary>
/// The status an admin actually SEES, which is not always the stored one.
///
/// The reference draws four chips — טיוטה / פתוח / מתוזמן / סגור — but the
/// column stores three. "מתוזמן" is not a fourth stored value invented for the
/// UI: it is what an Open form whose OpensAt is still in the future already
/// means, and the server's own gate agrees (FormsRepository.EvaluateGate
/// returns ClosedReason "before-open" for exactly this case). Deriving it here
/// keeps the chip honest without adding an unpersisted status to the table.
/// </summary>
public static class FormDisplayStatus
{
    public const string Draft     = "Draft";
    public const string Open      = "Open";
    public const string Scheduled = "Scheduled";
    public const string Closed    = "Closed";

    public static string Label(string displayStatus) => displayStatus switch
    {
        Draft     => "טיוטה",
        Open      => "פתוח",
        Scheduled => "מתוזמן",
        Closed    => "סגור",
        _         => displayStatus,
    };

    /// <summary>
    /// Resolves the chip from real persisted values only.
    /// Closed and Draft pass through; an Open form becomes Scheduled before its
    /// OpensAt and Closed after its ClosesAt.
    /// </summary>
    public static string Resolve(string status, string? opensAt, string? closesAt, DateTime? nowUtc = null)
    {
        if (string.Equals(status, FormStatuses.Closed, StringComparison.OrdinalIgnoreCase)) return Closed;
        if (string.Equals(status, FormStatuses.Draft,  StringComparison.OrdinalIgnoreCase)) return Draft;

        var now = nowUtc ?? DateTime.UtcNow;

        if (DateTime.TryParse(opensAt, out var opens) && now < opens.ToUniversalTime())
            return Scheduled;

        if (DateTime.TryParse(closesAt, out var closes) && now > closes.ToUniversalTime())
            return Closed;

        return Open;
    }
}

/// <summary>
/// Well-known FormBlock.BlockKey values for the assignment form.
///
/// A non-null BlockKey marks a SYSTEM block: one the assignment flow reads by
/// key rather than by position. System blocks cannot be deleted or retyped,
/// and two of them carry values that live code depends on:
///
///   · Strengths — its OptionValues are the literals
///     AssignmentManagementController.SkillWeight() switches on
///     ("Technology" / "Design" / "ProjectManagement" / "Content"). Renaming a
///     VALUE silently drops that strength's contribution to every team's match
///     score, with no error anywhere. Labels are free to change; values are not.
///   · ProjectPreferences — carries no stored options at all. Its choices are
///     the live project catalog, and the answer is a real Projects.Id written
///     to TeamProjectPreferences. Copying project names in as static options
///     would break assignment outright.
/// </summary>
public static class FormBlockKeys
{
    public const string Strengths          = "Strengths";
    public const string ProjectPreferences = "ProjectPreferences";
    public const string Notes              = "Notes";

    /// <summary>The OptionValues SkillWeight() depends on. Never rename.</summary>
    public static readonly IReadOnlyList<string> ProtectedStrengthValues = new[]
    {
        "Design", "Content", "Technology", "ProjectManagement",
    };

    /// <summary>True when the block's stored OptionValues are load-bearing.</summary>
    public static bool HasProtectedOptionValues(string? blockKey) =>
        string.Equals(blockKey, Strengths, StringComparison.Ordinal);

    /// <summary>True when the block's options come from live data, not the table.</summary>
    public static bool HasSystemSuppliedOptions(string? blockKey) =>
        string.Equals(blockKey, ProjectPreferences, StringComparison.Ordinal);
}

/// <summary>One row in the forms list.</summary>
public class FormListItemDto
{
    public int     Id              { get; set; }
    public int     AcademicYearId  { get; set; }
    public string  AcademicYear    { get; set; } = "";
    public string  Name            { get; set; } = "";
    public string  FormType        { get; set; } = "";
    public string  Status          { get; set; } = FormStatuses.Draft;
    public bool    IsOpen          { get; set; }
    public string? OpensAt         { get; set; }
    public string? ClosesAt        { get; set; }
    public int     SubmissionCount { get; set; }
    /// <summary>Blocks on the form, informational ones included.</summary>
    public int     QuestionCount   { get; set; }
    public string  UpdatedAt       { get; set; } = "";

    /// <summary>The chip the admin sees — derived, never stored. See FormDisplayStatus.</summary>
    public string  DisplayStatus   => FormDisplayStatus.Resolve(Status, OpensAt, ClosesAt);

    /// <summary>
    /// True for the one form per cycle that drives project assignment. It is
    /// filled through /student/assignment against its own domain tables, not
    /// through the generic responses engine.
    /// </summary>
    public bool    IsAssignmentForm =>
        string.Equals(FormType, "AssignmentForm", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Full form payload for the editor.</summary>
public class FormDetailDto
{
    public int     Id                   { get; set; }
    public int     AcademicYearId       { get; set; }
    public string  AcademicYear         { get; set; } = "";
    public string  Name                 { get; set; } = "";
    public string  FormType             { get; set; } = "";
    public string  Instructions         { get; set; } = "";
    public bool    IsOpen               { get; set; }
    public string? OpensAt              { get; set; }
    public string? ClosesAt             { get; set; }
    public bool    AllowEditAfterSubmit { get; set; }
    public string  Status               { get; set; } = FormStatuses.Draft;
    public int     SubmissionCount      { get; set; }
    public List<FormBlockDto> Blocks    { get; set; } = new();

    public string  DisplayStatus => FormDisplayStatus.Resolve(Status, OpensAt, ClosesAt);

    public bool    IsAssignmentForm =>
        string.Equals(FormType, "AssignmentForm", StringComparison.OrdinalIgnoreCase);
}

public class FormBlockDto
{
    public int     Id          { get; set; }
    public int     FormId      { get; set; }
    public string  BlockType   { get; set; } = FormBlockTypes.Text;
    public string? BlockKey    { get; set; }
    public string  Title       { get; set; } = "";
    public string  HelperText  { get; set; } = "";
    public bool    IsRequired  { get; set; }
    public int     SortOrder   { get; set; }

    /// <summary>Rating scale — 5 or 10. Only meaningful for BlockType "Rating".</summary>
    public int     RatingScale { get; set; } = 5;
    /// <summary>Optional label under the low end of a rating scale.</summary>
    public string  MinLabel    { get; set; } = "";
    /// <summary>Optional label under the high end of a rating scale.</summary>
    public string  MaxLabel    { get; set; } = "";

    public List<FormBlockOptionDto> Options { get; set; } = new();

    /// <summary>True when this block is anchored by a BlockKey the domain reads.</summary>
    public bool IsSystemBlock => !string.IsNullOrEmpty(BlockKey);
}

public class FormBlockOptionDto
{
    public int    Id          { get; set; }
    public int    FormBlockId { get; set; }
    public string OptionValue { get; set; } = "";
    public string OptionLabel { get; set; } = "";
    public int    SortOrder   { get; set; }
}

// ── Requests ────────────────────────────────────────────────────────────────

public class SaveFormRequest
{
    public int     AcademicYearId       { get; set; }
    public string  Name                 { get; set; } = "";
    public string  FormType             { get; set; } = "AssignmentForm";
    public string  Instructions         { get; set; } = "";
    public bool    IsOpen               { get; set; }
    public string? OpensAt              { get; set; }
    public string? ClosesAt             { get; set; }
    public bool    AllowEditAfterSubmit { get; set; } = true;
    public string  Status               { get; set; } = FormStatuses.Draft;
}

public class SaveBlockRequest
{
    public string  BlockType   { get; set; } = FormBlockTypes.OpenText;
    public string  Title       { get; set; } = "";
    public string  HelperText  { get; set; } = "";
    public bool    IsRequired  { get; set; }
    public int     SortOrder   { get; set; }
    public int     RatingScale { get; set; } = 5;
    public string  MinLabel    { get; set; } = "";
    public string  MaxLabel    { get; set; } = "";
}

public class SaveOptionRequest
{
    public string OptionValue { get; set; } = "";
    public string OptionLabel { get; set; } = "";
    public int    SortOrder   { get; set; }
}

// ── Duplication ─────────────────────────────────────────────────────────────

/// <summary>
/// Payload for POST /api/forms/{formId}/duplicate.
/// Creates a fresh form for a target academic year, copying blocks and options
/// from the source form. Submissions / responses are NEVER copied.
/// </summary>
public class DuplicateFormRequest
{
    public string NewName        { get; set; } = "";
    public int    AcademicYearId { get; set; }
    /// <summary>One of FormStatuses.* — Draft / Open / Closed.</summary>
    public string InitialStatus  { get; set; } = FormStatuses.Draft;
}

/// <summary>Returned on a successful duplication.</summary>
public class DuplicateFormResponse
{
    public int NewFormId { get; set; }
}

// ── Structure save (the editor's one "שמירה") ───────────────────────────────

/// <summary>
/// The whole question list, saved in one request.
///
/// The editor previously wrote one PUT per block and one per option, and
/// reordering fired two PUTs that could half-apply. A single structure save is
/// what makes the reference's dirty → saving → saved header honest: either the
/// form the admin is looking at is what is stored, or the save failed and
/// nothing moved.
///
/// This is an UPSERT, never a replace. Blocks and options keep their Ids so
/// answers already recorded against them stay attached; Id = 0 means "new".
/// Anything absent from the payload is deleted, EXCEPT system blocks (see
/// FormBlockKeys), which the server refuses to drop.
///
/// SortOrder is not sent. The server writes it from array position, so the
/// order the admin sees is the order that persists — there is no way for the
/// client's array and the stored SortOrder to disagree.
/// </summary>
public class SaveFormStructureRequest
{
    public List<SaveStructureBlockDto> Blocks { get; set; } = new();
}

public class SaveStructureBlockDto
{
    /// <summary>Existing block Id, or 0 to insert.</summary>
    public int     Id          { get; set; }
    public string  BlockType   { get; set; } = FormBlockTypes.OpenText;
    public string  Title       { get; set; } = "";
    public string  HelperText  { get; set; } = "";
    public bool    IsRequired  { get; set; }
    public int     RatingScale { get; set; } = 5;
    public string  MinLabel    { get; set; } = "";
    public string  MaxLabel    { get; set; } = "";
    public List<SaveStructureOptionDto> Options { get; set; } = new();
}

public class SaveStructureOptionDto
{
    /// <summary>Existing option Id, or 0 to insert.</summary>
    public int    Id          { get; set; }
    /// <summary>
    /// Stable machine value. For the assignment form's Strengths block this is
    /// load-bearing (SkillWeight switches on it) and the server rejects changes
    /// to it; elsewhere the server fills it in from the label when empty.
    /// </summary>
    public string OptionValue { get; set; } = "";
    public string OptionLabel { get; set; } = "";
}

// ── Submissions & answers (generic forms) ───────────────────────────────────

/// <summary>One row in a form's submissions list.</summary>
public class FormSubmissionListItemDto
{
    public int    Id          { get; set; }
    public int    FormId      { get; set; }
    public int    UserId      { get; set; }
    public string UserName    { get; set; } = "";
    public string UserEmail   { get; set; } = "";
    public string SubmittedAt { get; set; } = "";
    public string UpdatedAt   { get; set; } = "";
    public int    AnswerCount { get; set; }
}

/// <summary>One answered block, resolved for display.</summary>
public class FormAnswerDto
{
    public int    FormBlockId { get; set; }
    public string BlockType   { get; set; } = "";
    public string BlockTitle  { get; set; } = "";
    /// <summary>Chosen option LABELS, in the order the block defines them.</summary>
    public List<string> Values { get; set; } = new();
    /// <summary>Free-text answer, when the block is a text block.</summary>
    public string? Text        { get; set; }
    /// <summary>Numeric answer, when the block is a rating.</summary>
    public int?    Number      { get; set; }
}

/// <summary>A single submission with its answers, for the admin's read view.</summary>
public class FormSubmissionDetailDto
{
    public int    Id          { get; set; }
    public int    FormId      { get; set; }
    public string FormName    { get; set; } = "";
    public int    UserId      { get; set; }
    public string UserName    { get; set; } = "";
    public string UserEmail   { get; set; } = "";
    public string SubmittedAt { get; set; } = "";
    public string UpdatedAt   { get; set; } = "";
    public List<FormAnswerDto> Answers { get; set; } = new();
}

// ── Student-side fill ───────────────────────────────────────────────────────

/// <summary>
/// Everything the student page needs to render one generic form: the blocks as
/// the admin built them, the submission gate, and any answers already given.
/// </summary>
public class FormFillDto
{
    public int     FormId       { get; set; }
    public string  Name         { get; set; } = "";
    public string  Instructions { get; set; } = "";
    public string  AcademicYear { get; set; } = "";
    public string  Status       { get; set; } = FormStatuses.Draft;
    public string? OpensAt      { get; set; }
    public string? ClosesAt     { get; set; }
    public bool    CanSubmit    { get; set; }
    public string? ClosedReason  { get; set; }
    public string? ClosedMessage { get; set; }
    public bool    AllowEditAfterSubmit { get; set; }
    public string? SubmittedAt  { get; set; }
    public List<FormBlockDto>       Blocks           { get; set; } = new();
    public List<FormAnswerInputDto> ExistingAnswers  { get; set; } = new();

    public string DisplayStatus => FormDisplayStatus.Resolve(Status, OpensAt, ClosesAt);
}

/// <summary>
/// One answer as the student supplies it — and as it comes back on reload.
/// Values are option VALUES (not labels), so a later label edit never rewrites
/// what somebody actually answered.
/// </summary>
public class FormAnswerInputDto
{
    public int          FormBlockId  { get; set; }
    public List<string> OptionValues { get; set; } = new();
    public string?      Text         { get; set; }
    public int?         Number       { get; set; }
}

public class SubmitFormResponseRequest
{
    public List<FormAnswerInputDto> Answers { get; set; } = new();
}

/// <summary>One entry in the student's list of forms open to them.</summary>
public class StudentFormListItemDto
{
    public int     FormId      { get; set; }
    public string  Name        { get; set; } = "";
    public string  Status      { get; set; } = "";
    public string? OpensAt     { get; set; }
    public string? ClosesAt    { get; set; }
    public bool    CanSubmit   { get; set; }
    public bool    HasSubmitted{ get; set; }
    public string? SubmittedAt { get; set; }
}
