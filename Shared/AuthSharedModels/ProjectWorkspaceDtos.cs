namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Student project workspace (מרחב הפרויקט) — request/response contracts.
//
//  Everything here is scoped to the CALLER'S OWN project on the server side;
//  no DTO carries a ProjectId, so a client cannot address someone else's
//  project by changing a payload.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The two fields a student team owns on its own project.
///
/// These are written to ProjectTeamProfile, never to Projects.Title /
/// Projects.Description — those are catalog columns that the Airtable sync
/// overwrites on every run.
/// </summary>
public class UpdateMyProjectRequest
{
    /// <summary>The project's display name inside Motiva. Empty or whitespace
    /// clears the team's override, so the catalog title shows again.</summary>
    public string? Title { get; set; }

    /// <summary>The team's own description of the project. Empty or whitespace
    /// clears the override.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// One link in משאבי הפרויקט — the team's own working tools and materials.
///
/// No Kind field: the resource's type (Figma / GitHub / Drive …) is derived
/// from <see cref="Url"/> at render time, so it can never disagree with the
/// link it labels.
/// </summary>
public class ProjectResourceDto
{
    public int    Id    { get; set; }
    public string Label { get; set; } = "";
    public string Url   { get; set; } = "";
}

/// <summary>
/// One submission category's team-controlled status.
///
/// <para><c>DeliverableKey</c> is the catalog key (see
/// SubmissionDeliverablesCatalog on the client). <c>Status</c> is one of
/// <see cref="SubmissionStatusValues"/>.</para>
/// </summary>
public class SubmissionStatusDto
{
    public string DeliverableKey { get; set; } = "";
    public string Status         { get; set; } = SubmissionStatusValues.NotStarted;
}

/// <summary>
/// Named for the DELIVERABLE, not just "submission": TaskSubmissionDto already
/// owns an UpdateSubmissionStatusRequest for the milestone submission pipeline,
/// and these two are different domains that must not be confused.
/// </summary>
public class UpdateDeliverableStatusRequest
{
    public string? Status { get; set; }
}

/// <summary>
/// The three states a submission category can be in. Strings rather than an
/// enum so the stored value is readable in the database and an unknown value
/// can be rejected explicitly instead of silently becoming 0.
/// </summary>
public static class SubmissionStatusValues
{
    public const string NotStarted = "NotStarted";
    public const string InProgress = "InProgress";
    public const string Done       = "Done";

    public static bool IsValid(string? value) =>
        value is NotStarted or InProgress or Done;
}

public class CreateProjectResourceRequest
{
    public string? Label { get; set; }

    /// <summary>Must be an absolute http/https URL — the server rejects
    /// anything else, so a stored link can never be a javascript: or data:
    /// payload rendered into an anchor.</summary>
    public string? Url { get; set; }
}
