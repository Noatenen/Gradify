using System.Text.Json;
using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  ExternalIntegrationController — /api/external-integration
//
//  Admin/Staff-only configuration surface for the incoming Innovation-Team
//  (Airtable) webhook. Stores:
//    • Field mappings  — translate Airtable column names → our camelCase
//                        target field contract
//    • Status mappings — translate Airtable status values → our canonical
//                        status tokens + Hebrew labels
//
//  Plus an Admin-only dry-run test endpoint that applies the configured
//  mappings to a sample JSON payload so admins can validate the mappings
//  *before* asking Airtable to hit the real webhook.
//
//  The webhook itself (ExternalRequestsController) consumes these mappings
//  transparently — when no rows are configured the system falls back to
//  the original camelCase contract.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/external-integration")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = $"{Roles.Admin},{Roles.Staff}")]
public class ExternalIntegrationController : ControllerBase
{
    private const string ApiKeyConfigPath = "ExternalApi:ApiKey";

    private readonly DbRepository   _db;
    private readonly IConfiguration _config;

    public ExternalIntegrationController(DbRepository db, IConfiguration config)
    {
        _db     = db;
        _config = config;
    }

    // ── GET /api/external-integration/settings ─────────────────────────────
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(int authUserId,
        [FromQuery] string sourceSystem = ExternalIntegrationSourceSystems.Airtable)
    {
        var apiKey = _config[ApiKeyConfigPath] ?? "";

        var counts = (await _db.GetRecordsAsync<CountsRow>(@"
            SELECT
                (SELECT COUNT(1) FROM ExternalIntegrationFieldMappings
                 WHERE SourceSystem = @S AND IsActive = 1) AS FieldCount,
                (SELECT COUNT(1) FROM ExternalIntegrationStatusMappings
                 WHERE SourceSystem = @S AND IsActive = 1) AS StatusCount",
            new { S = sourceSystem }))?.FirstOrDefault() ?? new CountsRow();

        return Ok(new ExternalIntegrationSettingsDto
        {
            SourceSystem              = sourceSystem,
            EndpointPath              = "/api/external-requests/update",
            ApiKeyHeader              = "X-External-Api-Key",
            ApiKeyConfigured          = !string.IsNullOrWhiteSpace(apiKey),
            ApiKeyLength              = string.IsNullOrWhiteSpace(apiKey) ? 0 : apiKey.Length,
            ActiveFieldMappingsCount  = counts.FieldCount,
            ActiveStatusMappingsCount = counts.StatusCount,
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Field mappings
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet("field-mappings")]
    public async Task<IActionResult> ListFieldMappings(int authUserId,
        [FromQuery] string sourceSystem = ExternalIntegrationSourceSystems.Airtable)
    {
        var rows = (await _db.GetRecordsAsync<ExternalIntegrationFieldMappingDto>(@"
            SELECT  Id, SourceSystem, SourceFieldName, TargetFieldName,
                    IsRequired, DefaultValue, IsActive, Notes,
                    CreatedAt, UpdatedAt
            FROM    ExternalIntegrationFieldMappings
            WHERE   SourceSystem = @S
            ORDER   BY IsActive DESC, TargetFieldName, SourceFieldName",
            new { S = sourceSystem }))?.ToList()
            ?? new List<ExternalIntegrationFieldMappingDto>();
        return Ok(rows);
    }

    [HttpPost("field-mappings")]
    public async Task<IActionResult> CreateFieldMapping(int authUserId,
        [FromBody] ExternalIntegrationFieldMappingSaveRequest req)
    {
        var err = ValidateFieldMapping(req);
        if (err is not null) return BadRequest(err);

        var dup = (await _db.GetRecordsAsync<int>(@"
            SELECT 1 FROM ExternalIntegrationFieldMappings
            WHERE  SourceSystem = @S AND SourceFieldName = @Src AND TargetFieldName = @Tgt
            LIMIT 1",
            new
            {
                S   = req.SourceSystem,
                Src = req.SourceFieldName.Trim(),
                Tgt = req.TargetFieldName.Trim(),
            }))?.Any() ?? false;
        if (dup) return Conflict("מיפוי זהה כבר קיים");

        int id = await _db.InsertReturnIdAsync(@"
            INSERT INTO ExternalIntegrationFieldMappings
                (SourceSystem, SourceFieldName, TargetFieldName,
                 IsRequired, DefaultValue, IsActive, Notes)
            VALUES
                (@SourceSystem, @SourceFieldName, @TargetFieldName,
                 @IsRequiredInt, @DefaultValue, @IsActiveInt, @Notes)",
            new
            {
                req.SourceSystem,
                SourceFieldName = req.SourceFieldName.Trim(),
                TargetFieldName = req.TargetFieldName.Trim(),
                IsRequiredInt   = req.IsRequired ? 1 : 0,
                DefaultValue    = (req.DefaultValue ?? "").Trim(),
                IsActiveInt     = req.IsActive ? 1 : 0,
                Notes           = (req.Notes ?? "").Trim(),
            });

        if (id == 0) return StatusCode(500, "שגיאה בשמירת המיפוי");
        return Ok(new { id });
    }

    [HttpPut("field-mappings/{id:int}")]
    public async Task<IActionResult> UpdateFieldMapping(int id, int authUserId,
        [FromBody] ExternalIntegrationFieldMappingSaveRequest req)
    {
        var err = ValidateFieldMapping(req);
        if (err is not null) return BadRequest(err);

        var exists = (await _db.GetRecordsAsync<int>(
            "SELECT 1 FROM ExternalIntegrationFieldMappings WHERE Id = @Id",
            new { Id = id }))?.Any() ?? false;
        if (!exists) return NotFound("המיפוי לא נמצא");

        int affected = await _db.SaveDataAsync(@"
            UPDATE ExternalIntegrationFieldMappings
            SET    SourceSystem    = @SourceSystem,
                   SourceFieldName = @SourceFieldName,
                   TargetFieldName = @TargetFieldName,
                   IsRequired      = @IsRequiredInt,
                   DefaultValue    = @DefaultValue,
                   IsActive        = @IsActiveInt,
                   Notes           = @Notes,
                   UpdatedAt       = datetime('now')
            WHERE  Id = @Id",
            new
            {
                Id = id,
                req.SourceSystem,
                SourceFieldName = req.SourceFieldName.Trim(),
                TargetFieldName = req.TargetFieldName.Trim(),
                IsRequiredInt   = req.IsRequired ? 1 : 0,
                DefaultValue    = (req.DefaultValue ?? "").Trim(),
                IsActiveInt     = req.IsActive ? 1 : 0,
                Notes           = (req.Notes ?? "").Trim(),
            });

        if (affected == 0) return StatusCode(500, "שגיאה בעדכון המיפוי");
        return Ok();
    }

    [HttpDelete("field-mappings/{id:int}")]
    public async Task<IActionResult> DeleteFieldMapping(int id, int authUserId)
    {
        int affected = await _db.SaveDataAsync(
            "DELETE FROM ExternalIntegrationFieldMappings WHERE Id = @Id",
            new { Id = id });
        if (affected == 0) return NotFound("המיפוי לא נמצא");
        return Ok();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Status mappings
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet("status-mappings")]
    public async Task<IActionResult> ListStatusMappings(int authUserId,
        [FromQuery] string sourceSystem = ExternalIntegrationSourceSystems.Airtable)
    {
        var rows = (await _db.GetRecordsAsync<ExternalIntegrationStatusMappingDto>(@"
            SELECT  Id, SourceSystem, SourceStatusValue, InternalStatus, DisplayLabel,
                    IsTerminal, IsActive, CreatedAt, UpdatedAt
            FROM    ExternalIntegrationStatusMappings
            WHERE   SourceSystem = @S
            ORDER   BY IsActive DESC, InternalStatus, SourceStatusValue",
            new { S = sourceSystem }))?.ToList()
            ?? new List<ExternalIntegrationStatusMappingDto>();
        return Ok(rows);
    }

    [HttpPost("status-mappings")]
    public async Task<IActionResult> CreateStatusMapping(int authUserId,
        [FromBody] ExternalIntegrationStatusMappingSaveRequest req)
    {
        var err = ValidateStatusMapping(req);
        if (err is not null) return BadRequest(err);

        var dup = (await _db.GetRecordsAsync<int>(@"
            SELECT 1 FROM ExternalIntegrationStatusMappings
            WHERE  SourceSystem = @S AND SourceStatusValue = @V LIMIT 1",
            new { S = req.SourceSystem, V = req.SourceStatusValue.Trim() }))?.Any() ?? false;
        if (dup) return Conflict("כבר קיים מיפוי לערך מקור זה");

        int id = await _db.InsertReturnIdAsync(@"
            INSERT INTO ExternalIntegrationStatusMappings
                (SourceSystem, SourceStatusValue, InternalStatus, DisplayLabel,
                 IsTerminal, IsActive)
            VALUES
                (@SourceSystem, @SourceStatusValue, @InternalStatus, @DisplayLabel,
                 @IsTerminalInt, @IsActiveInt)",
            new
            {
                req.SourceSystem,
                SourceStatusValue = req.SourceStatusValue.Trim(),
                InternalStatus    = req.InternalStatus.Trim(),
                DisplayLabel      = (req.DisplayLabel ?? "").Trim(),
                IsTerminalInt     = req.IsTerminal ? 1 : 0,
                IsActiveInt       = req.IsActive ? 1 : 0,
            });

        if (id == 0) return StatusCode(500, "שגיאה בשמירת המיפוי");
        return Ok(new { id });
    }

    [HttpPut("status-mappings/{id:int}")]
    public async Task<IActionResult> UpdateStatusMapping(int id, int authUserId,
        [FromBody] ExternalIntegrationStatusMappingSaveRequest req)
    {
        var err = ValidateStatusMapping(req);
        if (err is not null) return BadRequest(err);

        var exists = (await _db.GetRecordsAsync<int>(
            "SELECT 1 FROM ExternalIntegrationStatusMappings WHERE Id = @Id",
            new { Id = id }))?.Any() ?? false;
        if (!exists) return NotFound("המיפוי לא נמצא");

        int affected = await _db.SaveDataAsync(@"
            UPDATE ExternalIntegrationStatusMappings
            SET    SourceSystem      = @SourceSystem,
                   SourceStatusValue = @SourceStatusValue,
                   InternalStatus    = @InternalStatus,
                   DisplayLabel      = @DisplayLabel,
                   IsTerminal        = @IsTerminalInt,
                   IsActive          = @IsActiveInt,
                   UpdatedAt         = datetime('now')
            WHERE  Id = @Id",
            new
            {
                Id = id,
                req.SourceSystem,
                SourceStatusValue = req.SourceStatusValue.Trim(),
                InternalStatus    = req.InternalStatus.Trim(),
                DisplayLabel      = (req.DisplayLabel ?? "").Trim(),
                IsTerminalInt     = req.IsTerminal ? 1 : 0,
                IsActiveInt       = req.IsActive ? 1 : 0,
            });

        if (affected == 0) return StatusCode(500, "שגיאה בעדכון המיפוי");
        return Ok();
    }

    [HttpDelete("status-mappings/{id:int}")]
    public async Task<IActionResult> DeleteStatusMapping(int id, int authUserId)
    {
        int affected = await _db.SaveDataAsync(
            "DELETE FROM ExternalIntegrationStatusMappings WHERE Id = @Id",
            new { Id = id });
        if (affected == 0) return NotFound("המיפוי לא נמצא");
        return Ok();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Test endpoint — dry-run only. Applies the mappings to an admin-supplied
    //  JSON sample and returns what the webhook *would* persist. Does not
    //  touch the ExternalRequests table.
    // ═══════════════════════════════════════════════════════════════════════

    [HttpPost("test-payload")]
    public async Task<IActionResult> TestPayload(int authUserId,
        [FromBody] ExternalIntegrationTestRequest req,
        [FromQuery] string sourceSystem = ExternalIntegrationSourceSystems.Airtable)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Payload))
            return Ok(new ExternalIntegrationTestResponse
            {
                Success = false,
                Error   = "אנא הדבק/י JSON לבדיקה",
            });

        // Hard cap the test payload at 64 KB — same as the real webhook.
        if (req.Payload.Length > 64 * 1024)
            return Ok(new ExternalIntegrationTestResponse
            {
                Success = false,
                Error   = "המטען גדול מ-64KB",
            });

        var fieldMappings  = (await _db.GetRecordsAsync<ExternalIntegrationFieldMappingDto>(
            "SELECT * FROM ExternalIntegrationFieldMappings WHERE SourceSystem = @S",
            new { S = sourceSystem }))?.ToList() ?? new();

        var statusMappings = (await _db.GetRecordsAsync<ExternalIntegrationStatusMappingDto>(
            "SELECT * FROM ExternalIntegrationStatusMappings WHERE SourceSystem = @S",
            new { S = sourceSystem }))?.ToList() ?? new();

        var result = ExternalIntegrationMapper.Apply(req.Payload, fieldMappings, statusMappings);

        // Try to parse the transformed JSON into the canonical DTO so the
        // admin gets a clean "ExternalRequestId missing" warning early.
        ExternalRequestUpdateRequest? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<ExternalRequestUpdateRequest>(
                result.TransformedJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            return Ok(new ExternalIntegrationTestResponse
            {
                Success         = false,
                Error           = "ה-JSON לאחר המיפוי לא תקין: " + ex.Message,
                TransformedJson = result.TransformedJson,
                Warnings        = result.Warnings,
            });
        }

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.ExternalRequestId))
        {
            return Ok(new ExternalIntegrationTestResponse
            {
                Success         = false,
                Error           = "חסר ערך עבור externalRequestId לאחר המיפוי",
                TransformedJson = result.TransformedJson,
                Warnings        = result.Warnings,
            });
        }

        return Ok(new ExternalIntegrationTestResponse
        {
            Success         = true,
            TransformedJson = result.TransformedJson,
            Warnings        = result.Warnings,
            Action          = req.DryRun ? "preview" : "preview-only", // never persists from this endpoint
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Validation
    // ─────────────────────────────────────────────────────────────────────────

    private static string? ValidateFieldMapping(ExternalIntegrationFieldMappingSaveRequest? r)
    {
        if (r is null) return "גוף בקשה ריק";
        if (string.IsNullOrWhiteSpace(r.SourceSystem))    return "מערכת מקור חובה";
        if (string.IsNullOrWhiteSpace(r.SourceFieldName)) return "שם השדה במקור חובה";
        if (string.IsNullOrWhiteSpace(r.TargetFieldName)) return "שם שדה היעד חובה";
        if (r.SourceFieldName.Trim().Length > 200)        return "שם שדה המקור ארוך מדי";
        if ((r.DefaultValue ?? "").Length > 1000)         return "ערך ברירת מחדל ארוך מדי";
        if ((r.Notes        ?? "").Length > 1000)         return "ההערות ארוכות מדי";

        var tgt = r.TargetFieldName.Trim();
        if (!ExternalIntegrationTargetFields.All.Contains(tgt))
            return "שדה יעד אינו חוקי";

        return null;
    }

    private static string? ValidateStatusMapping(ExternalIntegrationStatusMappingSaveRequest? r)
    {
        if (r is null) return "גוף בקשה ריק";
        if (string.IsNullOrWhiteSpace(r.SourceSystem))      return "מערכת מקור חובה";
        if (string.IsNullOrWhiteSpace(r.SourceStatusValue)) return "ערך הסטטוס במקור חובה";
        if (string.IsNullOrWhiteSpace(r.InternalStatus))    return "סטטוס פנימי חובה";
        if (r.SourceStatusValue.Trim().Length > 200)        return "ערך הסטטוס ארוך מדי";
        if ((r.DisplayLabel ?? "").Length > 200)            return "התווית ארוכה מדי";
        return null;
    }

    private sealed class CountsRow
    {
        public int FieldCount  { get; set; }
        public int StatusCount { get; set; }
    }
}