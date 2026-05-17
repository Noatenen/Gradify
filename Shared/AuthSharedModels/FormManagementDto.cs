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

    public static readonly IReadOnlyList<string> All = new[]
    {
        Text, OpenText, SingleChoice, MultiChoice, Ranking, FileUpload, Heading, Date,
    };

    public static string Label(string blockType) => blockType switch
    {
        Text         => "טקסט קצר",
        OpenText     => "טקסט ארוך",
        SingleChoice => "בחירה יחידה",
        MultiChoice  => "בחירה מרובה",
        Ranking      => "דירוג פרויקטים",
        FileUpload   => "העלאת קובץ",
        Heading      => "כותרת / טקסט הסבר",
        Date         => "תאריך",
        _            => blockType,
    };

    /// <summary>True when the block type uses an options list (single/multi/ranking).</summary>
    public static bool HasOptions(string blockType) =>
        blockType is SingleChoice or MultiChoice or Ranking;
}

/// <summary>Status values stored on Forms.Status.</summary>
public static class FormStatuses
{
    public const string Draft  = "Draft";
    public const string Open   = "Open";
    public const string Closed = "Closed";
}

/// <summary>Well-known FormBlock.BlockKey values for the assignment form.</summary>
public static class FormBlockKeys
{
    public const string Strengths          = "Strengths";
    public const string ProjectPreferences = "ProjectPreferences";
    public const string Notes              = "Notes";
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
    public string  UpdatedAt       { get; set; } = "";
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
    public List<FormBlockOptionDto> Options { get; set; } = new();
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
    public string  BlockType  { get; set; } = FormBlockTypes.Text;
    public string  Title      { get; set; } = "";
    public string  HelperText { get; set; } = "";
    public bool    IsRequired { get; set; }
    public int     SortOrder  { get; set; }
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
