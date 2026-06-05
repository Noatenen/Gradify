using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  RequestTypesController — /api/request-types
//
//  Admin-managed catalogue of student request types. Backs the
//  /management/request-types screen and the student NewRequestModal picker.
//
//  Authorization:
//    - GET    /api/request-types          → Admin
//    - GET    /api/request-types/active   → any authenticated user (picker)
//    - GET    /api/request-types/{id}     → Admin
//    - POST   /api/request-types          → Admin
//    - PUT    /api/request-types/{id}     → Admin
//    - PATCH  /api/request-types/{id}/active → Admin
//    - DELETE /api/request-types/{id}     → Admin (rejects built-in and in-use)
//
//  The Code column is the stable string stored in ProjectRequests.RequestType.
//  Built-in seed rows have IsBuiltIn = 1 and cannot be hard-deleted; the
//  controller surfaces a Hebrew error suggesting deactivation instead.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/request-types")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize]
public class RequestTypesController : ControllerBase
{
    private readonly DbRepository _db;

    public RequestTypesController(DbRepository db) => _db = db;

    // ── GET /api/request-types ─────────────────────────────────────────────
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetAll()
    {
        const string sql = @"
            SELECT  rtd.Id, rtd.Code, rtd.Name, rtd.Description, rtd.IsActive,
                    rtd.HandlerRole, rtd.AllowInternalNotes, rtd.FileUploadPolicy,
                    rtd.IsBuiltIn, rtd.CreatedAt, rtd.UpdatedAt,
                    (SELECT COUNT(1) FROM ProjectRequests pr WHERE pr.RequestType = rtd.Code) AS UsageCount
            FROM    RequestTypeDefinitions rtd
            ORDER   BY rtd.IsBuiltIn DESC, rtd.Name COLLATE NOCASE";

        var rows = (await _db.GetRecordsAsync<RequestTypeDefinitionDto>(sql))?.ToList()
                   ?? new List<RequestTypeDefinitionDto>();

        if (rows.Count > 0)
            await AttachFieldsAsync(rows);

        return Ok(rows);
    }

    // ── GET /api/request-types/active ──────────────────────────────────────
    // Used by the student NewRequestModal so disabled types disappear from
    // the picker. Any authenticated user can call it; nothing sensitive is
    // returned.
    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        const string sql = @"
            SELECT  Id, Code, Name, Description, IsActive,
                    HandlerRole, AllowInternalNotes, FileUploadPolicy,
                    IsBuiltIn, CreatedAt, UpdatedAt
            FROM    RequestTypeDefinitions
            WHERE   IsActive = 1
            ORDER   BY IsBuiltIn DESC, Name COLLATE NOCASE";

        var rows = (await _db.GetRecordsAsync<RequestTypeDefinitionDto>(sql))?.ToList()
                   ?? new List<RequestTypeDefinitionDto>();

        if (rows.Count > 0)
            await AttachFieldsAsync(rows);

        return Ok(rows);
    }

    // ── GET /api/request-types/{id} ────────────────────────────────────────
    [HttpGet("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetById(int id)
    {
        var row = await LoadOneAsync(id);
        if (row is null) return NotFound("סוג הבקשה לא נמצא");
        return Ok(row);
    }

    // ── POST /api/request-types ────────────────────────────────────────────
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create([FromBody] SaveRequestTypeRequest req)
    {
        var validation = ValidateRequest(req, isCreate: true);
        if (validation is not null) return BadRequest(validation);

        string code = req.Code.Trim();

        bool codeExists = (await _db.GetRecordsAsync<int>(
            "SELECT 1 FROM RequestTypeDefinitions WHERE Code = @Code COLLATE NOCASE LIMIT 1",
            new { Code = code }))?.Any() ?? false;
        if (codeExists)
            return Conflict("קוד סוג הבקשה כבר קיים — יש לבחור קוד אחר");

        int newId = await _db.InsertReturnIdAsync(@"
            INSERT INTO RequestTypeDefinitions
                (Code, Name, Description, IsActive, HandlerRole,
                 AllowInternalNotes, FileUploadPolicy, IsBuiltIn, CreatedAt, UpdatedAt)
            VALUES
                (@Code, @Name, @Description, @IsActive, @HandlerRole,
                 @AllowInternalNotes, @FileUploadPolicy, 0,
                 datetime('now'), datetime('now'))",
            new
            {
                Code               = code,
                Name               = req.Name.Trim(),
                Description        = (req.Description ?? "").Trim(),
                IsActive           = req.IsActive ? 1 : 0,
                HandlerRole        = NormaliseHandlerRole(req.HandlerRole),
                AllowInternalNotes = req.AllowInternalNotes ? 1 : 0,
                FileUploadPolicy   = req.FileUploadPolicy,
            });

        if (newId == 0) return StatusCode(500, "שגיאה ביצירת סוג הבקשה");

        await ReplaceFieldsAsync(newId, req.Fields);

        var created = await LoadOneAsync(newId);
        return Ok(created);
    }

    // ── PUT /api/request-types/{id} ────────────────────────────────────────
    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(int id, [FromBody] SaveRequestTypeRequest req)
    {
        var existing = await LoadOneAsync(id);
        if (existing is null) return NotFound("סוג הבקשה לא נמצא");

        var validation = ValidateRequest(req, isCreate: false);
        if (validation is not null) return BadRequest(validation);

        int affected = await _db.SaveDataAsync(@"
            UPDATE RequestTypeDefinitions
            SET    Name               = @Name,
                   Description        = @Description,
                   IsActive           = @IsActive,
                   HandlerRole        = @HandlerRole,
                   AllowInternalNotes = @AllowInternalNotes,
                   FileUploadPolicy   = @FileUploadPolicy,
                   UpdatedAt          = datetime('now')
            WHERE  Id = @Id",
            new
            {
                Id                 = id,
                Name               = req.Name.Trim(),
                Description        = (req.Description ?? "").Trim(),
                IsActive           = req.IsActive ? 1 : 0,
                HandlerRole        = NormaliseHandlerRole(req.HandlerRole),
                AllowInternalNotes = req.AllowInternalNotes ? 1 : 0,
                FileUploadPolicy   = req.FileUploadPolicy,
            });

        if (affected == 0) return NotFound("סוג הבקשה לא נמצא");

        await ReplaceFieldsAsync(id, req.Fields);

        var updated = await LoadOneAsync(id);
        return Ok(updated);
    }

    // ── PATCH /api/request-types/{id}/active ───────────────────────────────
    [HttpPatch("{id:int}/active")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> ToggleActive(int id, [FromBody] ToggleRequestTypeActiveRequest req)
    {
        int affected = await _db.SaveDataAsync(@"
            UPDATE RequestTypeDefinitions
            SET    IsActive  = @IsActive,
                   UpdatedAt = datetime('now')
            WHERE  Id = @Id",
            new { Id = id, IsActive = req.IsActive ? 1 : 0 });

        if (affected == 0) return NotFound("סוג הבקשה לא נמצא");
        return Ok();
    }

    // ── DELETE /api/request-types/{id} ─────────────────────────────────────
    // Hard delete only when the type has no existing ProjectRequests and is
    // not built-in. Otherwise return 409 with a Hebrew hint to deactivate.
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var row = (await _db.GetRecordsAsync<DeleteCheckRow>(@"
            SELECT  rtd.Id, rtd.Code, rtd.IsBuiltIn,
                    (SELECT COUNT(1) FROM ProjectRequests pr WHERE pr.RequestType = rtd.Code) AS UsageCount
            FROM    RequestTypeDefinitions rtd
            WHERE   rtd.Id = @Id LIMIT 1",
            new { Id = id }))?.FirstOrDefault();

        if (row is null) return NotFound("סוג הבקשה לא נמצא");

        if (row.IsBuiltIn == 1)
            return Conflict("סוג בקשה מובנה לא ניתן למחיקה — ניתן רק להשבית אותו");

        if (row.UsageCount > 0)
            return Conflict($"לסוג בקשה זה קיימות {row.UsageCount} בקשות במערכת — לא ניתן למחוק. מומלץ להשבית במקום");

        // CASCADE on RequestTypeFields removes child rows automatically.
        await _db.SaveDataAsync(
            "DELETE FROM RequestTypeDefinitions WHERE Id = @Id",
            new { Id = id });

        return Ok();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<RequestTypeDefinitionDto?> LoadOneAsync(int id)
    {
        const string sql = @"
            SELECT  rtd.Id, rtd.Code, rtd.Name, rtd.Description, rtd.IsActive,
                    rtd.HandlerRole, rtd.AllowInternalNotes, rtd.FileUploadPolicy,
                    rtd.IsBuiltIn, rtd.CreatedAt, rtd.UpdatedAt,
                    (SELECT COUNT(1) FROM ProjectRequests pr WHERE pr.RequestType = rtd.Code) AS UsageCount
            FROM    RequestTypeDefinitions rtd
            WHERE   rtd.Id = @Id LIMIT 1";

        var row = (await _db.GetRecordsAsync<RequestTypeDefinitionDto>(sql, new { Id = id }))?.FirstOrDefault();
        if (row is null) return null;
        await AttachFieldsAsync(new List<RequestTypeDefinitionDto> { row });
        return row;
    }

    private async Task AttachFieldsAsync(List<RequestTypeDefinitionDto> rows)
    {
        var ids = string.Join(",", rows.Select(r => r.Id));
        if (string.IsNullOrEmpty(ids)) return;

        // Inline list of ints is safe — we just controlled the values.
        var fieldsSql = $@"
            SELECT  Id, RequestTypeId, Label, FieldKey, FieldType,
                    IsRequired, Options, SortOrder
            FROM    RequestTypeFields
            WHERE   RequestTypeId IN ({ids})
            ORDER   BY SortOrder, Id";

        var fields = (await _db.GetRecordsAsync<RequestTypeFieldDto>(fieldsSql))?.ToList()
                     ?? new List<RequestTypeFieldDto>();

        var byTypeId = fields.GroupBy(f => f.RequestTypeId)
                             .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var r in rows)
            r.Fields = byTypeId.TryGetValue(r.Id, out var list) ? list : new();
    }

    // Wholesale replace: delete existing field rows and insert the new set.
    // Simpler and safer than diffing client/server lists; the table is small
    // (max ~10 fields per type in practice).
    private async Task ReplaceFieldsAsync(int requestTypeId, List<RequestTypeFieldDto> fields)
    {
        await _db.SaveDataAsync(
            "DELETE FROM RequestTypeFields WHERE RequestTypeId = @Id",
            new { Id = requestTypeId });

        if (fields is null || fields.Count == 0) return;

        const string insertSql = @"
            INSERT INTO RequestTypeFields
                (RequestTypeId, Label, FieldKey, FieldType, IsRequired, Options, SortOrder)
            VALUES
                (@RequestTypeId, @Label, @FieldKey, @FieldType, @IsRequired, @Options, @SortOrder)";

        int order = 0;
        foreach (var f in fields)
        {
            if (string.IsNullOrWhiteSpace(f.Label)) continue;
            if (!RequestTypeFieldTypes.All.Contains(f.FieldType)) continue;

            await _db.SaveDataAsync(insertSql, new
            {
                RequestTypeId = requestTypeId,
                Label         = f.Label.Trim(),
                FieldKey      = (f.FieldKey ?? "").Trim(),
                FieldType     = f.FieldType,
                IsRequired    = f.IsRequired ? 1 : 0,
                Options       = (f.Options ?? "").Trim(),
                SortOrder     = order++,
            });
        }
    }

    private static string? ValidateRequest(SaveRequestTypeRequest req, bool isCreate)
    {
        if (req is null) return "גוף בקשה ריק";

        if (isCreate)
        {
            string code = (req.Code ?? "").Trim();
            if (string.IsNullOrEmpty(code))
                return "קוד סוג הבקשה הוא שדה חובה";
            if (code.Length > 64)
                return "קוד סוג הבקשה ארוך מדי (עד 64 תווים)";
            // Keep codes safe to use as identifiers — alphanumerics only.
            foreach (var ch in code)
                if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'))
                    return "קוד סוג הבקשה יכול להכיל אותיות באנגלית, ספרות, מקף ותחתון בלבד";
        }

        if (string.IsNullOrWhiteSpace(req.Name))
            return "שם סוג הבקשה הוא שדה חובה";

        if (req.Name.Length > 80)
            return "שם סוג הבקשה ארוך מדי (עד 80 תווים)";

        if (!RequestTypeFileUploadPolicies.All.Contains(req.FileUploadPolicy ?? ""))
            return "ערך לא תקין עבור מדיניות העלאת קבצים";

        var handlerRoles = SplitHandlerRoles(req.HandlerRole);
        if (handlerRoles.Count == 0)
            return "יש לבחור לפחות תפקיד מטפל אחד";
        foreach (var r in handlerRoles)
            if (!RequestTypeHandlerRoles.All.Contains(r))
                return $"תפקיד מטפל לא חוקי: {r}";

        if (req.Fields is { Count: > 20 })
            return "ניתן להגדיר עד 20 שדות לסוג בקשה";

        return null;
    }

    private static string NormaliseHandlerRole(string? raw)
    {
        var list = SplitHandlerRoles(raw)
            .Where(RequestTypeHandlerRoles.All.Contains)
            .Distinct()
            .ToList();
        return string.Join(",", list);
    }

    private static List<string> SplitHandlerRoles(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .ToList();
    }

    private sealed class DeleteCheckRow
    {
        public int    Id         { get; set; }
        public string Code       { get; set; } = "";
        public int    IsBuiltIn  { get; set; }
        public int    UsageCount { get; set; }
    }
}