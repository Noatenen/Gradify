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
                    (SELECT COUNT(1) FROM FormBlocks b WHERE b.FormId = f.Id) AS QuestionCount,
                    (CASE WHEN f.FormType = 'AssignmentForm'
                          THEN (SELECT COUNT(1)
                                FROM   AssignmentFormSubmissions s
                                JOIN   Teams t ON t.Id = s.TeamId
                                WHERE  t.AcademicYearId = f.AcademicYearId)
                          ELSE (SELECT COUNT(1)
                                FROM   FormSubmissions fs
                                WHERE  fs.FormId = f.Id)
                     END) AS SubmissionCount
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

        // ── FormType, and why a custom form gets a generated one ──────────
        // Forms carries UNIQUE (AcademicYearId, FormType), which is exactly
        // right for AssignmentForm — a cycle has one, and a second would make
        // "the assignment form for this year" ambiguous. But it also means two
        // ordinary forms in the same cycle collide on any shared type string,
        // so "טופס חדש" could only ever be pressed once per cycle.
        //
        // Rather than drop a constraint the assignment flow depends on, a
        // custom form is given its own unique type. The constraint keeps
        // meaning what it meant; custom forms simply never share a key.
        string formType = req.FormType?.Trim() ?? "";

        if (formType.Length == 0 ||
            string.Equals(formType, CustomFormTypePrefix, StringComparison.OrdinalIgnoreCase))
        {
            formType = await GenerateCustomFormTypeAsync(req.AcademicYearId);
        }
        else
        {
            bool dup = await ExistsAsync(
                "SELECT 1 FROM Forms WHERE AcademicYearId = @YearId AND FormType = @Type",
                new { YearId = req.AcademicYearId, Type = formType });
            if (dup) return Conflict("כבר קיים טופס מסוג זה למחזור הנבחר");
        }

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
                FormType    = formType,
                Instructions= req.Instructions ?? "",
                IsOpenInt   = req.IsOpen ? 1 : 0,
                req.OpensAt,
                req.ClosesAt,
                AllowEditInt= req.AllowEditAfterSubmit ? 1 : 0,
                Status      = NormalizeStatus(req.Status, req.IsOpen)
            });

        if (newId == 0) return StatusCode(500, "שגיאה ביצירת הטופס");

        // For an AssignmentForm, seed the canonical 3 blocks. A custom form
        // starts genuinely empty — the editor's empty state invites the first
        // question rather than pre-filling questions nobody asked for.
        if (string.Equals(formType, FormsRepository.AssignmentFormType, StringComparison.OrdinalIgnoreCase))
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
            INSERT INTO FormBlocks
                (FormId, BlockType, Title, HelperText, IsRequired, SortOrder,
                 RatingScale, MinLabel, MaxLabel)
            VALUES
                (@FormId, @BlockType, @Title, @HelperText, @IsRequiredInt, @SortOrder,
                 @RatingScale, @MinLabel, @MaxLabel)",
            new
            {
                FormId       = id,
                req.BlockType,
                Title        = req.Title.Trim(),
                HelperText   = req.HelperText ?? "",
                IsRequiredInt= NormalizeRequired(req.BlockType, req.IsRequired),
                SortOrder    = sortOrder,
                RatingScale  = NormalizeScale(req.RatingScale),
                MinLabel     = req.MinLabel ?? "",
                MaxLabel     = req.MaxLabel ?? ""
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
            SET    BlockType   = @BlockType,
                   Title       = @Title,
                   HelperText  = @HelperText,
                   IsRequired  = @IsRequiredInt,
                   SortOrder   = @SortOrder,
                   RatingScale = @RatingScale,
                   MinLabel    = @MinLabel,
                   MaxLabel    = @MaxLabel,
                   UpdatedAt   = datetime('now')
            WHERE  Id = @Id",
            new
            {
                Id           = blockId,
                req.BlockType,
                Title        = req.Title.Trim(),
                HelperText   = req.HelperText ?? "",
                IsRequiredInt= NormalizeSystemRequired(info.BlockKey, req.BlockType, req.IsRequired),
                req.SortOrder,
                RatingScale  = NormalizeScale(req.RatingScale),
                MinLabel     = req.MinLabel ?? "",
                MaxLabel     = req.MaxLabel ?? ""
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

    // ── POST /api/forms/{id}/duplicate ──────────────────────────────────────
    //
    // Creates a new Form for the target academic year, copying:
    //   - Forms row (Instructions, AllowEditAfterSubmit) — Name / FormType /
    //     AcademicYearId / Status / IsOpen come from the request body.
    //   - All FormBlocks rows (Title, HelperText, IsRequired, SortOrder, etc.)
    //   - All FormBlockOptions rows for those blocks.
    //
    // Submissions and uploaded files are NEVER copied — duplication is
    // structure-only.
    //
    // Future-friendly: the underlying logic (read source + insert blocks /
    // options) is the same primitive a "form builder" page will need when it
    // creates a form from a blank template, so the standalone page can reuse
    // SeedAssignmentBlocks-style helpers + the per-block insert path.
    [HttpPost("{id:int}/duplicate")]
    public async Task<IActionResult> DuplicateForm(
        int id, int authUserId, [FromBody] DuplicateFormRequest req)
    {
        if (req is null) return BadRequest("גוף בקשה ריק");
        if (string.IsNullOrWhiteSpace(req.NewName))
            return BadRequest("שם הטופס החדש הוא שדה חובה");
        if (req.AcademicYearId <= 0)
            return BadRequest("יש לבחור מחזור / שנה אקדמית");
        if (req.InitialStatus != FormStatuses.Draft
            && req.InitialStatus != FormStatuses.Open
            && req.InitialStatus != FormStatuses.Closed)
            return BadRequest("סטטוס התחלתי לא תקין");

        // ── Load the source form (used as the structure template) ─────────
        var source = (await _db.GetRecordsAsync<SourceFormRow>(
                @"SELECT Id, AcademicYearId, FormType, Instructions, AllowEditAfterSubmit
                  FROM   Forms WHERE Id = @Id LIMIT 1",
                new { Id = id }))?.FirstOrDefault();
        if (source is null) return NotFound("הטופס המקור לא נמצא");

        // ── Validate target year exists ───────────────────────────────────
        if (!await ExistsAsync(
                "SELECT 1 FROM AcademicYears WHERE Id = @Id",
                new { Id = req.AcademicYearId }))
            return BadRequest("המחזור האקדמי לא נמצא");

        // ── Reject duplicates on the (AcademicYearId, FormType) UNIQUE ────
        // The Hebrew message gives the lecturer enough context to either edit
        // the existing form or pick a different year.
        bool dup = await ExistsAsync(
            "SELECT 1 FROM Forms WHERE AcademicYearId = @YearId AND FormType = @Type",
            new { YearId = req.AcademicYearId, Type = source.FormType });
        if (dup) return Conflict("כבר קיים טופס מסוג זה למחזור הנבחר. ערוך אותו או בחר מחזור אחר.");

        // ── Compute the new form's IsOpen and effective Status ────────────
        // - InitialStatus = Open  → IsOpen=1, Status=Open
        // - InitialStatus = Draft → IsOpen=0, Status=Draft
        // - InitialStatus = Closed→ IsOpen=0, Status=Closed
        bool newIsOpen   = req.InitialStatus == FormStatuses.Open;
        string newStatus = NormalizeStatus(req.InitialStatus, newIsOpen);

        // ── Insert the cloned Forms row ───────────────────────────────────
        int newFormId = await _db.InsertReturnIdAsync(@"
            INSERT INTO Forms
                (AcademicYearId, Name, FormType, Instructions, IsOpen, OpensAt, ClosesAt,
                 AllowEditAfterSubmit, Status)
            VALUES
                (@AcademicYearId, @Name, @FormType, @Instructions, @IsOpenInt, NULL, NULL,
                 @AllowEditInt, @Status)",
            new
            {
                AcademicYearId = req.AcademicYearId,
                Name           = req.NewName.Trim(),
                source.FormType,
                Instructions   = source.Instructions ?? "",
                IsOpenInt      = newIsOpen ? 1 : 0,
                AllowEditInt   = source.AllowEditAfterSubmit,
                Status         = newStatus,
            });
        if (newFormId == 0) return StatusCode(500, "שגיאה בשכפול הטופס");

        // ── Copy blocks ───────────────────────────────────────────────────
        // We re-key per-block (old → new) so block options can attach to the
        // new block id below. SortOrder is preserved verbatim.
        var sourceBlocks = (await _db.GetRecordsAsync<SourceBlockRow>(
                @"SELECT Id, BlockType, BlockKey, Title, HelperText, IsRequired, SortOrder
                  FROM   FormBlocks WHERE FormId = @FormId ORDER BY SortOrder, Id",
                new { FormId = source.Id }))?.ToList() ?? new();

        var idMap = new Dictionary<int, int>();
        foreach (var b in sourceBlocks)
        {
            int newBlockId = await _db.InsertReturnIdAsync(@"
                INSERT INTO FormBlocks
                    (FormId, BlockType, BlockKey, Title, HelperText, IsRequired, SortOrder)
                VALUES
                    (@FormId, @BlockType, @BlockKey, @Title, @HelperText, @IsRequiredInt, @SortOrder)",
                new
                {
                    FormId        = newFormId,
                    b.BlockType, b.BlockKey, b.Title,
                    HelperText    = b.HelperText ?? "",
                    IsRequiredInt = b.IsRequired,
                    b.SortOrder,
                });
            if (newBlockId > 0) idMap[b.Id] = newBlockId;
        }

        // ── Copy options for the blocks we copied ─────────────────────────
        if (idMap.Count > 0)
        {
            string idsCsv = string.Join(",", idMap.Keys);
            var sourceOptions = (await _db.GetRecordsAsync<SourceOptionRow>(
                    $@"SELECT FormBlockId, OptionValue, OptionLabel, SortOrder
                       FROM   FormBlockOptions
                       WHERE  FormBlockId IN ({idsCsv})
                       ORDER  BY FormBlockId, SortOrder, Id"))?.ToList() ?? new();

            foreach (var o in sourceOptions)
            {
                if (!idMap.TryGetValue(o.FormBlockId, out int newBlockId)) continue;
                await _db.SaveDataAsync(@"
                    INSERT INTO FormBlockOptions (FormBlockId, OptionValue, OptionLabel, SortOrder)
                    VALUES (@FormBlockId, @OptionValue, @OptionLabel, @SortOrder)",
                    new
                    {
                        FormBlockId = newBlockId,
                        o.OptionValue, o.OptionLabel, o.SortOrder,
                    });
            }
        }

        return Ok(new DuplicateFormResponse { NewFormId = newFormId });
    }

    private sealed class SourceFormRow
    {
        public int    Id                   { get; set; }
        public int    AcademicYearId       { get; set; }
        public string FormType             { get; set; } = "";
        public string? Instructions        { get; set; }
        public int    AllowEditAfterSubmit { get; set; }
    }

    private sealed class SourceBlockRow
    {
        public int     Id          { get; set; }
        public string  BlockType   { get; set; } = "";
        public string? BlockKey    { get; set; }
        public string  Title       { get; set; } = "";
        public string? HelperText  { get; set; }
        public int     IsRequired  { get; set; }
        public int     SortOrder   { get; set; }
    }

    private sealed class SourceOptionRow
    {
        public int    FormBlockId { get; set; }
        public string OptionValue { get; set; } = "";
        public string OptionLabel { get; set; } = "";
        public int    SortOrder   { get; set; }
    }

    // ── PUT /api/forms/{id}/structure ───────────────────────────────────────
    //
    //  The editor's single "שמירה". Replaces the question list in one call.
    //
    //  UPSERT, NOT REPLACE
    //  ───────────────────
    //  Blocks and options keep their Ids. Deleting and re-inserting would give
    //  every block a fresh Id and orphan every FormAnswer already recorded
    //  against it, so an admin fixing a typo would quietly destroy the
    //  responses. Id = 0 means "new"; everything else is matched and updated.
    //
    //  ORDER
    //  ─────
    //  SortOrder is written from array position rather than taken from the
    //  payload, so what the admin sees is what persists. Reordering is
    //  therefore just a save — there is no separate reorder path that could
    //  half-apply.
    //
    //  ATOMICITY
    //  ─────────
    //  DbRepository exposes no transaction (every write in this codebase is a
    //  standalone statement), so this is not atomic. Writes are ordered
    //  upserts-first / deletes-last: a failure part-way leaves extra blocks,
    //  never missing ones, and re-saving converges.
    [HttpPut("{id:int}/structure")]
    public async Task<IActionResult> SaveStructure(int id, int authUserId, [FromBody] SaveFormStructureRequest req)
    {
        if (req is null) return BadRequest("נתונים חסרים");

        if (!await ExistsAsync("SELECT 1 FROM Forms WHERE Id = @Id", new { Id = id }))
            return NotFound("הטופס לא נמצא");

        // Existing structure, needed to tell inserts from updates and to know
        // which blocks are system-anchored.
        var existingBlocks = (await _db.GetRecordsAsync<StructureBlockRow>(
            "SELECT Id, BlockType, BlockKey FROM FormBlocks WHERE FormId = @Id",
            new { Id = id }))?.ToList() ?? new List<StructureBlockRow>();

        var existingById = existingBlocks.ToDictionary(b => b.Id);

        var existingOptions = (await _db.GetRecordsAsync<StructureOptionRow>(@"
            SELECT o.Id, o.FormBlockId, o.OptionValue
            FROM   FormBlockOptions o
            JOIN   FormBlocks b ON b.Id = o.FormBlockId
            WHERE  b.FormId = @Id",
            new { Id = id }))?.ToList() ?? new List<StructureOptionRow>();

        // ── Validate the whole payload before writing anything ────────────
        foreach (var incoming in req.Blocks)
        {
            if (!IsValidBlockType(incoming.BlockType))
                return BadRequest("סוג שאלה לא חוקי");

            if (string.IsNullOrWhiteSpace(incoming.Title))
                return BadRequest("לכל שאלה בטופס חייבת להיות כותרת");

            if (incoming.Id > 0 && existingById.TryGetValue(incoming.Id, out var current))
            {
                // A system block's type is load-bearing — the assignment flow
                // reads it by key and expects a specific shape.
                if (!string.IsNullOrEmpty(current.BlockKey) &&
                    !string.Equals(current.BlockType, incoming.BlockType, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest("לא ניתן לשנות סוג של בלוק מערכת");
                }
            }
        }

        int sortOrder = 0;
        var keptBlockIds = new HashSet<int>();

        foreach (var incoming in req.Blocks)
        {
            sortOrder++;

            bool   isExisting = incoming.Id > 0 && existingById.ContainsKey(incoming.Id);
            var    current    = isExisting ? existingById[incoming.Id] : null;
            string blockKey   = current?.BlockKey ?? "";
            int    blockId;

            if (isExisting)
            {
                blockId = incoming.Id;
                await _db.SaveDataAsync(@"
                    UPDATE FormBlocks
                    SET    BlockType   = @BlockType,
                           Title       = @Title,
                           HelperText  = @HelperText,
                           IsRequired  = @IsRequiredInt,
                           SortOrder   = @SortOrder,
                           RatingScale = @RatingScale,
                           MinLabel    = @MinLabel,
                           MaxLabel    = @MaxLabel,
                           UpdatedAt   = datetime('now')
                    WHERE  Id = @Id",
                    new
                    {
                        Id            = blockId,
                        incoming.BlockType,
                        Title         = incoming.Title.Trim(),
                        HelperText    = incoming.HelperText ?? "",
                        IsRequiredInt = NormalizeSystemRequired(blockKey, incoming.BlockType, incoming.IsRequired),
                        SortOrder     = sortOrder,
                        RatingScale   = NormalizeScale(incoming.RatingScale),
                        MinLabel      = incoming.MinLabel ?? "",
                        MaxLabel      = incoming.MaxLabel ?? ""
                    });
            }
            else
            {
                blockId = await _db.InsertReturnIdAsync(@"
                    INSERT INTO FormBlocks
                        (FormId, BlockType, Title, HelperText, IsRequired, SortOrder,
                         RatingScale, MinLabel, MaxLabel)
                    VALUES
                        (@FormId, @BlockType, @Title, @HelperText, @IsRequiredInt, @SortOrder,
                         @RatingScale, @MinLabel, @MaxLabel)",
                    new
                    {
                        FormId        = id,
                        incoming.BlockType,
                        Title         = incoming.Title.Trim(),
                        HelperText    = incoming.HelperText ?? "",
                        IsRequiredInt = NormalizeRequired(incoming.BlockType, incoming.IsRequired),
                        SortOrder     = sortOrder,
                        RatingScale   = NormalizeScale(incoming.RatingScale),
                        MinLabel      = incoming.MinLabel ?? "",
                        MaxLabel      = incoming.MaxLabel ?? ""
                    });

                if (blockId == 0) return StatusCode(500, "שגיאה בשמירת השאלות");
            }

            keptBlockIds.Add(blockId);
            await SaveBlockOptionsAsync(blockId, blockKey, incoming, existingOptions);
        }

        // ── Deletes last ──────────────────────────────────────────────────
        // System blocks are never removed, even when a client omits them: the
        // assignment flow looks them up by key and a missing one would break
        // the student form. They are kept and pushed after the sent blocks.
        int tailOrder = sortOrder;
        foreach (var stale in existingBlocks.Where(b => !keptBlockIds.Contains(b.Id)))
        {
            if (!string.IsNullOrEmpty(stale.BlockKey))
            {
                tailOrder++;
                await _db.SaveDataAsync(
                    "UPDATE FormBlocks SET SortOrder = @SortOrder WHERE Id = @Id",
                    new { Id = stale.Id, SortOrder = tailOrder });
                continue;
            }

            await _db.SaveDataAsync("DELETE FROM FormBlocks WHERE Id = @Id", new { Id = stale.Id });
        }

        await TouchFormAsync(id);

        var detail = await LoadFormDetailAsync(id);
        return Ok(detail);
    }

    /// <summary>
    /// Upserts one block's options, honouring the two system-block rules.
    /// </summary>
    private async Task SaveBlockOptionsAsync(
        int blockId,
        string blockKey,
        SaveStructureBlockDto incoming,
        List<StructureOptionRow> existingOptions)
    {
        // ProjectPreferences draws its choices from the live project catalog.
        // It has no stored options and must not gain any — static rows here
        // would be disconnected strings where assignment expects Projects.Id.
        if (FormBlockKeys.HasSystemSuppliedOptions(blockKey)) return;

        var currentForBlock = existingOptions.Where(o => o.FormBlockId == blockId).ToList();
        var currentById     = currentForBlock.ToDictionary(o => o.Id);

        // Options only mean anything for choice blocks. Switching a block away
        // from a choice type clears them rather than leaving dead rows behind.
        if (!FormBlockTypes.HasOptions(incoming.BlockType))
        {
            foreach (var stale in currentForBlock)
                await _db.SaveDataAsync("DELETE FROM FormBlockOptions WHERE Id = @Id", new { Id = stale.Id });
            return;
        }

        bool valuesProtected = FormBlockKeys.HasProtectedOptionValues(blockKey);

        int  order = 0;
        var  kept  = new HashSet<int>();

        foreach (var opt in incoming.Options)
        {
            order++;

            string label = (opt.OptionLabel ?? "").Trim();
            if (label.Length == 0) continue;   // an empty option row is not an option

            bool isExisting = opt.Id > 0 && currentById.ContainsKey(opt.Id);

            // The machine value. For the Strengths block it is what
            // SkillWeight() switches on, so an existing option keeps the value
            // already stored no matter what the client sends. Elsewhere it
            // falls back to the label when the client leaves it blank.
            string value;
            if (isExisting && valuesProtected)
                value = currentById[opt.Id].OptionValue;
            else
                value = string.IsNullOrWhiteSpace(opt.OptionValue) ? label : opt.OptionValue.Trim();

            if (isExisting)
            {
                await _db.SaveDataAsync(@"
                    UPDATE FormBlockOptions
                    SET    OptionValue = @OptionValue,
                           OptionLabel = @OptionLabel,
                           SortOrder   = @SortOrder
                    WHERE  Id = @Id",
                    new { Id = opt.Id, OptionValue = value, OptionLabel = label, SortOrder = order });

                kept.Add(opt.Id);
            }
            else
            {
                int newOptId = await _db.InsertReturnIdAsync(@"
                    INSERT INTO FormBlockOptions (FormBlockId, OptionValue, OptionLabel, SortOrder)
                    VALUES (@BlockId, @OptionValue, @OptionLabel, @SortOrder)",
                    new { BlockId = blockId, OptionValue = value, OptionLabel = label, SortOrder = order });

                if (newOptId > 0) kept.Add(newOptId);
            }
        }

        foreach (var stale in currentForBlock.Where(o => !kept.Contains(o.Id)))
        {
            // A protected option is not deletable, only re-wordable.
            //
            // Renaming its VALUE was already refused above, but deleting the
            // row was not — and dropping "Technology" removes that strength
            // from the student form entirely, which silently changes what
            // SkillWeight() can ever score. Same reasoning as system blocks:
            // it is kept and pushed to the end rather than removed.
            if (valuesProtected &&
                FormBlockKeys.ProtectedStrengthValues.Contains(stale.OptionValue, StringComparer.Ordinal))
            {
                order++;
                await _db.SaveDataAsync(
                    "UPDATE FormBlockOptions SET SortOrder = @SortOrder WHERE Id = @Id",
                    new { Id = stale.Id, SortOrder = order });
                continue;
            }

            await _db.SaveDataAsync("DELETE FROM FormBlockOptions WHERE Id = @Id", new { Id = stale.Id });
        }
    }

    // ── GET /api/forms/{id}/submissions ─────────────────────────────────────
    //
    //  Real rows from FormSubmissions. The assignment form is deliberately NOT
    //  served here: its submissions are team-scoped and live in
    //  AssignmentFormSubmissions, and they already have a purpose-built screen
    //  at /assignments. Returning an empty list for it would read as "nobody
    //  submitted", so this reports the mismatch instead and the client links
    //  to the real screen.
    [HttpGet("{id:int}/submissions")]
    public async Task<IActionResult> GetSubmissions(int id, int authUserId)
    {
        var info = (await _db.GetRecordsAsync<FormTypeRow>(
            "SELECT FormType, AcademicYearId FROM Forms WHERE Id = @Id",
            new { Id = id }))?.FirstOrDefault();

        if (info is null) return NotFound("הטופס לא נמצא");

        if (string.Equals(info.FormType, FormsRepository.AssignmentFormType, StringComparison.OrdinalIgnoreCase))
            return BadRequest("הגשות טופס השיבוץ מנוהלות במסך השיבוצים");

        const string sql = @"
            SELECT  fs.Id,
                    fs.FormId,
                    fs.UserId,
                    COALESCE(u.FirstName || ' ' || u.LastName, '') AS UserName,
                    COALESCE(u.Email, '')                          AS UserEmail,
                    fs.SubmittedAt,
                    fs.UpdatedAt,
                    (SELECT COUNT(DISTINCT a.FormBlockId)
                     FROM   FormAnswers a
                     WHERE  a.FormSubmissionId = fs.Id) AS AnswerCount
            FROM    FormSubmissions fs
            LEFT JOIN users u ON u.Id = fs.UserId
            WHERE   fs.FormId = @Id
            ORDER   BY fs.UpdatedAt DESC, fs.Id DESC";

        var rows = (await _db.GetRecordsAsync<FormSubmissionListItemDto>(sql, new { Id = id }))?.ToList()
                   ?? new List<FormSubmissionListItemDto>();

        return Ok(rows);
    }

    // ── GET /api/forms/submissions/{submissionId} ───────────────────────────
    [HttpGet("submissions/{submissionId:int}")]
    public async Task<IActionResult> GetSubmission(int submissionId, int authUserId)
    {
        const string headSql = @"
            SELECT  fs.Id,
                    fs.FormId,
                    COALESCE(f.Name, '')                           AS FormName,
                    fs.UserId,
                    COALESCE(u.FirstName || ' ' || u.LastName, '') AS UserName,
                    COALESCE(u.Email, '')                          AS UserEmail,
                    fs.SubmittedAt,
                    fs.UpdatedAt
            FROM    FormSubmissions fs
            LEFT JOIN Forms f ON f.Id = fs.FormId
            LEFT JOIN users u ON u.Id = fs.UserId
            WHERE   fs.Id = @Id";

        var head = (await _db.GetRecordsAsync<FormSubmissionDetailDto>(headSql, new { Id = submissionId }))
                   ?.FirstOrDefault();
        if (head is null) return NotFound("ההגשה לא נמצאה");

        head.Answers = await LoadAnswersForDisplayAsync(submissionId, head.FormId);
        return Ok(head);
    }

    /// <summary>
    /// Resolves stored answers into display rows: option VALUES become their
    /// current labels, and blocks with no answer are omitted.
    /// </summary>
    private async Task<List<FormAnswerDto>> LoadAnswersForDisplayAsync(int submissionId, int formId)
    {
        var blocks = (await _db.GetRecordsAsync<FormBlockDto>(@"
            SELECT  Id, FormId, BlockType, BlockKey,
                    COALESCE(Title, '') AS Title,
                    SortOrder
            FROM    FormBlocks
            WHERE   FormId = @FormId
            ORDER   BY SortOrder, Id",
            new { FormId = formId }))?.ToList() ?? new List<FormBlockDto>();

        var options = (await _db.GetRecordsAsync<FormBlockOptionDto>(@"
            SELECT  o.Id, o.FormBlockId, o.OptionValue, o.OptionLabel, o.SortOrder
            FROM    FormBlockOptions o
            JOIN    FormBlocks b ON b.Id = o.FormBlockId
            WHERE   b.FormId = @FormId",
            new { FormId = formId }))?.ToList() ?? new List<FormBlockOptionDto>();

        var answers = (await _db.GetRecordsAsync<AnswerRow>(@"
            SELECT  FormBlockId, OptionValue, AnswerText, AnswerNumber, SortOrder
            FROM    FormAnswers
            WHERE   FormSubmissionId = @Id
            ORDER   BY FormBlockId, SortOrder, Id",
            new { Id = submissionId }))?.ToList() ?? new List<AnswerRow>();

        var byBlock = answers.GroupBy(a => a.FormBlockId).ToDictionary(g => g.Key, g => g.ToList());
        var result  = new List<FormAnswerDto>();

        foreach (var b in blocks)
        {
            if (FormBlockTypes.IsInformational(b.BlockType)) continue;
            if (!byBlock.TryGetValue(b.Id, out var rows)) continue;

            var dto = new FormAnswerDto
            {
                FormBlockId = b.Id,
                BlockType   = b.BlockType,
                BlockTitle  = b.Title
            };

            foreach (var r in rows)
            {
                if (r.AnswerNumber.HasValue) dto.Number = r.AnswerNumber;
                if (!string.IsNullOrEmpty(r.AnswerText)) dto.Text = r.AnswerText;

                if (!string.IsNullOrEmpty(r.OptionValue))
                {
                    // Resolve to the CURRENT label; fall back to the raw stored
                    // value when the option has since been removed, so a
                    // deleted option never blanks a real answer.
                    var match = options.FirstOrDefault(
                        o => o.FormBlockId == b.Id &&
                             string.Equals(o.OptionValue, r.OptionValue, StringComparison.Ordinal));

                    dto.Values.Add(match?.OptionLabel is { Length: > 0 } lbl ? lbl : r.OptionValue);
                }
            }

            result.Add(dto);
        }

        return result;
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
                    (CASE WHEN f.FormType = 'AssignmentForm'
                          THEN (SELECT COUNT(1)
                                FROM   AssignmentFormSubmissions s
                                JOIN   Teams t ON t.Id = s.TeamId
                                WHERE  t.AcademicYearId = f.AcademicYearId)
                          ELSE (SELECT COUNT(1)
                                FROM   FormSubmissions fs
                                WHERE  fs.FormId = f.Id)
                     END) AS SubmissionCount
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
                    SortOrder,
                    COALESCE(RatingScale, 5) AS RatingScale,
                    COALESCE(MinLabel, '')   AS MinLabel,
                    COALESCE(MaxLabel, '')   AS MaxLabel
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

    // Was a hand-written subset that silently omitted Heading, FileUpload and
    // Date, so the reference's "טקסט / מידע" block could never be saved. Reads
    // the canonical list instead, which now also carries Rating.
    private static bool IsValidBlockType(string t) =>
        FormBlockTypes.All.Contains(t, StringComparer.Ordinal);

    /// <summary>Marker the client sends when it wants a generated form type.</summary>
    private const string CustomFormTypePrefix = "Custom";

    /// <summary>
    /// Produces a form type unique within the cycle: Custom, then Custom-2,
    /// Custom-3 … Deterministic and readable, so the stored value still says
    /// what it is when read straight out of the table.
    /// </summary>
    private async Task<string> GenerateCustomFormTypeAsync(int academicYearId)
    {
        var taken = (await _db.GetRecordsAsync<string>(
            "SELECT FormType FROM Forms WHERE AcademicYearId = @YearId",
            new { YearId = academicYearId }))?.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(CustomFormTypePrefix)) return CustomFormTypePrefix;

        for (int i = 2; i < 10_000; i++)
        {
            var candidate = $"{CustomFormTypePrefix}-{i}";
            if (!taken.Contains(candidate)) return candidate;
        }

        return $"{CustomFormTypePrefix}-{Guid.NewGuid():N}";
    }

    /// <summary>Rating scales are 5 or 10; anything else falls back to 5.</summary>
    private static int NormalizeScale(int raw) => raw == 10 ? 10 : 5;

    /// <summary>
    /// An informational block carries no answer, so it can never be required.
    /// Enforced server-side as well as in the editor: a Heading saved as
    /// required would make the student form permanently unsubmittable.
    /// </summary>
    private static int NormalizeRequired(string blockType, bool isRequired) =>
        FormBlockTypes.IsInformational(blockType) ? 0 : (isRequired ? 1 : 0);

    /// <summary>
    /// Same rule, plus the one system block whose required flag is not the
    /// admin's to set.
    ///
    /// The project-preferences block is backed by TeamProjectPreferences,
    /// which AssignmentManagementController scores 30/20/10 by priority and
    /// joins to Projects. Exactly three ranked choices is a DOMAIN invariant,
    /// not a form setting, so an admin clearing "required" here would produce
    /// a form whose own configuration contradicts what the server enforces on
    /// submit. It is pinned instead, and the editor draws it as pinned.
    /// </summary>
    private static int NormalizeSystemRequired(string? blockKey, string blockType, bool isRequired) =>
        FormBlockKeys.HasSystemSuppliedOptions(blockKey)
            ? 1
            : NormalizeRequired(blockType, isRequired);

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

    private sealed class StructureBlockRow
    {
        public int     Id        { get; set; }
        public string  BlockType { get; set; } = "";
        public string? BlockKey  { get; set; }
    }

    private sealed class StructureOptionRow
    {
        public int    Id          { get; set; }
        public int    FormBlockId { get; set; }
        public string OptionValue { get; set; } = "";
    }

    private sealed class AnswerRow
    {
        public int     FormBlockId  { get; set; }
        public string? OptionValue  { get; set; }
        public string? AnswerText   { get; set; }
        public int?    AnswerNumber { get; set; }
        public int     SortOrder    { get; set; }
    }

    private sealed class ToggleRow
    {
        public bool    IsOpen   { get; set; }
        public string  Status   { get; set; } = "";
        public string? OpensAt  { get; set; }
        public string? ClosesAt { get; set; }
    }
}
