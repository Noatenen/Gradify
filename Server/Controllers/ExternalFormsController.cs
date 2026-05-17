using System.Text.Json;
using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  ExternalFormsController — /api/external-forms
//
//  Manages the *metadata* for Innovation-Team forms embedded in Gradify via
//  iframe. The form bodies themselves are hosted externally; we only store
//  URL + a few labels so admins/lecturers can swap links per cycle.
//
//  Mutations:
//   • Write an ExternalFormAuditLog row (before-and-after JSON snapshots).
//   • Capture authUserId on the *ByUserId columns of the form row itself.
//   • DELETE is soft — IsDeleted = 1, the row stays for audit. Every read
//     query filters with WHERE IsDeleted = 0.
//
//  URL validation is intentionally strict (HTTPS only, no javascript:/data:
//  schemes, no embedded credentials) — see ValidateIframeUrl below.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/external-forms")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
public class ExternalFormsController : ControllerBase
{
    private readonly DbRepository _db;

    public ExternalFormsController(DbRepository db) => _db = db;

    // ── GET /api/external-forms ────────────────────────────────────────────
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Staff}")]
    public async Task<IActionResult> List(int authUserId)
    {
        const string sql = @"
            SELECT  f.Id,
                    f.Name,
                    f.Description,
                    f.FormType,
                    f.IframeUrl,
                    f.IsActive,
                    f.AcademicYearId,
                    COALESCE(ay.Name, '') AS AcademicYearName,
                    f.CreatedAt,
                    f.UpdatedAt
            FROM    ExternalForms f
            LEFT JOIN AcademicYears ay ON ay.Id = f.AcademicYearId
            WHERE   f.IsDeleted = 0
            ORDER   BY f.IsActive DESC, ay.IsCurrent DESC, f.UpdatedAt DESC";

        var rows = (await _db.GetRecordsAsync<ExternalFormDto>(sql))?.ToList()
                   ?? new List<ExternalFormDto>();
        return Ok(rows);
    }

    // ── GET /api/external-forms/active ─────────────────────────────────────
    [HttpGet("active")]
    public async Task<IActionResult> ListActive(int authUserId)
    {
        var userYearId = (await _db.GetRecordsAsync<int?>(
            "SELECT AcademicYearId FROM users WHERE Id = @Id",
            new { Id = authUserId }))?.FirstOrDefault();

        string sql;
        object parameters;
        if (userYearId is int yid && yid > 0)
        {
            sql = @"
                SELECT  f.Id, f.Name, f.Description, f.FormType, f.IframeUrl,
                        f.IsActive, f.AcademicYearId,
                        COALESCE(ay.Name, '') AS AcademicYearName,
                        f.CreatedAt, f.UpdatedAt
                FROM    ExternalForms f
                LEFT JOIN AcademicYears ay ON ay.Id = f.AcademicYearId
                WHERE   f.IsActive  = 1
                  AND   f.IsDeleted = 0
                  AND   (f.AcademicYearId IS NULL OR f.AcademicYearId = @Yid)
                ORDER   BY f.UpdatedAt DESC";
            parameters = new { Yid = yid };
        }
        else
        {
            sql = @"
                SELECT  f.Id, f.Name, f.Description, f.FormType, f.IframeUrl,
                        f.IsActive, f.AcademicYearId,
                        COALESCE(ay.Name, '') AS AcademicYearName,
                        f.CreatedAt, f.UpdatedAt
                FROM    ExternalForms f
                LEFT JOIN AcademicYears ay ON ay.Id = f.AcademicYearId
                WHERE   f.IsActive = 1 AND f.IsDeleted = 0
                ORDER   BY f.UpdatedAt DESC";
            parameters = new { };
        }

        var rows = (await _db.GetRecordsAsync<ExternalFormDto>(sql, parameters))?.ToList()
                   ?? new List<ExternalFormDto>();
        return Ok(rows);
    }

    // ── POST /api/external-forms ───────────────────────────────────────────
    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Staff}")]
    public async Task<IActionResult> Create(int authUserId, [FromBody] ExternalFormSaveRequest req)
    {
        var err = Validate(req);
        if (err is not null) return BadRequest(err);

        if (req.AcademicYearId is int yid && yid > 0)
        {
            var exists = (await _db.GetRecordsAsync<int>(
                "SELECT 1 FROM AcademicYears WHERE Id = @Id", new { Id = yid }))?.Any() ?? false;
            if (!exists) return BadRequest("המחזור האקדמי לא נמצא");
        }

        int newId = await _db.InsertReturnIdAsync(@"
            INSERT INTO ExternalForms
                (Name, Description, FormType, IframeUrl, IsActive, AcademicYearId,
                 CreatedByUserId, UpdatedByUserId)
            VALUES
                (@Name, @Description, @FormType, @IframeUrl, @IsActiveInt, @AcademicYearId,
                 @UserId, @UserId)",
            new
            {
                Name           = req.Name.Trim(),
                Description    = (req.Description ?? "").Trim(),
                FormType       = (req.FormType    ?? "").Trim(),
                IframeUrl      = req.IframeUrl.Trim(),
                IsActiveInt    = req.IsActive ? 1 : 0,
                req.AcademicYearId,
                UserId         = authUserId,
            });

        if (newId == 0) return StatusCode(500, "שגיאה ביצירת הטופס");

        await WriteAuditAsync(newId, ExternalFormAuditActions.Created, authUserId,
            oldValues: null, newValues: req);

        return Ok(new { id = newId });
    }

    // ── PUT /api/external-forms/{id} ───────────────────────────────────────
    [HttpPut("{id:int}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Staff}")]
    public async Task<IActionResult> Update(int id, int authUserId, [FromBody] ExternalFormSaveRequest req)
    {
        var err = Validate(req);
        if (err is not null) return BadRequest(err);

        var existing = await LoadRowAsync(id);
        if (existing is null) return NotFound("הטופס לא נמצא");

        if (req.AcademicYearId is int yid && yid > 0)
        {
            var yearOk = (await _db.GetRecordsAsync<int>(
                "SELECT 1 FROM AcademicYears WHERE Id = @Id", new { Id = yid }))?.Any() ?? false;
            if (!yearOk) return BadRequest("המחזור האקדמי לא נמצא");
        }

        int affected = await _db.SaveDataAsync(@"
            UPDATE ExternalForms
            SET    Name            = @Name,
                   Description     = @Description,
                   FormType        = @FormType,
                   IframeUrl       = @IframeUrl,
                   IsActive        = @IsActiveInt,
                   AcademicYearId  = @AcademicYearId,
                   UpdatedByUserId = @UserId,
                   UpdatedAt       = datetime('now')
            WHERE  Id = @Id AND IsDeleted = 0",
            new
            {
                Id             = id,
                Name           = req.Name.Trim(),
                Description    = (req.Description ?? "").Trim(),
                FormType       = (req.FormType    ?? "").Trim(),
                IframeUrl      = req.IframeUrl.Trim(),
                IsActiveInt    = req.IsActive ? 1 : 0,
                req.AcademicYearId,
                UserId         = authUserId,
            });

        if (affected == 0) return StatusCode(500, "שגיאה בעדכון הטופס");

        await WriteAuditAsync(id, ExternalFormAuditActions.Updated, authUserId,
            oldValues: existing, newValues: req);

        return Ok();
    }

    // ── POST /api/external-forms/{id}/toggle ───────────────────────────────
    [HttpPost("{id:int}/toggle")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Staff}")]
    public async Task<IActionResult> Toggle(int id, int authUserId)
    {
        var existing = await LoadRowAsync(id);
        if (existing is null) return NotFound("הטופס לא נמצא");

        int newVal = existing.IsActive ? 0 : 1;
        await _db.SaveDataAsync(@"
            UPDATE ExternalForms
            SET    IsActive        = @V,
                   UpdatedByUserId = @UserId,
                   UpdatedAt       = datetime('now')
            WHERE  Id = @Id AND IsDeleted = 0",
            new { Id = id, V = newVal, UserId = authUserId });

        await WriteAuditAsync(id, ExternalFormAuditActions.Toggled, authUserId,
            oldValues: new { existing.IsActive },
            newValues: new { IsActive = newVal == 1 });

        return Ok(new { id, isActive = newVal == 1 });
    }

    // ── DELETE /api/external-forms/{id} ────────────────────────────────────
    // Soft delete — the row stays for audit purposes and is filtered out of
    // every read query. A future endpoint can restore the row.
    [HttpDelete("{id:int}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Staff}")]
    public async Task<IActionResult> Delete(int id, int authUserId)
    {
        var existing = await LoadRowAsync(id);
        if (existing is null) return NotFound("הטופס לא נמצא");

        await _db.SaveDataAsync(@"
            UPDATE ExternalForms
            SET    IsDeleted       = 1,
                   IsActive        = 0,
                   DeletedByUserId = @UserId,
                   DeletedAt       = datetime('now'),
                   UpdatedAt       = datetime('now')
            WHERE  Id = @Id",
            new { Id = id, UserId = authUserId });

        await WriteAuditAsync(id, ExternalFormAuditActions.SoftDeleted, authUserId,
            oldValues: existing, newValues: null);

        return Ok();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Loads the FULL row (including IframeUrl and lifecycle flags)
    /// for audit snapshots. Returns null when the row is missing or soft-deleted.</summary>
    private async Task<FormRow?> LoadRowAsync(int id)
    {
        var rows = await _db.GetRecordsAsync<FormRow>(
            @"SELECT Id, Name, Description, FormType, IframeUrl, IsActive,
                     AcademicYearId, CreatedAt, UpdatedAt
              FROM   ExternalForms
              WHERE  Id = @Id AND IsDeleted = 0",
            new { Id = id });
        return rows?.FirstOrDefault();
    }

    private async Task WriteAuditAsync(
        int formId,
        string action,
        int authUserId,
        object? oldValues,
        object? newValues)
    {
        await _db.SaveDataAsync(@"
            INSERT INTO ExternalFormAuditLog
                (ExternalFormId, Action, ChangedByUserId, OldValuesJson, NewValuesJson)
            VALUES
                (@FormId, @Action, @UserId, @OldJson, @NewJson)",
            new
            {
                FormId  = formId,
                Action  = action,
                UserId  = authUserId,
                OldJson = oldValues is null ? "" : JsonSerializer.Serialize(oldValues),
                NewJson = newValues is null ? "" : JsonSerializer.Serialize(newValues),
            });
    }

    // ── Validation ─────────────────────────────────────────────────────────

    private static string? Validate(ExternalFormSaveRequest? req)
    {
        if (req is null) return "גוף בקשה ריק";
        if (string.IsNullOrWhiteSpace(req.Name))      return "שם הטופס הוא שדה חובה";
        if (req.Name.Trim().Length > 200)             return "שם הטופס ארוך מדי (מקס׳ 200 תווים)";
        if ((req.Description ?? "").Length > 1000)    return "התיאור ארוך מדי (מקס׳ 1000 תווים)";
        if ((req.FormType    ?? "").Length > 100)     return "סוג הטופס ארוך מדי (מקס׳ 100 תווים)";
        return ValidateIframeUrl(req.IframeUrl);
    }

    /// <summary>
    /// Strict iframe URL policy:
    ///   • Must parse as an absolute URI.
    ///   • Scheme must be HTTPS exactly (no http, javascript, data, file, etc.).
    ///   • No embedded userinfo (user:pass@host).
    ///   • Host must be non-empty.
    ///   • Total length capped at 2048 chars (well within RFC and browser limits).
    /// </summary>
    public static string? ValidateIframeUrl(string? raw)
    {
        // "IframeUrl" is the legacy column name we keep for compatibility;
        // the user-facing label is "קישור לטופס חיצוני".
        if (string.IsNullOrWhiteSpace(raw)) return "קישור לטופס חיצוני הוא שדה חובה";

        var trimmed = raw.Trim();
        if (trimmed.Length > 2048) return "הקישור ארוך מדי";

        // Reject anything that *looks* like an unsafe scheme before parsing,
        // since Uri.TryCreate will happily accept "javascript:alert(1)".
        var lower = trimmed.ToLowerInvariant();
        if (lower.StartsWith("javascript:") ||
            lower.StartsWith("data:")       ||
            lower.StartsWith("vbscript:")   ||
            lower.StartsWith("file:")       ||
            lower.StartsWith("blob:")       ||
            lower.StartsWith("about:"))
        {
            return "הקישור חייב להיות מסוג HTTPS בלבד";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return "הקישור לא תקין";

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return "הקישור חייב להיות מסוג HTTPS בלבד";

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return "הקישור לא יכול לכלול שם משתמש/סיסמה";

        if (string.IsNullOrWhiteSpace(uri.Host))
            return "הקישור לא תקין";

        return null;
    }

    // ── Private row types ──────────────────────────────────────────────────
    private sealed class FormRow
    {
        public int      Id             { get; set; }
        public string   Name           { get; set; } = "";
        public string   Description    { get; set; } = "";
        public string   FormType       { get; set; } = "";
        public string   IframeUrl      { get; set; } = "";
        public bool     IsActive       { get; set; }
        public int?     AcademicYearId { get; set; }
        public DateTime? CreatedAt     { get; set; }
        public DateTime? UpdatedAt     { get; set; }
    }
}