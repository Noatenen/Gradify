using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Task Management DTOs  —  /api/task-templates
//
//  These are for the admin management area only.
//  TaskTemplates are global reusable task definitions linked to a milestone
//  template. They are distinct from per-project operational Tasks.
//
//  Date-based status (computed client-side):
//    StartDate > today  →  Locked  (מנוע)
//    today in [Start, Due] →  Open   (פתוח)
//    DueDate  < today  →  Closed  (סגור)
// ─────────────────────────────────────────────────────────────────────────────

public class TaskTemplateDto
{
    public int      Id                  { get; set; }
    public string   Title               { get; set; } = "";
    public string?  Description         { get; set; }
    // ── Milestone assignment ────────────────────────────────────────────────
    // NULLABLE. Null = an UNASSIGNED library template: it exists in the Task
    // Templates library but is attached to no milestone, so no cycle rollout
    // will ever create it. This is what the milestone editor's "remove from
    // this milestone" produces — the template is detached, never deleted.
    public int?     MilestoneTemplateId { get; set; }

    /// <summary>Title of the assigned milestone template, or null when unassigned.</summary>
    public string?  MilestoneTitle      { get; set; }

    // ── Applicability — INHERITED, never stored on the task ──────────────────
    // TaskTemplates has no ProjectTypeId and deliberately does not get one: the
    // rollout decides which projects a task reaches from its MILESTONE's
    // ProjectTypeId (AcademicYearsController.ApplyTemplates). A second,
    // task-level type would render a control the engine does not read.
    // These two are projections of the parent milestone, resolved server-side,
    // and are read-only everywhere in the UI.

    /// <summary>Parent milestone's ProjectTypeId. Null = both types, or unassigned.</summary>
    public int?     ProjectTypeId       { get; set; }

    /// <summary>Resolved label: "שניהם" | "טכנולוגי" | "מתודולוגי" | "לא משויך".</summary>
    public string   Applicability       { get; set; } = "לא משויך";

    public DateTime StartDate           { get; set; }
    public DateTime DueDate             { get; set; }
    public bool     IsActive            { get; set; }
    public DateTime CreatedAt           { get; set; }

    // ── Submission policy ───────────────────────────────────────────────────
    // Populated only when IsSubmission = true.
    // These fields define the upload rules enforced on the student side.
    public bool     IsSubmission             { get; set; }
    public string?  SubmissionInstructions   { get; set; }
    /// <summary>Maximum number of files a student may upload. Null when not a submission task.</summary>
    public int?     MaxFilesCount            { get; set; }
    /// <summary>Maximum size of each uploaded file in MB. Null when not a submission task.</summary>
    public int?     MaxFileSizeMb            { get; set; }
    /// <summary>Comma-separated list of permitted extensions, e.g. "pdf,docx,jpg".</summary>
    public string?  AllowedFileTypes         { get; set; }

    /// <summary>Supporting/reference resource files linked to this template (read-only in DTO).</summary>
    public List<TaskTemplateResourceFileDto> LinkedResourceFiles { get; set; } = new();
}

/// <summary>Slim representation of a resource file attached to a task template.</summary>
public class TaskTemplateResourceFileDto
{
    public int    Id          { get; set; }
    public string FileName    { get; set; } = "";
    public string ContentType { get; set; } = "";
}

public class SaveTaskTemplateRequest
{
    public string   Title               { get; set; } = "";
    public string?  Description         { get; set; }

    /// <summary>Milestone template to attach to, or null to leave the template
    /// unassigned in the library. See TaskTemplateDto.MilestoneTemplateId.</summary>
    public int?     MilestoneTemplateId { get; set; }

    public DateTime StartDate           { get; set; }
    public DateTime DueDate             { get; set; }
    public bool     IsActive            { get; set; } = true;

    // ── Submission policy ───────────────────────────────────────────────────
    public bool     IsSubmission            { get; set; }
    public string?  SubmissionInstructions  { get; set; }
    public int?     MaxFilesCount           { get; set; }
    public int?     MaxFileSizeMb           { get; set; }
    /// <summary>Comma-separated permitted extensions, e.g. "pdf,docx,jpg".</summary>
    public string?  AllowedFileTypes        { get; set; }
    /// <summary>IDs of ResourceFiles to link as reference materials. Empty when IsSubmission = false.</summary>
    public List<int> LinkedResourceFileIds  { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Admin view of ALL operational tasks from the Tasks table.
//  Returned unfiltered; reserved for future operational-task screens.
//  Not shown in the global task-templates management page.
// ─────────────────────────────────────────────────────────────────────────────

public class OperationalTaskAdminDto
{
    public int       Id             { get; set; }
    public string    Title          { get; set; } = "";
    public string?   Description    { get; set; }
    /// <summary>"Personal" | "System" | "Mentor"</summary>
    public string    TaskType       { get; set; } = "";
    /// <summary>"Open" | "InProgress" | "Done"</summary>
    public string    Status         { get; set; } = "";
    public DateTime? DueDate        { get; set; }
    public DateTime  CreatedAt      { get; set; }
    public DateTime? ClosedAt       { get; set; }
    /// <summary>Name of the task creator (student for Personal, system/mentor for others).</summary>
    public string    CreatorName    { get; set; } = "";
    /// <summary>Name of the user this task is assigned to (may be empty).</summary>
    public string    AssignedToName { get; set; } = "";
    public int       ProjectNumber  { get; set; }
    public string    ProjectTitle   { get; set; } = "";
    /// <summary>Name of the milestone this task belongs to, or empty string.</summary>
    public string    MilestoneTitle { get; set; } = "";
}

/// <summary>
/// Attach a task template to a milestone template, or detach it.
///
/// A dedicated endpoint rather than a full PUT because the milestone editor
/// changes only this one field: sending the whole template back would let an
/// association change clobber a concurrent edit to the task's title, dates or
/// submission policy.
/// </summary>
public class SetTaskTemplateMilestoneRequest
{
    /// <summary>Target milestone template, or null to detach into the
    /// unassigned pool.</summary>
    public int? MilestoneTemplateId { get; set; }
}
