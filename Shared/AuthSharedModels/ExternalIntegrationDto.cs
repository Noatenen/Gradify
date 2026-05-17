using System;
using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Innovation-Team / Airtable integration — admin configuration DTOs.
//
//  These mappings let admins translate an arbitrary Airtable payload shape
//  into the canonical fields the webhook stores. Two surfaces:
//    • Field mappings : "rename" source keys to our internal target keys,
//                       optionally provide defaults / requiredness.
//    • Status mappings: translate source status values (e.g. "חדש") into
//                       canonical tokens (e.g. "received") + a Hebrew label.
//
//  No outbound Airtable calls — these only describe the *incoming* mapping.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Known "source systems" — currently only Airtable. Constant so we
/// don't sprinkle the string around the codebase.</summary>
public static class ExternalIntegrationSourceSystems
{
    public const string Airtable = "Airtable";

    public static readonly IReadOnlyCollection<string> All = new[] { Airtable };
}

/// <summary>
/// Canonical target-field names that field-mapping rows can write into.
/// These match the property names on <see cref="ExternalRequestUpdateRequest"/>
/// (camelCase to match the JSON contract documented in docs/external-api.md).
/// </summary>
public static class ExternalIntegrationTargetFields
{
    public const string ExternalRequestId = "externalRequestId";
    public const string StudentEmail      = "studentEmail";
    public const string StudentId         = "studentId";
    public const string ProjectId         = "projectId";
    public const string RequestType       = "requestType";
    public const string Status            = "status";
    public const string StatusLabel       = "statusLabel";
    public const string Notes             = "notes";
    public const string UpdatedAt         = "updatedAt";

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        ExternalRequestId, StudentEmail, StudentId, ProjectId,
        RequestType, Status, StatusLabel, Notes, UpdatedAt,
    };

    /// <summary>Hebrew display label for the admin dropdown. Unknown keys
    /// fall through to the camelCase name verbatim.</summary>
    public static string Label(string? key) =>
        key switch
        {
            ExternalRequestId => "מזהה בקשה חיצוני",
            StudentEmail      => "אימייל סטודנט",
            StudentId         => "מזהה סטודנט (פנימי)",
            ProjectId         => "מזהה פרויקט (פנימי)",
            RequestType       => "סוג בקשה",
            Status            => "סטטוס (מילת מפתח)",
            StatusLabel       => "סטטוס (תווית עברית)",
            Notes             => "הערות",
            UpdatedAt         => "תאריך עדכון",
            _                 => key ?? "",
        };
}

// ─────────────────────────────────────────────────────────────────────────────
//  Field mappings
// ─────────────────────────────────────────────────────────────────────────────

public class ExternalIntegrationFieldMappingDto
{
    public int      Id              { get; set; }
    public string   SourceSystem    { get; set; } = ExternalIntegrationSourceSystems.Airtable;
    public string   SourceFieldName { get; set; } = "";
    public string   TargetFieldName { get; set; } = "";
    public bool     IsRequired      { get; set; }
    public string   DefaultValue    { get; set; } = "";
    public bool     IsActive        { get; set; } = true;
    public string   Notes           { get; set; } = "";
    public DateTime? CreatedAt      { get; set; }
    public DateTime? UpdatedAt      { get; set; }
}

public class ExternalIntegrationFieldMappingSaveRequest
{
    public string  SourceSystem    { get; set; } = ExternalIntegrationSourceSystems.Airtable;
    public string  SourceFieldName { get; set; } = "";
    public string  TargetFieldName { get; set; } = "";
    public bool    IsRequired      { get; set; }
    public string? DefaultValue    { get; set; }
    public bool    IsActive        { get; set; } = true;
    public string? Notes           { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Status mappings
// ─────────────────────────────────────────────────────────────────────────────

public class ExternalIntegrationStatusMappingDto
{
    public int      Id                 { get; set; }
    public string   SourceSystem       { get; set; } = ExternalIntegrationSourceSystems.Airtable;
    public string   SourceStatusValue  { get; set; } = "";
    public string   InternalStatus     { get; set; } = "";
    public string   DisplayLabel       { get; set; } = "";
    public bool     IsTerminal         { get; set; }
    public bool     IsActive           { get; set; } = true;
    public DateTime? CreatedAt         { get; set; }
    public DateTime? UpdatedAt         { get; set; }
}

public class ExternalIntegrationStatusMappingSaveRequest
{
    public string  SourceSystem      { get; set; } = ExternalIntegrationSourceSystems.Airtable;
    public string  SourceStatusValue { get; set; } = "";
    public string  InternalStatus    { get; set; } = "";
    public string? DisplayLabel      { get; set; }
    public bool    IsTerminal        { get; set; }
    public bool    IsActive          { get; set; } = true;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Settings + test endpoint
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Surfaced on the integration settings page so admins can see, at a glance,
/// the endpoint Airtable should POST to + whether the API key is set.
/// The actual secret is NEVER returned.
/// </summary>
public class ExternalIntegrationSettingsDto
{
    public string  SourceSystem     { get; set; } = ExternalIntegrationSourceSystems.Airtable;
    public string  EndpointPath     { get; set; } = "/api/external-requests/update";
    public string  ApiKeyHeader     { get; set; } = "X-External-Api-Key";
    public bool    ApiKeyConfigured { get; set; }
    /// <summary>Length of the configured key, for UI feedback. Zero when not
    /// configured. The actual value is never sent.</summary>
    public int     ApiKeyLength     { get; set; }
    public int     ActiveFieldMappingsCount  { get; set; }
    public int     ActiveStatusMappingsCount { get; set; }
}

public class ExternalIntegrationTestRequest
{
    /// <summary>Raw JSON the admin pasted into the textarea.</summary>
    public string  Payload { get; set; } = "";
    /// <summary>When true (default), the endpoint applies the mappings and
    /// returns what would have been stored — without touching the DB.</summary>
    public bool    DryRun  { get; set; } = true;
}

public class ExternalIntegrationTestResponse
{
    public bool          Success         { get; set; }
    /// <summary>Human-readable error when Success is false. Empty otherwise.</summary>
    public string        Error           { get; set; } = "";
    /// <summary>The payload AFTER applying field + status mappings. Useful
    /// for the admin to see what the webhook will actually persist.</summary>
    public string        TransformedJson { get; set; } = "";
    /// <summary>Non-fatal warnings (e.g. "field X has no mapping — passed
    /// through verbatim", "required mapping Y missing in payload").</summary>
    public List<string>  Warnings        { get; set; } = new();
    /// <summary>"preview" when DryRun=true; otherwise "created" | "updated".</summary>
    public string        Action          { get; set; } = "preview";
}