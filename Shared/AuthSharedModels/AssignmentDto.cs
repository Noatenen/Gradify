using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

public class AssignmentContextDto
{
    public StudentBasicDto             Me                { get; set; } = new();
    public bool                        HasTeam           { get; set; }
    public List<TeamMemberBasicDto>    TeamMembers       { get; set; } = new();
    public List<StudentBasicDto>       AvailableStudents { get; set; } = new();
    public List<AssignmentCatalogItemDto> Catalog        { get; set; } = new();
    public ExistingAssignmentDto?      ExistingSubmission { get; set; }
    public AssignmentFormStatusDto     FormStatus         { get; set; } = new();

    /// <summary>
    /// The assignment form's PRESENTATION, resolved from the real FormBlocks
    /// the admin edits in the form editor. Null only in the legacy case where
    /// no Forms row exists yet, in which case the page falls back to its own
    /// built-in wording.
    /// </summary>
    public AssignmentFormLayoutDto?    FormLayout         { get; set; }
}

/// <summary>
/// Bridges the form editor and the assignment page.
///
/// WHY THIS IS A HYBRID AND NOT A GENERIC FORM
/// ───────────────────────────────────────────
/// Three of the assignment form's blocks are DOMAIN-BACKED: their answers are
/// not rows in FormAnswers but real business records —
/// StudentStrengths.Strength (the literals SkillWeight switches on),
/// TeamProjectPreferences.ProjectId + Priority (real FKs the matching
/// algorithm joins and scores 30/20/10), and AssignmentFormSubmissions.Notes.
/// Those three keep their storage exactly as it is.
///
/// What this DTO carries is only their presentation — title, helper text,
/// required flag, and for Strengths the option labels — so that editing a
/// block in the admin editor is no longer silently ignored by the student
/// page. Everything else on the form is a genuinely generic question and
/// travels in ExtraQuestions, answered through FormSubmissions/FormAnswers.
/// </summary>
public class AssignmentFormLayoutDto
{
    /// <summary>The Forms row this layout came from. 0 when none exists.</summary>
    public int FormId { get; set; }

    /// <summary>
    /// The Strengths block. Its Options carry BOTH halves: OptionLabel is what
    /// the student sees, OptionValue is what is written to
    /// StudentStrengths.Strength. The admin may edit the label; the value is
    /// refused by the server.
    /// </summary>
    public FormBlockDto? Strengths   { get; set; }

    /// <summary>
    /// The project-preferences block. Carries NO options on purpose — the
    /// choices are the live eligible catalog in
    /// <see cref="AssignmentContextDto.Catalog"/> and the answer is a real
    /// Projects.Id. Only its wording and required flag are configurable.
    /// </summary>
    public FormBlockDto? Preferences { get; set; }

    /// <summary>The free-text notes block, stored on AssignmentFormSubmissions.</summary>
    public FormBlockDto? Notes       { get; set; }

    /// <summary>
    /// Everything the admin added beyond the system blocks, in SortOrder.
    /// These are ordinary questions and their answers go to FormAnswers —
    /// never to TeamProjectPreferences or StudentStrengths.
    /// </summary>
    public List<FormBlockDto> ExtraQuestions { get; set; } = new();
}

public class AssignmentFormStatusDto
{
    public bool    IsOpen               { get; set; } = true;
    public string? OpensAt              { get; set; }
    public string? ClosesAt             { get; set; }
    public bool    AllowEditAfterSubmit { get; set; } = true;
    public string  Instructions         { get; set; } = "";
    public string  Status               { get; set; } = "Open"; // 'Draft' | 'Open' | 'Closed'
    public bool    CanSubmit            { get; set; } = true;
    public string? ClosedReason         { get; set; }            // 'before-open' | 'after-close' | 'form-closed' | 'edit-locked'
    public string? ClosedMessage        { get; set; }
}

public class StudentBasicDto
{
    public int    Id       { get; set; }
    public string FullName { get; set; } = "";
}

public class TeamMemberBasicDto
{
    public int          UserId    { get; set; }
    public string       FullName  { get; set; } = "";
    public List<string> Strengths { get; set; } = new();
}

public class AssignmentCatalogItemDto
{
    public int     Id            { get; set; }
    public int     ProjectNumber { get; set; }
    public string  Title        { get; set; } = "";
    public string  ProjectType  { get; set; } = "";
    public string  Availability { get; set; } = "Available";
    public string? Description  { get; set; }
}

public class ExistingAssignmentDto
{
    public List<ProjectPreferenceDto> Preferences           { get; set; } = new();
    public bool                       HasOwnProject         { get; set; }
    public string                     OwnProjectDescription { get; set; } = "";
    public string                     Notes                 { get; set; } = "";
    public string                     SubmittedAt           { get; set; } = "";

    /// <summary>
    /// Answers to the admin-added generic questions, read back from
    /// FormAnswers so an edit-after-submit reopens with them filled in.
    /// </summary>
    public List<FormAnswerInputDto>   ExtraAnswers          { get; set; } = new();
}

public class ProjectPreferenceDto
{
    public int Priority  { get; set; }
    public int ProjectId { get; set; }
}

public class StudentStrengthDto
{
    public int    UserId   { get; set; }
    public string Strength { get; set; } = "";
}

public class SubmitAssignmentRequest
{
    public List<int>                  PartnerIds            { get; set; } = new();
    public List<StudentStrengthDto>   Strengths             { get; set; } = new();
    public List<ProjectPreferenceDto> Preferences           { get; set; } = new();
    public bool                       HasOwnProject         { get; set; }
    public string                     OwnProjectDescription { get; set; } = "";
    public string                     Notes                 { get; set; } = "";

    /// <summary>
    /// Answers to the admin-added generic questions. Submitted in the SAME
    /// request as the domain data so the student performs one action, but
    /// persisted to FormSubmissions/FormAnswers rather than being forced into
    /// the domain tables.
    /// </summary>
    public List<FormAnswerInputDto>   ExtraAnswers          { get; set; } = new();
}
