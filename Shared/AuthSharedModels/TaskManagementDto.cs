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
    public int      MilestoneTemplateId { get; set; }
    public string   MilestoneTitle      { get; set; } = "";
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
    public int      MilestoneTemplateId { get; set; }
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
