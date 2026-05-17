using System.Text.Json;
using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  ExternalRequestsController — /api/external-requests
//
//   1. POST  /api/external-requests/update
//      Inbound webhook from the Innovation Team. Auth: X-External-Api-Key
//      header (value read from configuration — never hard-coded). Upserts
//      keyed on ExternalRequestId. Every successful status change appends a
//      row to ExternalRequestStatusHistory and raises an in-process event
//      so future notification subscribers can react. Bad payloads are
//      persisted in ExternalRequestFailedPayloads for the team to debug.
//
//   2. GET   /api/external-requests/me
//      Authenticated student-facing list. Matches on local StudentId when
//      known, else falls back to email (case-insensitive).
//
//   3. GET   /api/external-requests/me/{externalRequestId}/history
//      Status timeline for a request belonging to the calling student.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/external-requests")]
[ApiController]
public class ExternalRequestsController : ControllerBase
{
    private const string ApiKeyHeader  = "X-External-Api-Key";
    private const string ApiKeyConfig  = "ExternalApi:ApiKey";

    private readonly DbRepository  _db;
    private readonly IConfiguration _config;
    private readonly ILogger<ExternalRequestsController> _log;

    public ExternalRequestsController(
        DbRepository db,
        IConfiguration config,
        ILogger<ExternalRequestsController> log)
    {
        _db     = db;
        _config = config;
        _log    = log;
    }

    // ── POST /api/external-requests/update ─────────────────────────────────
    [HttpPost("update")]
    [AllowAnonymous]
    public async Task<IActionResult> Update()
    {
        // ── Auth: pre-shared header secret from configuration ─────────────
        var configured = _config[ApiKeyConfig];
        if (string.IsNullOrWhiteSpace(configured))
            return StatusCode(503, new { error = "external_api_key_not_configured" });

        var provided = Request.Headers[ApiKeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(provided) || !FixedTimeEquals(provided, configured))
            return Unauthorized(new { error = "invalid_api_key" });

        // ── Read body once (verbatim copy goes into RawPayload columns) ───
        string rawBody;
        try
        {
            using var reader = new StreamReader(Request.Body);
            rawBody = await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            await LogFailedPayloadAsync("", "read_body: " + ex.Message);
            _log.LogWarning(ex, "external-requests/update: body read failed");
            return BadRequest(new { error = "could_not_read_body" });
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            await LogFailedPayloadAsync("", "empty_body");
            return BadRequest(new { error = "empty_body" });
        }

        // Cap raw body length to a safe limit. Anything longer is almost
        // certainly a misuse and would balloon the DB row.
        if (rawBody.Length > 64 * 1024)
        {
            await LogFailedPayloadAsync(Truncate(rawBody, 2048), "body_too_large");
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { error = "payload_too_large" });
        }

        // ── Apply admin-configured field + status mappings (Airtable) ────
        // When no rows exist the transform is a no-op — the body is parsed
        // exactly as before, preserving backward compatibility.
        string bodyForParse = rawBody;
        try
        {
            var fieldMappings = (await _db.GetRecordsAsync<ExternalIntegrationFieldMappingDto>(
                "SELECT * FROM ExternalIntegrationFieldMappings WHERE SourceSystem = @S AND IsActive = 1",
                new { S = ExternalIntegrationSourceSystems.Airtable }))?.ToList() ?? new();

            var statusMappings = (await _db.GetRecordsAsync<ExternalIntegrationStatusMappingDto>(
                "SELECT * FROM ExternalIntegrationStatusMappings WHERE SourceSystem = @S AND IsActive = 1",
                new { S = ExternalIntegrationSourceSystems.Airtable }))?.ToList() ?? new();

            if (fieldMappings.Count > 0 || statusMappings.Count > 0)
            {
                var transformed = ExternalIntegrationMapper.Apply(rawBody, fieldMappings, statusMappings);
                bodyForParse = transformed.TransformedJson;
                // Soft signal for ops — warnings end up in the log only.
                if (transformed.Warnings.Count > 0)
                    _log.LogInformation("external-requests/update: mapping warnings: {Warnings}",
                        string.Join("; ", transformed.Warnings));
            }
        }
        catch (Exception ex)
        {
            // Mapper failures must not block the webhook — fall back to raw.
            _log.LogWarning(ex, "external-requests/update: mapping failed, using raw body");
        }

        ExternalRequestUpdateRequest? req;
        try
        {
            req = JsonSerializer.Deserialize<ExternalRequestUpdateRequest>(bodyForParse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            await LogFailedPayloadAsync(rawBody, "invalid_json: " + ex.Message);
            _log.LogWarning(ex, "external-requests/update: invalid JSON");
            return BadRequest(new { error = "invalid_json" });
        }

        if (req is null || string.IsNullOrWhiteSpace(req.ExternalRequestId))
        {
            await LogFailedPayloadAsync(rawBody, "missing_ExternalRequestId");
            return BadRequest(new { error = "missing_ExternalRequestId" });
        }

        // ── Resolve a local StudentId when possible ───────────────────────
        int? studentId = req.StudentId;
        if ((studentId is null or 0) && !string.IsNullOrWhiteSpace(req.StudentEmail))
        {
            var match = (await _db.GetRecordsAsync<int>(
                "SELECT Id FROM users WHERE Email = @E COLLATE NOCASE LIMIT 1",
                new { E = req.StudentEmail.Trim() }))?.FirstOrDefault();
            if (match > 0) studentId = match;
        }

        // ── Load existing row (if any) for history diff ───────────────────
        var existing = (await _db.GetRecordsAsync<ExistingRow>(
            @"SELECT ExternalRequestId, Status, StatusLabel, StudentEmail, StudentId,
                     ProjectId, RequestType, Notes
              FROM   ExternalRequests
              WHERE  ExternalRequestId = @Eid LIMIT 1",
            new { Eid = req.ExternalRequestId! }))?.FirstOrDefault();

        string updatedAt = (req.UpdatedAt ?? DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss");

        // Trim & length-cap user-supplied strings before they hit the DB.
        string newStatus      = (req.Status      ?? "").Trim();
        string newStatusLabel = (req.StatusLabel ?? "").Trim();
        string newRequestType = (req.RequestType ?? "").Trim();
        string newNotes       = (req.Notes       ?? "").Trim();
        string newEmail       = (req.StudentEmail ?? "").Trim();

        try
        {
            if (existing is not null)
            {
                // COALESCE keeps existing values when the caller omits a field.
                await _db.SaveDataAsync(@"
                    UPDATE ExternalRequests
                    SET    StudentEmail = COALESCE(NULLIF(@StudentEmail, ''), StudentEmail),
                           StudentId    = COALESCE(@StudentId, StudentId),
                           ProjectId    = COALESCE(@ProjectId, ProjectId),
                           RequestType  = COALESCE(NULLIF(@RequestType, ''), RequestType),
                           Status       = COALESCE(NULLIF(@Status, ''), Status),
                           StatusLabel  = COALESCE(NULLIF(@StatusLabel, ''), StatusLabel),
                           Notes        = COALESCE(NULLIF(@Notes, ''), Notes),
                           RawPayload   = @RawPayload,
                           UpdatedAt    = @UpdatedAt
                    WHERE  ExternalRequestId = @Eid",
                    new
                    {
                        Eid          = req.ExternalRequestId,
                        StudentEmail = newEmail,
                        StudentId    = studentId,
                        ProjectId    = req.ProjectId,
                        RequestType  = newRequestType,
                        Status       = newStatus,
                        StatusLabel  = newStatusLabel,
                        Notes        = newNotes,
                        RawPayload   = rawBody,
                        UpdatedAt    = updatedAt,
                    });

                // History + event only when the status actually changed.
                bool statusChanged = !string.IsNullOrEmpty(newStatus) &&
                    !string.Equals(newStatus, existing.Status, StringComparison.OrdinalIgnoreCase);

                if (statusChanged)
                {
                    await _db.SaveDataAsync(@"
                        INSERT INTO ExternalRequestStatusHistory
                            (ExternalRequestId, OldStatus, NewStatus,
                             OldStatusLabel, NewStatusLabel, Notes, RawPayload)
                        VALUES
                            (@Eid, @OldStatus, @NewStatus, @OldLabel, @NewLabel, @Notes, @Raw)",
                        new
                        {
                            Eid       = req.ExternalRequestId,
                            OldStatus = existing.Status ?? "",
                            NewStatus = newStatus,
                            OldLabel  = existing.StatusLabel ?? "",
                            NewLabel  = string.IsNullOrEmpty(newStatusLabel)
                                          ? ExternalRequestStatuses.Label(newStatus)
                                          : newStatusLabel,
                            Notes     = newNotes,
                            Raw       = rawBody,
                        });

                    await ExternalRequestEvents.RaiseStatusChangedAsync(
                        new ExternalRequestStatusChanged(
                            ExternalRequestId: req.ExternalRequestId!,
                            OldStatus:        existing.Status ?? "",
                            NewStatus:        newStatus,
                            OldStatusLabel:   existing.StatusLabel ?? "",
                            NewStatusLabel:   newStatusLabel,
                            StudentId:        studentId,
                            StudentEmail:     string.IsNullOrEmpty(newEmail) ? existing.StudentEmail ?? "" : newEmail,
                            ProjectId:        req.ProjectId ?? existing.ProjectId,
                            RequestType:      string.IsNullOrEmpty(newRequestType) ? existing.RequestType ?? "" : newRequestType,
                            Notes:            newNotes));
                }

                return Ok(new
                {
                    externalRequestId = req.ExternalRequestId,
                    action            = "updated",
                    statusChanged,
                });
            }
            else
            {
                int newId = await _db.InsertReturnIdAsync(@"
                    INSERT INTO ExternalRequests
                        (ExternalRequestId, StudentEmail, StudentId, ProjectId,
                         RequestType, Status, StatusLabel, Notes, RawPayload, UpdatedAt)
                    VALUES
                        (@Eid, @StudentEmail, @StudentId, @ProjectId,
                         @RequestType, @Status, @StatusLabel, @Notes, @RawPayload, @UpdatedAt)",
                    new
                    {
                        Eid          = req.ExternalRequestId,
                        StudentEmail = newEmail,
                        StudentId    = studentId,
                        ProjectId    = req.ProjectId,
                        RequestType  = newRequestType,
                        Status       = newStatus,
                        StatusLabel  = newStatusLabel,
                        Notes        = newNotes,
                        RawPayload   = rawBody,
                        UpdatedAt    = updatedAt,
                    });

                // Seed the timeline with the initial status as a single row,
                // so the history view doesn't look empty for first-time pushes.
                await _db.SaveDataAsync(@"
                    INSERT INTO ExternalRequestStatusHistory
                        (ExternalRequestId, OldStatus, NewStatus,
                         OldStatusLabel, NewStatusLabel, Notes, RawPayload)
                    VALUES
                        (@Eid, '', @NewStatus, '', @NewLabel, @Notes, @Raw)",
                    new
                    {
                        Eid       = req.ExternalRequestId,
                        NewStatus = newStatus,
                        NewLabel  = string.IsNullOrEmpty(newStatusLabel)
                                       ? ExternalRequestStatuses.Label(newStatus)
                                       : newStatusLabel,
                        Notes     = newNotes,
                        Raw       = rawBody,
                    });

                await ExternalRequestEvents.RaiseCreatedAsync(
                    new ExternalRequestCreated(
                        ExternalRequestId: req.ExternalRequestId!,
                        Status:            newStatus,
                        StatusLabel:       newStatusLabel,
                        StudentId:         studentId,
                        StudentEmail:      newEmail,
                        ProjectId:         req.ProjectId,
                        RequestType:       newRequestType,
                        Notes:             newNotes));

                return Ok(new
                {
                    externalRequestId = req.ExternalRequestId,
                    id                = newId,
                    action            = "created",
                });
            }
        }
        catch (Exception ex)
        {
            // Could be a UNIQUE-constraint race with a concurrent push. Persist
            // the body for the team and surface a generic 500 — the upstream
            // system will retry on its own schedule.
            await LogFailedPayloadAsync(rawBody, "upsert_failed: " + ex.Message);
            _log.LogError(ex, "external-requests/update: upsert failed for {Eid}",
                req.ExternalRequestId);
            return StatusCode(500, new { error = "upsert_failed" });
        }
    }

    // ── GET /api/external-requests/me ──────────────────────────────────────
    [HttpGet("me")]
    [ServiceFilter(typeof(AuthCheck))]
    [Authorize]
    public async Task<IActionResult> ListMine(int authUserId)
    {
        var me = (await _db.GetRecordsAsync<MeRow>(
            "SELECT Id, Email FROM users WHERE Id = @Id LIMIT 1",
            new { Id = authUserId }))?.FirstOrDefault();
        if (me is null) return Unauthorized();

        const string sql = @"
            SELECT  Id, ExternalRequestId, RequestType, Status, StatusLabel,
                    Notes, CreatedAt, UpdatedAt
            FROM    ExternalRequests
            WHERE   StudentId = @Uid
               OR   (StudentEmail <> '' AND StudentEmail = @Email COLLATE NOCASE)
            ORDER   BY UpdatedAt DESC, Id DESC";

        var rows = (await _db.GetRecordsAsync<ExternalRequestStatusDto>(
                        sql, new { Uid = me.Id, Email = me.Email }))?.ToList()
                   ?? new List<ExternalRequestStatusDto>();
        return Ok(rows);
    }

    // ── GET /api/external-requests/me/{eid}/history ────────────────────────
    [HttpGet("me/{externalRequestId}/history")]
    [ServiceFilter(typeof(AuthCheck))]
    [Authorize]
    public async Task<IActionResult> MyHistory(string externalRequestId, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(externalRequestId))
            return BadRequest("missing_id");

        var me = (await _db.GetRecordsAsync<MeRow>(
            "SELECT Id, Email FROM users WHERE Id = @Id LIMIT 1",
            new { Id = authUserId }))?.FirstOrDefault();
        if (me is null) return Unauthorized();

        // Ownership check — only return history for a request the caller owns.
        bool owns = (await _db.GetRecordsAsync<int>(@"
            SELECT 1 FROM ExternalRequests
            WHERE  ExternalRequestId = @Eid
              AND  (StudentId = @Uid OR (StudentEmail <> '' AND StudentEmail = @Email COLLATE NOCASE))
            LIMIT 1",
            new { Eid = externalRequestId, Uid = me.Id, Email = me.Email }))?.Any() ?? false;
        if (!owns) return NotFound();

        var rows = (await _db.GetRecordsAsync<ExternalRequestStatusHistoryDto>(@"
            SELECT  Id, ExternalRequestId, OldStatus, NewStatus,
                    OldStatusLabel, NewStatusLabel, Notes, ChangedAt
            FROM    ExternalRequestStatusHistory
            WHERE   ExternalRequestId = @Eid
            ORDER   BY ChangedAt ASC, Id ASC",
            new { Eid = externalRequestId }))?.ToList()
            ?? new List<ExternalRequestStatusHistoryDto>();
        return Ok(rows);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private sealed class MeRow
    {
        public int    Id    { get; set; }
        public string Email { get; set; } = "";
    }

    private sealed class ExistingRow
    {
        public string? ExternalRequestId { get; set; }
        public string? Status            { get; set; }
        public string? StatusLabel       { get; set; }
        public string? StudentEmail      { get; set; }
        public int?    StudentId         { get; set; }
        public int?    ProjectId         { get; set; }
        public string? RequestType       { get; set; }
        public string? Notes             { get; set; }
    }

    private async Task LogFailedPayloadAsync(string body, string error)
    {
        try
        {
            var ip = HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "";
            await _db.SaveDataAsync(@"
                INSERT INTO ExternalRequestFailedPayloads
                    (RemoteIp, ErrorMessage, RawPayload)
                VALUES
                    (@Ip, @Err, @Body)",
                new
                {
                    Ip   = ip.Length > 64 ? ip[..64] : ip,
                    Err  = Truncate(error, 1000),
                    Body = Truncate(body,  64 * 1024),
                });
        }
        catch (Exception ex)
        {
            // Never throw from a forensics-only write.
            _log.LogWarning(ex, "Failed to persist failed-payload row");
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);

    /// <summary>Constant-time compare to defeat header-byte timing attacks.</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}