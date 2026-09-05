using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  StudentProjectDetailsDto
//
//  Student-safe view of a project's full details.
//  Excludes all internal/management fields:
//    HealthStatus, Priority, InternalNotes, SourceType, AirtableRecordId,
//    TeamId, IsAssigned, assignment member counts.
//
//  Returned by GET /api/projects/my-project-details.
//  The endpoint is scoped to the requesting user's own project only.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Full project details for the student dashboard modal.</summary>
public class StudentProjectDetailsDto
{
    public int    Id            { get; set; }
    public int    ProjectNumber { get; set; }
    public string Title         { get; set; } = "";
    public string ProjectType   { get; set; } = "";
    public string AcademicYear  { get; set; } = "";

    // ── Main content ─────────────────────────────────────────────────────────
    /// <summary>Problem/need statement.</summary>
    public string? Description    { get; set; }
    public string? Goals          { get; set; }
    public string? TargetAudience { get; set; }
    /// <summary>High-level topic (Airtable-sourced; may be null for manual entries).</summary>
    public string? ProjectTopic   { get; set; }
    /// <summary>Extended content / scope description (Airtable-sourced).</summary>
    public string? Contents       { get; set; }

    // ── Organization / client contact ────────────────────────────────────────
    public string? OrganizationName { get; set; }
    public string? OrganizationType { get; set; }
    public string? ContactPerson    { get; set; }
    public string? ContactRole      { get; set; }
    public string? ContactEmail     { get; set; }
    public string? ContactPhone     { get; set; }

    // ── Branding ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Public URL of the team's uploaded project logo, or null when the team
    /// has not uploaded one.
    ///
    /// <para>Built server-side from ProjectTeamProfile.LogoPath, which stores
    /// the bare filename — the same split users.ProfileImagePath uses. The
    /// client never composes this path itself: the server names the stored
    /// file, so its response is the only authoritative source for the URL.</para>
    /// </summary>
    public string? LogoUrl { get; set; }
}

/// <summary>
/// Body of PUT /api/projects/my-project/logo.
///
/// <para>Same shape as <c>UploadAvatarRequest</c> because it is the same
/// transport — a base64 image plus its extension — but kept as its own type:
/// these are two different features, and a project-workspace call site reading
/// "UploadAvatarRequest" would be describing the wrong thing.</para>
/// </summary>
public class UploadProjectLogoRequest
{
    public string ImageBase64 { get; set; } = "";
    public string Extension   { get; set; } = "";
}
