using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  תוצרי הגשה — the faculty's graduation-deliverable catalog.
//
//  A SEPARATE DOMAIN FROM TASKS, and deliberately so. Tasks/TaskTemplates are
//  the mid-course milestone pipeline (submit → mentor approve → Moodle);
//  deliverables are the official products the faculty expects at the end. They
//  share no table and no foreign key, exactly as the two existing
//  deliverable-keyed tables already document.
//
//  WHAT THIS MODEL OWNS: the DEFINITION only — what a deliverable is called and
//  what it requires. It does NOT own:
//      ProjectSubmissionStatuses   a team's progress, keyed by DeliverableKey
//      ProjectResources            a team's own links, keyed by DeliverableKey
//  Both keep their own tables and keep joining on the string Key, which is why
//  the eight shipped keys are migrated verbatim and are not editable.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One deliverable as the STUDENT screen reads it. Shaped to match the
/// client's existing <c>SubmissionDeliverable</c> record one-for-one, so the
/// three student components render it unchanged.</summary>
public class DeliverableDto
{
    /// <summary>Stable join key — the value ProjectSubmissionStatuses and
    /// ProjectResources already store. Never rendered, never edited.</summary>
    public string Key               { get; set; } = "";
    public string Title             { get; set; } = "";
    public string IconPath          { get; set; } = "";
    public string? Intro            { get; set; }

    /// <summary>The heading above the requirement list — "מה כולל התוכן",
    /// "פרקי החוברת", "שלבי ההעלאה". Per-deliverable, not a constant.</summary>
    public string RequirementsLabel { get; set; } = "";

    public List<string> Requirements { get; set; } = new();
    public List<string> Notes        { get; set; } = new();
}

/// <summary>The management row. Adds the fields authoring needs and the two
/// counts the list column shows, so the page needs one call.</summary>
public class DeliverableAdminDto : DeliverableDto
{
    public int  Id         { get; set; }
    public int  OrderIndex { get; set; }
    public bool IsActive   { get; set; }

    public int RequirementCount => Requirements.Count;
    public int NoteCount        => Notes.Count;
}

/// <summary>
/// Create/update payload.
///
/// <para><b>Key is absent on purpose.</b> For an existing deliverable it is
/// immutable — ProjectSubmissionStatuses and ProjectResources rows already
/// point at it, and renaming it would silently orphan every team's progress.
/// For a new one the server derives it. A lecturer manages the visible Title
/// and never sees or invents a database identifier.</para>
///
/// <para>Requirements and Notes are sent as ordered lists and replace what is
/// stored — the client owns their order, so add / edit / remove / reorder are
/// all the same save.</para>
/// </summary>
public class SaveDeliverableRequest
{
    public string  Title             { get; set; } = "";
    public string? Intro             { get; set; }
    public string  RequirementsLabel { get; set; } = "";
    public bool    IsActive          { get; set; } = true;

    public List<string> Requirements { get; set; } = new();
    public List<string> Notes        { get; set; } = new();
}

/// <summary>Reorder payload — the deliverable ids in their new display order.
/// Sent whole rather than as per-row index edits, so the list can never end up
/// with duplicate or gapped OrderIndex values.</summary>
public class ReorderDeliverablesRequest
{
    public List<int> OrderedIds { get; set; } = new();
}
