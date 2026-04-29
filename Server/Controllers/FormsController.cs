using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  FormsController — /api/forms
//
//  Reusable form-builder system. Supports multiple form types; the current
//  implementation drives the AssignmentForm (טופס שיבוץ פרויקט).
//
//  The submission storage for the AssignmentForm continues to live in the
//  existing tables (AssignmentFormSubmissions, TeamProjectPreferences,
//  StudentStrengths). This controller manages STRUCTURE and SETTINGS only:
//  Forms, FormBlocks, FormBlockOptions.
//
//  System-managed blocks are anchored by FormBlocks.BlockKey
//  ('Strengths' | 'ProjectPreferences' | 'Notes'). Their type and options can
//  be edited (renamed, reordered, marked required) but they cannot be deleted
//  because the assignment-form rendering depends on their existence.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/forms")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin)]
public class FormsController : ControllerBase
{
    private readonly DbRepository _db;

    public FormsController(DbRepository db) => _db = db;

    // ── GET /api/forms ──────────────────────────────────────────────────────
    // Lists all forms. Auto-creates the AssignmentForm for the current
    // academic year on first access so the lecturer always has something to
    // edit.
    [HttpGet]
    public async Task<IActionResult> GetForms(int authUserId)
    {
        var currentYearId = await GetCurrentAcademicYearIdAsync();
        if (currentYearId > 0)
        {
            await FormsRepository.EnsureAssignmentFormAsync(_db, currentYearId);
        }

        const string sql = @"
            SELECT  f.Id,
                    f.AcademicYearId,
                    COALESCE(ay.Name, '') AS AcademicYear,
                    f.Name,
                    f.FormType,
                    f.Status,
                    f.IsOpen,
                    f.OpensAt,
                    f.ClosesAt,
                    f.UpdatedAt,
                    (SELECT COUNT(1)
                     FROM   AssignmentFormSubmissions s
                     JOIN   Teams t ON t.Id = s.TeamId
                     WHERE  f.FormType = 'AssignmentForm'
                       AND  t.AcademicYearId = f.AcademicYearId) AS SubmissionCount
            FROM    Forms f
            LEFT JOIN AcademicYears ay ON ay.Id = f.AcademicYearId
            ORDER   BY ay.IsCurrent DESC, f.UpdatedAt DESC";

        var rows = (await _db.GetRecordsAsync<FormListItemDto>(sql))?.ToList()
                   ?? new List<FormListItemDto>();
        return Ok(rows);
    }

    // ── GET /api/forms/{id} ─────────────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetForm(int id, int authUserId)
    {
        var detail = await LoadFormDetailAsync(id);
        if (detail is null) return NotFound("הטופס לא נמצא");
        return Ok(detail);
    }

    // ── POST /api/forms ─────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> CreateForm(int authUserId, [FromBody] SaveFormRequest req)
    {
        var err = ValidateForm(req);
        if (err is not null) return BadRequest(err);

        if (!await ExistsAsync("SELECT 1 FROM AcademicYears WHERE Id = @Id", new { Id = req.AcademicYearId }))
            return BadRequest("המחזור האקדמי לא נמצא");

        bool dup = await ExistsAsync(
            "SELECT 1 FROM Forms WHERE AcademicYearId = @YearId AND FormType = @Type",
            new { YearId = req.AcademicYearId, Type = req.FormType });
        if (dup) return Conflict("כבר קיים טופס מסוג זה למחזור הנבחר");

        int newId = await _db.InsertReturnIdAsync(@"
            INSERT INTO Forms
                (AcademicYearId, Name, FormType, Instructions, IsOpen, OpensAt, ClosesAt,
                 AllowEditAfterSubmit, Status)
            VALUES
                (@AcademicYearId, @Name, @FormType, @Instructions, @IsOpenInt, @OpensAt, @ClosesAt,
                 @AllowEditInt, @Status)",
            new
            {
                req.AcademicYearId,
                Name        = req.Name.Trim(),
                req.FormType,
                Instructions= req.Instructions ?? "",
                IsOpenInt   = req.IsOpen ? 1 : 0,
                req.OpensAt,
                req.ClosesAt,
                AllowEditInt= req.AllowEditAfterSubmit ? 1 : 0,
                Status      = NormalizeStatus(req.Status, req.IsOpen)
            });

        if (newId == 0) return StatusCode(500, "שגיאה ביצירת הטופס");

        // For an AssignmentForm, seed the canonical 3 blocks.
        if (string.Equals(req.FormType, "AssignmentForm", StringComparison.OrdinalIgnoreCase))
            await FormsRepository.SeedAssignmentBlocksAsync(_db, newId);

        return Ok(new { id = newId });
    }

    // ── PUT /api/forms/{id} ─────────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateForm(int id, int authUserId, [FromBody] SaveFormRequest req)
    {
        var err = ValidateForm(req);
        if (err is not null) return BadRequest(err);

        if (!await ExistsAsync("SELECT 1 FROM Forms WHERE Id = @Id", new { Id = id }))
            return NotFound("הטופס לא נמצא");

        int affected = await _db.SaveDataAsync(@"
            UPDATE Forms
            SET    Name                 = @Name,
                   Instructions         = @Instructions,
                   IsOpen               = @IsOpenInt,
                   OpensAt              = @OpensAt,
                   ClosesAt             = @ClosesAt,
                   AllowEditAfterSubmit = @AllowEditInt,
                   Status               = @Status,
                   UpdatedAt            = datetime('now')
            WHERE  Id = @Id",
            new
            {
                Id          = id,
                Name        = req.Name.Trim(),
                Instructions= req.Instructions ?? "",
                IsOpenInt   = req.IsOpen ? 1 : 0,
                req.OpensAt,
                req.ClosesAt,
                AllowEditInt= req.AllowEditAfterSubmit ? 1 : 0,
                Status      = NormalizeStatus(req.Status, req.IsOpen)
            });

        if (affected == 0) return StatusCode(500, "שגיאה בעדכון הטופס");
        return Ok();
    }

    // ── DELETE /api/forms/{id} ──────────────────────────────────────────────
    // Only allowed when the form has no submissions yet. Built-in
    // AssignmentForm rows can be deleted only if they're empty.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteForm(int id, int authUserId)
    {
        var info = (await _db.GetRecordsAsync<FormTypeRow>(
            "SELECT FormType, AcademicYearId FROM Forms WHERE Id = @Id",
            new { Id = id }))?.FirstOrDefault();

        if (info is null) return NotFound("הטופס לא נמצא");

        if (string.Equals(info.FormType, "AssignmentForm", StringComparison.OrdinalIgnoreCase))
        {
            int submissions = (await _db.GetRecordsAsync<int>(@"
                SELECT COUNT(1)
                FROM   AssignmentFormSubmissions s
                JOIN   Teams t ON t.Id = s.TeamId
                WHERE  t.AcademicYearId = @YearId",
                new { YearId = info.AcademicYearId })).FirstOrDefault();

            if (submissions > 0)
                return Conflict("לא ניתן למחוק טופס שיבוץ עם הגשות קיימות");
        }

        await _db.SaveDataAsync("DELETE FROM Forms WHERE Id = @Id", new { Id = id });
        return Ok();
    }

    // ── POST /api/forms/{id}/blocks ─────────────────────────────────────────
    [HttpPost("{id:int}/blocks")]
    public async Task<IActionResult> AddBlock(int id, int authUserId, [FromBody] SaveBlockRequest req)
    {
        if (!await ExistsAsync("SELECT 1 FROM Forms WHERE Id = @Id", new { Id = id }))
            return NotFound("הטופס לא נמצא");

        if (!IsValidBlockType(req.BlockType)) return BadRequest("סוג בלוק לא חוקי");
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("כותרת הבלוק חובה");

        int sortOrder = req.SortOrder > 0
            ? req.SortOrder
            : (await _db.GetRecordsAsync<int>(
                "SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM FormBlocks WHERE FormId = @Id",
                new { Id = id })).FirstOrDefault();

        int newId = await _db.InsertReturnIdAsync(@"
            INSERT INTO FormBlocks (FormId, BlockType, Title, HelperText, IsRequired, SortOrder)
            VALUES (@FormId, @BlockType, @Title, @HelperText, @IsRequiredInt, @SortOrder)",
            new
            {
                FormId       = id,
                req.BlockType,
                Title        = req.Title.Trim(),
                HelperText   = req.HelperText ?? "",
                IsRequiredInt= req.IsRequired ? 1 : 0,
                SortOrder    = sortOrder
            });

        if (newId == 0) return StatusCode(500, "שגיאה בהוספת הבלוק");

        await TouchFormAsync(id);
        return Ok(new { id = newId });
    }

    // ── PUT /api/forms/blocks/{blockId} ─────────────────────────────────────
    [HttpPut("blocks/{blockId:int}")]
    public async Task<IActionResult> UpdateBlock(int blockId, int authUserId, [FromBody] SaveBlockRequest req)
    {
        var info = (await _db.GetRecordsAsync<BlockInfoRow>(
            "SELECT FormId, BlockKey FROM FormBlocks WHERE Id = @Id",
            new { Id = blockId }))?.FirstOrDefault();

        if (info is null) return NotFound("הבלוק לא נמצא");

        if (!IsValidBlockType(req.BlockType)) return BadRequest("סוג בלוק לא חוקי");
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("כותרת הבלוק חובה");

        // System blocks cannot change BlockType (rendering depends on it).
        bool isSystem = !string.IsNullOrEmpty(info.BlockKey);
        if (isSystem)
        {
            var currentType = (await _db.GetRecordsAsync<string>(
                "SELECT BlockType FROM FormBlocks WHERE Id = @Id",
                new { Id = blockId })).FirstOrDefault() ?? "";
            if (!string.Equals(currentType, req.BlockType, StringComparison.OrdinalIgnoreCase))
                return BadRequest("לא ניתן לשנות סוג של בלוק מערכת");
        }

        await _db.SaveDataAsync(@"
            UPDATE FormBlocks
            SET    BlockType  = @BlockType,
                   Title      = @Title,
                   HelperText = @HelperText,
                   IsRequired = @IsRequiredInt,
                   SortOrder  = @SortOrder,
                   UpdatedAt  = datetime('now')
            WHERE  Id = @Id",
            new
            {
                Id           = blockId,
                req.BlockType,
                Title        = req.Title.Trim(),
                HelperText   = req.HelperText ?? "",
                IsRequiredInt= req.IsRequired ? 1 : 0,
                req.SortOrder
            });

        await TouchFormAsync(info.FormId);
        return Ok();
    }

    // ── DELETE /api/forms/blocks/{blockId} ──────────────────────────────────
    // System blocks (BlockKey != null) cannot be deleted.
    [HttpDelete("blocks/{blockId:int}")]
    public async Task<IActionResult> DeleteBlock(int blockId, int authUserId)
    {
        var info = (await _db.GetRecordsAsync<BlockInfoRow>(
            "SELECT FormId, BlockKey FROM FormBlocks WHERE Id = @Id",
            new { Id = blockId }))?.FirstOrDefault();

        if (info is null) return NotFound("הבלוק לא נמצא");

        if (!string.IsNullOrEmpty(info.BlockKey))
            return BadRequest("לא ניתן למחוק בלוק מערכת");

        await _db.SaveDataAsync("DELETE FROM FormBlocks WHERE Id = @Id", new { Id = blockId });
        await TouchFormAsync(info.FormId);
        return Ok();
    }

    // ── POST /api/forms/blocks/{blockId}/options ────────────────────────────
    [HttpPost("blocks/{blockId:int}/options")]
    public async Task<IActionResult> AddOption(int blockId, int authUserId, [FromBody] SaveOptionRequest req)
    {
        var info = (await _db.GetRecordsAsync<BlockInfoRow>(
            "SELECT FormId, BlockKey FROM FormBlocks WHERE Id = @Id",
            new { Id = blockId }))?.FirstOrDefault();
        if (info is null) return NotFound("הבלוק לא נמצא");

        if (string.IsNullOrWhiteSpace(req.OptionLabel)) return BadRequest("תווית האפשרות חובה");

        string value = string.IsNullOrWhiteSpace(req.OptionValue)
            ? req.OptionLabel.Trim()
            : req.OptionValue.Trim();

        int sortOrder = req.SortOrder > 0
            ? req.SortOrder
            : (await _db.GetRecordsAsync<int>(
                "SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM FormBlockOptions WHERE FormBlockId = @Id",
                new { Id = blockId })).FirstOrDefault();

        int newId = await _db.InsertReturnIdAsync(@"
            INSERT INTO FormBlockOptions (FormBlockId, OptionValue, OptionLabel, SortOrder)
            VALUES (@FormBlockId, @Value, @Label, @SortOrder)",
            new
            {
                FormBlockId = blockId,
                Value       = value,
                Label       = req.OptionLabel.Trim(),
                SortOrder   = sortOrder
            });

        if (newId == 0) return StatusCode(500, "שגיאה בהוספת האפשרות");

        await TouchFormAsync(info.FormId);
        return Ok(new { id = newId });
    }

    // ── PUT /api/forms/options/{optionId} ───────────────────────────────────
    [HttpPut("options/{optionId:int}")]
    public async Task<IActionResult> UpdateOption(int optionId, int authUserId, [FromBody] SaveOptionRequest req)
    {
        var info = (await _db.GetRecordsAsync<OptionInfoRow>(
            "SELECT o.FormBlockId, b.FormId FROM FormBlockOptions o JOIN FormBlocks b ON b.Id = o.FormBlockId WHERE o.Id = @Id",
            new { Id = optionId }))?.FirstOrDefault();
        if (info is null) return NotFound("האפשרות לא נמצאה");

        if (string.IsNullOrWhiteSpace(req.OptionLabel)) return BadRequest("תווית האפשרות חובה");

        string value = string.IsNullOrWhiteSpace(req.OptionValue)
            ? req.OptionLabel.Trim()
            : req.OptionValue.Trim();

        await _db.SaveDataAsync(@"
            UPDATE FormBlockOptions
            SET    OptionValue = @Value,
                   OptionLabel = @Label,
                   SortOrder   = @SortOrder
            WHERE  Id = @Id",
            new
            {
                Id        = optionId,
                Value     = value,
                Label     = req.OptionLabel.Trim(),
                req.SortOrder
            });

        await TouchFormAsync(info.FormId);
        return Ok();
    }

    // ── DELETE /api/forms/options/{optionId} ────────────────────────────────
    [HttpDelete("options/{optionId:int}")]
    public async Task<IActionResult> DeleteOption(int optionId, int authUserId)
    {
        var info = (await _db.GetRecordsAsync<OptionInfoRow>(
            "SELECT o.FormBlockId, b.FormId FROM FormBlockOptions o JOIN FormBlocks b ON b.Id = o.FormBlockId WHERE o.Id = @Id",
            new { Id = optionId }))?.FirstOrDefault();
        if (info is null) return NotFound("האפשרות לא נמצאה");

        await _db.SaveDataAsync("DELETE FROM FormBlockOptions WHERE Id = @Id", new { Id = optionId });
        await TouchFormAsync(info.FormId);
        return Ok();
    }

    // ── POST /api/forms/{id}/toggle-open ────────────────────────────────────
    // Convenience action used by the list page "פתיחה/סגירה" button. Cycles
    // between Open and Draft (or Closed when after the close date).
    [HttpPost("{id:int}/toggle-open")]
    public async Task<IActionResult> ToggleOpen(int id, int authUserId)
    {
        var row = (await _db.GetRecordsAsync<ToggleRow>(
            "SELECT IsOpen, Status, OpensAt, ClosesAt FROM Forms WHERE Id = @Id",
            new { Id = id }))?.FirstOrDefault();
        if (row is null) return NotFound("הטופס לא נמצא");

        bool   newOpen   = !row.IsOpen;
        string newStatus = newOpen ? FormStatuses.Open : FormStatuses.Draft;

        await _db.SaveDataAsync(@"
            UPDATE Forms
            SET    IsOpen    = @IsOpenInt,
                   Status    = @Status,
                   UpdatedAt = datetime('now')
            WHERE  Id = @Id",
            new { Id = id, IsOpenInt = newOpen ? 1 : 0, Status = newStatus });

        return Ok(new { id, isOpen = newOpen, status = newStatus });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Internal helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<FormDetailDto?> LoadFormDetailAsync(int formId)
    {
        const string formSql = @"
            SELECT  f.Id,
                    f.AcademicYearId,
                    COALESCE(ay.Name, '') AS AcademicYear,
                    f.Name,
                    f.FormType,
                    COALESCE(f.Instructions, '') AS Instructions,
                    f.IsOpen,
                    f.OpensAt,
                    f.ClosesAt,
                    f.AllowEditAfterSubmit,
                    f.Status,
                    (SELECT COUNT(1)
                     FROM   AssignmentFormSubmissions s
                     JOIN   Teams t ON t.Id = s.TeamId
                     WHERE  f.FormType = 'AssignmentForm'
                       AND  t.AcademicYearId = f.AcademicYearId) AS SubmissionCount
            FROM    Forms f
            LEFT JOIN AcademicYears ay ON ay.Id = f.AcademicYearId
            WHERE   f.Id = @Id";

        var form = (await _db.GetRecordsAsync<FormDetailDto>(formSql, new { Id = formId }))?.FirstOrDefault();
        if (form is null) return null;

        const string blocksSql = @"
            SELECT  Id, FormId, BlockType, BlockKey,
                    COALESCE(Title, '')      AS Title,
                    COALESCE(HelperText, '') AS HelperText,
                    IsRequired,
                    SortOrder
            FROM    FormBlocks
            WHERE   FormId = @Id
            ORDER   BY SortOrder, Id";

        var blocks = (await _db.GetRecordsAsync<FormBlockDto>(blocksSql, new { Id = formId }))?.ToList()
                     ?? new List<FormBlockDto>();

        if (blocks.Count > 0)
        {
            const string optsSql = @"
                SELECT  Id, FormBlockId, OptionValue, OptionLabel, SortOrder
                FROM    FormBlockOptions
                WHERE   FormBlockId IN (SELECT Id FROM FormBlocks WHERE FormId = @Id)
                ORDER   BY FormBlockId, SortOrder, Id";

            var opts = (await _db.GetRecordsAsync<FormBlockOptionDto>(optsSql, new { Id = formId }))?.ToList()
                       ?? new List<FormBlockOptionDto>();

            var byBlock = opts.GroupBy(o => o.FormBlockId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var b in blocks)
                if (byBlock.TryGetValue(b.Id, out var list)) b.Options = list;
        }

        form.Blocks = blocks;
        return form;
    }

    private async Task TouchFormAsync(int formId) =>
        await _db.SaveDataAsync(
            "UPDATE Forms SET UpdatedAt = datetime('now') WHERE Id = @Id",
            new { Id = formId });

    private async Task<int> GetCurrentAcademicYearIdAsync()
    {
        var rows = await _db.GetRecordsAsync<int>(
            "SELECT Id FROM AcademicYears WHERE IsCurrent = 1 ORDER BY Id DESC LIMIT 1");
        return rows?.FirstOrDefault() ?? 0;
    }

    private async Task<bool> ExistsAsync(string sql, object parameters)
    {
        var rows = await _db.GetRecordsAsync<int>(sql, parameters);
        return rows is not null && rows.Any();
    }

    private static bool IsValidBlockType(string t) => t is
        FormBlockTypes.Text or
        FormBlockTypes.SingleChoice or
        FormBlockTypes.MultiChoice or
        FormBlockTypes.Ranking or
        FormBlockTypes.OpenText;

    private static string NormalizeStatus(string raw, bool isOpen)
    {
        if (raw is FormStatuses.Draft or FormStatuses.Open or FormStatuses.Closed)
            return isOpen && raw == FormStatuses.Draft ? FormStatuses.Open : raw;
        return isOpen ? FormStatuses.Open : FormStatuses.Draft;
    }

    private static string? ValidateForm(SaveFormRequest req)
    {
        if (req is null) return "נתונים חסרים";
        if (string.IsNullOrWhiteSpace(req.Name))     return "שם הטופס חובה";
        if (string.IsNullOrWhiteSpace(req.FormType)) return "סוג הטופס חובה";
        if (req.AcademicYearId <= 0)                 return "מחזור אקדמי חובה";

        if (!string.IsNullOrWhiteSpace(req.OpensAt) &&
            !string.IsNullOrWhiteSpace(req.ClosesAt) &&
            DateTime.TryParse(req.OpensAt,  out var opens) &&
            DateTime.TryParse(req.ClosesAt, out var closes) &&
            opens >= closes)
        {
            return "תאריך הפתיחה חייב להיות לפני תאריך הסגירה";
        }

        return null;
    }

    // ── Private row types ────────────────────────────────────────────────────

    private sealed class FormTypeRow
    {
        public string FormType       { get; set; } = "";
        public int    AcademicYearId { get; set; }
    }

    private sealed class BlockInfoRow
    {
        public int     FormId   { get; set; }
        public string? BlockKey { get; set; }
    }

    private sealed class OptionInfoRow
    {
        public int FormBlockId { get; set; }
        public int FormId      { get; set; }
    }

    private sealed class ToggleRow
    {
        public bool    IsOpen   { get; set; }
        public string  Status   { get; set; } = "";
        public string? OpensAt  { get; set; }
        public string? ClosesAt { get; set; }
    }
}
