using System;
using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Request type definitions — admin-managed catalogue of student request types.
//
//  The legacy RequestTypes static constants are still used as the in-code
//  identifiers for the 7 built-in types and remain the source of Hebrew
//  labels for those types. Custom types added through the management screen
//  live only in the DB; their Name/Description come from the DTO.
// ─────────────────────────────────────────────────────────────────────────────

public class RequestTypeDefinitionDto
{
    public int      Id                 { get; set; }
    public string   Code               { get; set; } = "";
    public string   Name               { get; set; } = "";
    public string   Description        { get; set; } = "";
    public bool     IsActive           { get; set; }
    /// <summary>Comma-separated role names (Admin / Staff / Mentor).</summary>
    public string   HandlerRole        { get; set; } = "";
    public bool     AllowInternalNotes { get; set; }
    /// <summary>One of RequestTypeFileUploadPolicies values.</summary>
    public string   FileUploadPolicy   { get; set; } = "Optional";
    public bool     IsBuiltIn          { get; set; }
    public DateTime CreatedAt          { get; set; }
    public DateTime UpdatedAt          { get; set; }
    /// <summary>Number of ProjectRequests rows referencing this type.
    /// Used to decide whether the type can be hard-deleted.</summary>
    public int      UsageCount         { get; set; }
    public List<RequestTypeFieldDto> Fields { get; set; } = new();
}

public class RequestTypeFieldDto
{
    public int    Id            { get; set; }
    public int    RequestTypeId { get; set; }
    public string Label         { get; set; } = "";
    public string FieldKey      { get; set; } = "";
    /// <summary>One of RequestTypeFieldTypes values.</summary>
    public string FieldType     { get; set; } = "";
    public bool   IsRequired    { get; set; }
    /// <summary>Newline-separated options. Used by Select fields only.</summary>
    public string Options       { get; set; } = "";
    public int    SortOrder     { get; set; }
}

/// <summary>Request body for POST and PUT /api/request-types.</summary>
public class SaveRequestTypeRequest
{
    /// <summary>Stable string identifier. Required on create; ignored on update.</summary>
    public string Code               { get; set; } = "";
    public string Name               { get; set; } = "";
    public string Description        { get; set; } = "";
    public bool   IsActive           { get; set; } = true;
    public string HandlerRole        { get; set; } = "Admin,Staff";
    public bool   AllowInternalNotes { get; set; }
    public string FileUploadPolicy   { get; set; } = "Optional";
    public List<RequestTypeFieldDto> Fields { get; set; } = new();
}

public class ToggleRequestTypeActiveRequest
{
    public bool IsActive { get; set; }
}

// ── Controlled string constants ──────────────────────────────────────────────

public static class RequestTypeFieldTypes
{
    public const string ShortText = "ShortText";
    public const string LongText  = "LongText";
    public const string Date      = "Date";
    public const string File      = "File";
    public const string Select    = "Select";
    public const string Checkbox  = "Checkbox";

    public static readonly IReadOnlyList<string> All = new[]
    {
        ShortText, LongText, Date, File, Select, Checkbox
    };

    public static string Label(string t) => t switch
    {
        ShortText => "טקסט קצר",
        LongText  => "טקסט ארוך",
        Date      => "תאריך",
        File      => "העלאת קובץ",
        Select    => "רשימה נפתחת",
        Checkbox  => "תיבת סימון",
        _         => t,
    };
}

public static class RequestTypeFileUploadPolicies
{
    public const string Required = "Required";
    public const string Optional = "Optional";
    public const string Disabled = "Disabled";

    public static readonly IReadOnlyList<string> All = new[] { Required, Optional, Disabled };

    public static string Label(string p) => p switch
    {
        Required => "חובה",
        Optional => "אופציונלי",
        Disabled => "מבוטל",
        _        => p,
    };
}

/// <summary>The three roles that can be designated as handlers for a request
/// type. "Lecturer" in the UI maps to Staff. Tech-team / Admin distinctions
/// collapse into the Admin role per the Lecturer⇒Admin invariant.</summary>
public static class RequestTypeHandlerRoles
{
    public const string Admin  = "Admin";
    public const string Staff  = "Staff";
    public const string Mentor = "Mentor";

    public static readonly IReadOnlyList<string> All = new[] { Admin, Staff, Mentor };

    public static string Label(string r) => r switch
    {
        Admin  => "מנהל",
        Staff  => "מרצה",
        Mentor => "מנחה",
        _      => r,
    };
}