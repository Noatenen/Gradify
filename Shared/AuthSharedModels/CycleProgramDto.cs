using System;
using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Cycle Program — the milestones configured for ONE academic year.
//
//  The record behind every row here is an AcademicYearMilestones row (AYM):
//  the association of a MilestoneTemplate with a cycle. That table already
//  existed and is already read by the student roadmap, the mentor calendar and
//  the project workspace; these DTOs only give the Admin screen a shape to read
//  and write it through.
//
//  WHICH FIELDS LIVE WHERE — this split drives the whole screen:
//
//    MilestoneTemplates (SHARED across every cycle that uses the template)
//        Title, Description, ProjectTypeId, IsRequired, OrderIndex
//
//    AcademicYearMilestones (OWNED by this cycle alone)
//        OpenDate, DueDate, CloseDate, IsActive, DisplayOrder, RoadmapStageId
//
//  Editing a title from inside a cycle therefore edits the template for every
//  cycle using it. That is deliberate — AYM is the existing association
//  mechanism and cloning templates per cycle would fork the library — but it is
//  never silent: TemplateCycleUsage tells the client how many cycles share the
//  template so the edit modal can warn before saving.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One milestone in one cycle's program: the AcademicYearMilestones row joined
/// to its MilestoneTemplate, plus the two counts the Admin screen needs.
/// </summary>
public class CycleMilestoneDto
{
    /// <summary>AcademicYearMilestones.Id — the row this cycle actually owns.</summary>
    public int Id { get; set; }

    /// <summary>The shared MilestoneTemplates row this milestone is based on.</summary>
    public int MilestoneTemplateId { get; set; }

    public int AcademicYearId { get; set; }

    // ── Template-level (shared) ───────────────────────────────────────────────
    public string  Title       { get; set; } = "";
    public string? Description { get; set; }
    public bool    IsRequired  { get; set; }

    /// <summary>Null means the milestone applies to both project types.</summary>
    public int?   ProjectTypeId { get; set; }

    /// <summary>Resolved label: "שניהם" | "טכנולוגי" | "מתודולוגי".</summary>
    public string Applicability { get; set; } = "שניהם";

    // ── Cycle-level (owned by this cycle) ─────────────────────────────────────

    /// <summary>
    /// Resolved position: COALESCE(AcademicYearMilestones.DisplayOrder,
    /// MilestoneTemplates.OrderIndex). The server sorts by this, so the client
    /// never re-sorts — it renders the list in the order it was handed.
    /// </summary>
    public int OrderIndex { get; set; }

    public DateTime? OpenDate  { get; set; }
    public DateTime? DueDate   { get; set; }
    public DateTime? CloseDate { get; set; }

    /// <summary>Per-cycle active flag. An inactive milestone stays in the
    /// program but is not presented on project progress.</summary>
    public bool IsActive { get; set; }

    /// <summary>Roadmap stage binding, managed by the stages screen. Read-only here.</summary>
    public int? RoadmapStageId { get; set; }

    // ── Counts ────────────────────────────────────────────────────────────────

    /// <summary>Active TaskTemplates hanging off this milestone template — the
    /// tasks "החלה על פרויקטי המחזור" would create per project.</summary>
    public int TaskTemplateCount { get; set; }

    /// <summary>Projects that already have this milestone instantiated
    /// (ProjectMilestones). Non-zero makes removal destructive.</summary>
    public int ProjectCount { get; set; }

    /// <summary>How many cycles use the underlying template. &gt; 1 means an edit
    /// to the shared fields reaches other cycles.</summary>
    public int TemplateCycleUsage { get; set; }
}

/// <summary>
/// Create/update payload for a milestone inside a cycle.
///
/// One request writes BOTH tables: the shared fields go to MilestoneTemplates,
/// the dates and the active flag to that cycle's AcademicYearMilestones row.
/// </summary>
public class SaveCycleMilestoneRequest
{
    // Template-level
    public string  Title         { get; set; } = "";
    public string? Description   { get; set; }
    public bool    IsRequired    { get; set; } = true;
    public int?    ProjectTypeId { get; set; }

    // Cycle-level
    public DateTime? OpenDate { get; set; }
    public DateTime? DueDate  { get; set; }
    public bool      IsActive { get; set; } = true;
}

/// <summary>Direction for a one-step reorder inside the cycle program.</summary>
public class MoveCycleMilestoneRequest
{
    /// <summary>-1 moves the milestone one position earlier, +1 one later.</summary>
    public int Direction { get; set; }
}

/// <summary>
/// A MilestoneTemplate offered to a cycle by the "החלת תבניות" picker.
/// Only templates NOT already in the cycle are returned — the AYM table's
/// UNIQUE(AcademicYearId, MilestoneTemplateId) makes adding one twice
/// impossible anyway, and offering it would be a dead choice.
/// </summary>
public class AvailableMilestoneTemplateDto
{
    public int     Id            { get; set; }
    public string  Title         { get; set; } = "";
    public string? Description   { get; set; }
    public int     OrderIndex    { get; set; }
    public bool    IsRequired    { get; set; }
    public int?    ProjectTypeId { get; set; }
    public string  Applicability { get; set; } = "שניהם";

    /// <summary>Template-level default dates, copied into the cycle on apply.</summary>
    public DateTime? OpenDate  { get; set; }
    public DateTime? DueDate   { get; set; }
    public DateTime? CloseDate { get; set; }

    /// <summary>Active TaskTemplates carried by this template.</summary>
    public int TaskTemplateCount { get; set; }
}

/// <summary>Which library templates to add to the cycle's program.</summary>
public class ApplyMilestoneTemplatesRequest
{
    public List<int> TemplateIds { get; set; } = new();
}

/// <summary>Result of adding library templates to a cycle's program.</summary>
public class ApplyMilestoneTemplatesResultDto
{
    /// <summary>AcademicYearMilestones rows created.</summary>
    public int Added { get; set; }

    /// <summary>Requested templates already present in the cycle.</summary>
    public int Skipped { get; set; }
}
