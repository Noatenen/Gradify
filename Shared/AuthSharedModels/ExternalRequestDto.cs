using System;
using System.Text.Json.Serialization;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  External Requests — status updates pushed to us by the Innovation Team.
//
//  Wire shape is intentionally loose: the upstream contract is not finalised
//  yet, so we accept any combination of known fields plus a free-form
//  RawPayload for forensics / future evolution.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Incoming update from the Innovation Team. Only ExternalRequestId is
/// strictly required (it's the upsert key). Everything else is optional;
/// missing fields are preserved from the previous push on this id.
/// </summary>
public class ExternalRequestUpdateRequest
{
    /// <summary>Upstream key. Required. Used to upsert by UNIQUE constraint.</summary>
    public string?  ExternalRequestId { get; set; }

    /// <summary>Email of the requesting student. We resolve to a local
    /// StudentId on the server side when we recognise the email.</summary>
    public string?  StudentEmail      { get; set; }

    /// <summary>Optional explicit StudentId — used when the Innovation Team
    /// already knows our internal user id (e.g. via a future sync).</summary>
    public int?     StudentId         { get; set; }

    /// <summary>Optional explicit ProjectId — same rationale as StudentId.</summary>
    public int?     ProjectId         { get; set; }

    /// <summary>Free-text category — e.g. "InnovationRequest". Not enforced.</summary>
    public string?  RequestType       { get; set; }

    /// <summary>Machine-readable status token — e.g. "approved", "rejected",
    /// "in_review". Treated as opaque; UI uses StatusLabel for display.</summary>
    public string?  Status            { get; set; }

    /// <summary>Human-readable Hebrew label of the status — displayed verbatim
    /// to students. Empty when the team only sends a machine status.</summary>
    public string?  StatusLabel       { get; set; }

    /// <summary>Optional update timestamp from the upstream system. When null
    /// we use the server's current time as UpdatedAt.</summary>
    public DateTime? UpdatedAt        { get; set; }

    /// <summary>Free-text notes (lecturer/innovation-team comments etc.).</summary>
    public string?  Notes             { get; set; }

    /// <summary>
    /// Captures unrecognised top-level fields so we can persist the full body
    /// without code changes. Used together with RawPayload below — the server
    /// will store the raw JSON regardless.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object>? Extra { get; set; }
}

/// <summary>
/// Student-facing read shape. Returned by GET /api/external-requests/me.
/// Hidden internals (RawPayload, ids) are omitted on purpose.
/// </summary>
public class ExternalRequestStatusDto
{
    public int       Id                 { get; set; }
    public string    ExternalRequestId  { get; set; } = "";
    public string    RequestType        { get; set; } = "";
    public string    Status             { get; set; } = "";
    public string    StatusLabel        { get; set; } = "";
    public string    Notes              { get; set; } = "";
    public DateTime? CreatedAt          { get; set; }
    public DateTime? UpdatedAt          { get; set; }
}

/// <summary>
/// One row of the status timeline. Returned by future endpoints that surface
/// the full history; the table is also written to on every upsert that
/// changes the Status column.
/// </summary>
public class ExternalRequestStatusHistoryDto
{
    public int       Id                 { get; set; }
    public string    ExternalRequestId  { get; set; } = "";
    public string    OldStatus          { get; set; } = "";
    public string    NewStatus          { get; set; } = "";
    public string    OldStatusLabel     { get; set; } = "";
    public string    NewStatusLabel     { get; set; } = "";
    public string    Notes              { get; set; } = "";
    public DateTime? ChangedAt          { get; set; }
}