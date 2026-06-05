using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>Result returned from POST /api/airtable/sync-projects.</summary>
public class AirtableSyncResultDto
{
    public int          TotalFetched { get; set; }
    public int          Inserted     { get; set; }
    public int          Updated      { get; set; }
    /// <summary>Records that the admin explicitly excluded from this import
    /// (typically suspected duplicates left unchecked in the preview).
    /// Counted separately from Failed so the audit row can distinguish
    /// "I chose not to import this" from "the system tried and failed".</summary>
    public int          Skipped      { get; set; }
    public int          Failed       { get; set; }
    public List<string> Errors       { get; set; } = new();

    /// <summary>
    /// Set when the entire sync could not run (not configured, network error, etc.).
    /// Individual record failures are captured in <see cref="Errors"/> instead.
    /// </summary>
    public string? SyncError { get; set; }
}

/// <summary>Request body for POST /api/integrations/airtable/{id}/import.
/// Optional — when omitted, every fetched Airtable record is imported.
/// When supplied, records whose Airtable RecordId is in the list are
/// counted as Skipped instead of processed.</summary>
public class AirtableImportRequest
{
    public List<string> SkipRecordIds { get; set; } = new();
}

// ── Fixture preview (admin / QA only) ───────────────────────────────────────
//
// Lets a developer or QA engineer run the preview pipeline against a
// hand-crafted list of fake "Airtable" records, without making a real
// Airtable HTTP call and without writing anything to the DB. Used to
// validate that the New / Update / Warning / Error buckets fire as
// expected for the three core scenarios:
//
//   • New record       — RecordId that doesn't match any local row
//   • Existing update  — RecordId matches a row; some field differs
//   • Suspected dup    — RecordId is new but Title/ProjectNumber collide
//
// See QA documentation in the matching PR for sample payloads.

public class AirtableFixtureRecordDto
{
    public string  RecordId         { get; set; } = "";
    public int?    ProjectNumber    { get; set; }
    public string  Title            { get; set; } = "";
    public string  OrganizationName { get; set; } = "";
    public string  OrganizationType { get; set; } = "";
    public string  Description      { get; set; } = "";
    public string  Status           { get; set; } = "";
    public string  ProjectTopic     { get; set; } = "";
    public string  Contents         { get; set; } = "";
    public string  ContactPerson    { get; set; } = "";
    public string  ContactRole      { get; set; } = "";
    public string  ContactEmail     { get; set; } = "";
    public string  ContactPhone     { get; set; } = "";
    public string  Goals            { get; set; } = "";
    public string  TargetAudience   { get; set; } = "";
    public string  Priority         { get; set; } = "";
}

public class AirtableFixturePreviewRequest
{
    public List<AirtableFixtureRecordDto> Records { get; set; } = new();
}

// ── Preview (dry-run) ───────────────────────────────────────────────────────
//
// POST /api/integrations/airtable/{id}/preview returns this DTO. No DB writes
// happen during preview — the response is the admin's "what would change"
// confirmation surface before they decide to run the real import.

/// <summary>Per-record bucket label used by the preview UI.</summary>
public static class AirtablePreviewKinds
{
    public const string New     = "New";     // No existing row with this AirtableRecordId
    public const string Update  = "Update";  // Row exists; at least one field would change
    public const string Warning = "Warning"; // Row will be created/updated, but data is suspicious
    public const string Error   = "Error";   // Row cannot be imported (e.g. missing required field)
}

public class AirtablePreviewRowDto
{
    /// <summary>"New" | "Update" | "Warning" | "Error" — see AirtablePreviewKinds.</summary>
    public string  Kind             { get; set; } = "";
    public string  RecordId         { get; set; } = "";
    public int?    ProjectNumber    { get; set; }
    public string  Title            { get; set; } = "";
    public string  OrganizationName { get; set; } = "";
    /// <summary>For Warning/Error, the reason in Hebrew. For Update, the list
    /// of fields that would change (comma-joined Hebrew labels). For New,
    /// empty.</summary>
    public string  Detail           { get; set; } = "";
    /// <summary>Set on Update rows — local Projects.Id of the existing row,
    /// useful for the UI to deep-link if needed.</summary>
    public int?    ExistingProjectId { get; set; }
}

public class AirtablePreviewResultDto
{
    public int    TotalFetched { get; set; }
    public int    NewCount     { get; set; }
    public int    UpdateCount  { get; set; }
    public int    WarningCount { get; set; }
    public int    ErrorCount   { get; set; }

    /// <summary>All rows in one flat list. UI filters by Kind for the four
    /// colored sections. Keeping it flat avoids duplicating per-row data.</summary>
    public List<AirtablePreviewRowDto> Rows { get; set; } = new();

    /// <summary>Set when the entire preview could not run (no config, network
    /// error, etc.). Per-record problems land in <see cref="Rows"/> with
    /// Kind = "Error" instead.</summary>
    public string? PreviewError { get; set; }
}

// ── Import audit trail ──────────────────────────────────────────────────────

/// <summary>One row of AirtableImportRuns surfaced via
/// GET /api/integrations/airtable/{id}/import-runs.</summary>
public class AirtableImportRunDto
{
    public int       Id                    { get; set; }
    public int       IntegrationSettingsId { get; set; }
    public int?      TriggeredByUserId     { get; set; }
    public string    TriggeredByName       { get; set; } = "";
    public DateTime  StartedAt             { get; set; }
    public DateTime? FinishedAt            { get; set; }
    public int       TotalFetched          { get; set; }
    public int       Inserted              { get; set; }
    public int       Updated               { get; set; }
    public int       Failed                { get; set; }
    public int       Skipped               { get; set; }
    /// <summary>"Success" | "PartialFailure" | "Failure" — derived at write
    /// time so the list can color-code rows without re-computing.</summary>
    public string    Status                { get; set; } = "Success";
    public string    ErrorSummary          { get; set; } = "";
}
