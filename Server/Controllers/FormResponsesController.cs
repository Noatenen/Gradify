using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  FormResponsesController — /api/form-responses
//
//  The answering half of the form builder. Until this existed, an admin could
//  describe a form but nobody could fill one in: the only answer storage in the
//  system belonged to the assignment form's own domain tables.
//
//  WHAT THIS DOES NOT TOUCH
//  ────────────────────────
//  The assignment form. Its answers are team-scoped and carry real Projects.Id
//  values that AssignmentManagementController joins and scores, so it keeps its
//  dedicated endpoints (/api/assignment) and its dedicated tables. Asking for
//  it here returns a redirect instruction rather than an empty generic form —
//  rendering it generically would turn project preferences into loose strings
//  and silently break assignment.
//
//  The submission window is not re-implemented here. It is the same
//  FormsRepository.EvaluateGate the admin's status chip and the assignment
//  flow already use.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/form-responses")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize]
public class FormResponsesController : ControllerBase
{
    private readonly DbRepository _db;

    public FormResponsesController(DbRepository db) => _db = db;

    // ── GET /api/form-responses/available ───────────────────────────────────
    // Forms in the caller's current cycle that are not drafts. Draft forms are
    // withheld entirely: a draft is the admin's workspace, not a thing a
    // student should discover.
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailable(int authUserId)
    {
        const string sql = @"
            SELECT  f.Id                AS FormId,
                    f.Name,
                    f.Status,
                    f.IsOpen,
                    f.OpensAt,
                    f.ClosesAt,
                    f.AllowEditAfterSubmit,
                    COALESCE(f.Instructions, '') AS Instructions,
                    (SELECT fs.SubmittedAt FROM FormSubmissions fs
                     WHERE  fs.FormId = f.Id AND fs.UserId = @UserId) AS SubmittedAt
            FROM    Forms f
            JOIN    AcademicYears ay ON ay.Id = f.AcademicYearId
            WHERE   f.FormType <> @AssignmentType
              AND   f.Status <> @Draft
              AND   (ay.IsCurrent = 1 OR ay.IsActive = 1)
            ORDER   BY f.UpdatedAt DESC";

        var rows = (await _db.GetRecordsAsync<AvailableRow>(sql, new
        {
            UserId         = authUserId,
            AssignmentType = FormsRepository.AssignmentFormType,
            Draft          = FormStatuses.Draft
        }))?.ToList() ?? new List<AvailableRow>();

        var result = rows.Select(r =>
        {
            var gate = FormsRepository.EvaluateGate(
                r.IsOpen, r.Status, r.OpensAt, r.ClosesAt,
                r.AllowEditAfterSubmit, r.Instructions,
                hasExistingSubmission: r.SubmittedAt is not null);

            return new StudentFormListItemDto
            {
                FormId       = r.FormId,
                Name         = r.Name,
                Status       = r.Status,
                OpensAt      = r.OpensAt,
                ClosesAt     = r.ClosesAt,
                CanSubmit    = gate.CanSubmit,
                HasSubmitted = r.SubmittedAt is not null,
                SubmittedAt  = r.SubmittedAt
            };
        }).ToList();

        return Ok(result);
    }

    // ── GET /api/form-responses/{formId} ────────────────────────────────────
    [HttpGet("{formId:int}")]
    public async Task<IActionResult> GetForm(int formId, int authUserId)
    {
        var form = await LoadFormAsync(formId);
        if (form is null) return NotFound("הטופס לא נמצא");

        if (string.Equals(form.FormType, FormsRepository.AssignmentFormType, StringComparison.OrdinalIgnoreCase))
            return BadRequest("טופס שיבוץ פרויקט מוגש במסך השיבוץ");

        // A draft has no student-facing existence.
        if (string.Equals(form.Status, FormStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            return NotFound("הטופס אינו זמין");

        var existing = await LoadExistingSubmissionAsync(formId, authUserId);

        var gate = FormsRepository.EvaluateGate(
            form.IsOpen, form.Status, form.OpensAt, form.ClosesAt,
            form.AllowEditAfterSubmit, form.Instructions,
            hasExistingSubmission: existing is not null);

        var dto = new FormFillDto
        {
            FormId               = form.Id,
            Name                 = form.Name,
            Instructions         = form.Instructions,
            AcademicYear         = form.AcademicYear,
            Status               = form.Status,
            OpensAt              = form.OpensAt,
            ClosesAt             = form.ClosesAt,
            AllowEditAfterSubmit = form.AllowEditAfterSubmit,
            CanSubmit            = gate.CanSubmit,
            ClosedReason         = gate.ClosedReason,
            ClosedMessage        = gate.ClosedMessage,
            SubmittedAt          = existing?.SubmittedAt,
            Blocks               = await LoadBlocksAsync(formId)
        };

        if (existing is not null)
            dto.ExistingAnswers = await LoadExistingAnswersAsync(existing.Id);

        return Ok(dto);
    }

    // ── POST /api/form-responses/{formId} ───────────────────────────────────
    [HttpPost("{formId:int}")]
    public async Task<IActionResult> Submit(int formId, int authUserId, [FromBody] SubmitFormResponseRequest req)
    {
        if (req is null) return BadRequest("נתונים חסרים");

        var form = await LoadFormAsync(formId);
        if (form is null) return NotFound("הטופס לא נמצא");

        if (string.Equals(form.FormType, FormsRepository.AssignmentFormType, StringComparison.OrdinalIgnoreCase))
            return BadRequest("טופס שיבוץ פרויקט מוגש במסך השיבוץ");

        var existing = await LoadExistingSubmissionAsync(formId, authUserId);

        // Gate BEFORE any write, so a closed form leaves the DB untouched.
        var gate = FormsRepository.EvaluateGate(
            form.IsOpen, form.Status, form.OpensAt, form.ClosesAt,
            form.AllowEditAfterSubmit, form.Instructions,
            hasExistingSubmission: existing is not null);

        if (!gate.CanSubmit)
            return BadRequest(gate.ClosedMessage ?? "הטופס אינו פתוח להגשה כרגע.");

        var blocks = await LoadBlocksAsync(formId);

        // ── Server-side required validation ───────────────────────────────
        // The client validates too, but the client is not the authority: a
        // required question must be answered even if the page is bypassed.
        var byBlock = req.Answers
            .Where(a => a.FormBlockId > 0)
            .GroupBy(a => a.FormBlockId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var b in blocks)
        {
            if (!b.IsRequired || FormBlockTypes.IsInformational(b.BlockType)) continue;

            bool answered = byBlock.TryGetValue(b.Id, out var a) && IsAnswered(b.BlockType, a);
            if (!answered)
                return BadRequest($"יש לענות על השאלה \"{b.Title}\"");
        }

        // ── Upsert the submission header ──────────────────────────────────
        int submissionId;
        if (existing is not null)
        {
            submissionId = existing.Id;
            await _db.SaveDataAsync(
                "UPDATE FormSubmissions SET UpdatedAt = datetime('now') WHERE Id = @Id",
                new { Id = submissionId });

            // Answers are replaced wholesale for this submission only. This is
            // the student's own row, and the previous answers are exactly what
            // they are editing.
            await _db.SaveDataAsync(
                "DELETE FROM FormAnswers WHERE FormSubmissionId = @Id",
                new { Id = submissionId });
        }
        else
        {
            submissionId = await _db.InsertReturnIdAsync(@"
                INSERT INTO FormSubmissions (FormId, UserId, SubmittedAt, UpdatedAt)
                VALUES (@FormId, @UserId, datetime('now'), datetime('now'))",
                new { FormId = formId, UserId = authUserId });

            if (submissionId == 0) return StatusCode(500, "שגיאה בשמירת ההגשה");
        }

        // ── Write the answers ─────────────────────────────────────────────
        var blockTypes = blocks.ToDictionary(b => b.Id, b => b.BlockType);

        foreach (var a in req.Answers)
        {
            if (!blockTypes.TryGetValue(a.FormBlockId, out var type)) continue;  // not a block of this form
            if (FormBlockTypes.IsInformational(type)) continue;                  // carries no answer

            if (FormBlockTypes.IsRating(type))
            {
                if (a.Number is null) continue;
                await InsertAnswerAsync(submissionId, a.FormBlockId, null, null, a.Number, 0);
                continue;
            }

            if (FormBlockTypes.HasOptions(type))
            {
                int order = 0;
                foreach (var v in a.OptionValues.Where(v => !string.IsNullOrWhiteSpace(v)))
                    await InsertAnswerAsync(submissionId, a.FormBlockId, v, null, null, order++);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(a.Text))
                await InsertAnswerAsync(submissionId, a.FormBlockId, null, a.Text.Trim(), null, 0);
        }

        return Ok(new { submissionId });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsAnswered(string blockType, FormAnswerInputDto a)
    {
        if (FormBlockTypes.IsRating(blockType))    return a.Number is > 0;
        if (FormBlockTypes.HasOptions(blockType))  return a.OptionValues.Any(v => !string.IsNullOrWhiteSpace(v));
        return !string.IsNullOrWhiteSpace(a.Text);
    }

    private async Task InsertAnswerAsync(
        int submissionId, int blockId, string? optionValue, string? text, int? number, int sortOrder) =>
        await _db.SaveDataAsync(@"
            INSERT INTO FormAnswers
                (FormSubmissionId, FormBlockId, OptionValue, AnswerText, AnswerNumber, SortOrder)
            VALUES
                (@SubmissionId, @BlockId, @OptionValue, @AnswerText, @AnswerNumber, @SortOrder)",
            new
            {
                SubmissionId = submissionId,
                BlockId      = blockId,
                OptionValue  = optionValue,
                AnswerText   = text,
                AnswerNumber = number,
                SortOrder    = sortOrder
            });

    private async Task<FormRow?> LoadFormAsync(int formId)
    {
        const string sql = @"
            SELECT  f.Id,
                    f.Name,
                    f.FormType,
                    COALESCE(f.Instructions, '') AS Instructions,
                    COALESCE(ay.Name, '')        AS AcademicYear,
                    f.IsOpen,
                    f.OpensAt,
                    f.ClosesAt,
                    f.AllowEditAfterSubmit,
                    f.Status
            FROM    Forms f
            LEFT JOIN AcademicYears ay ON ay.Id = f.AcademicYearId
            WHERE   f.Id = @Id";

        return (await _db.GetRecordsAsync<FormRow>(sql, new { Id = formId }))?.FirstOrDefault();
    }

    private async Task<List<FormBlockDto>> LoadBlocksAsync(int formId)
    {
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

        if (blocks.Count == 0) return blocks;

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

        return blocks;
    }

    private async Task<SubmissionRow?> LoadExistingSubmissionAsync(int formId, int userId) =>
        (await _db.GetRecordsAsync<SubmissionRow>(
            "SELECT Id, SubmittedAt FROM FormSubmissions WHERE FormId = @FormId AND UserId = @UserId LIMIT 1",
            new { FormId = formId, UserId = userId }))?.FirstOrDefault();

    private async Task<List<FormAnswerInputDto>> LoadExistingAnswersAsync(int submissionId)
    {
        var rows = (await _db.GetRecordsAsync<StoredAnswerRow>(@"
            SELECT  FormBlockId, OptionValue, AnswerText, AnswerNumber
            FROM    FormAnswers
            WHERE   FormSubmissionId = @Id
            ORDER   BY FormBlockId, SortOrder, Id",
            new { Id = submissionId }))?.ToList() ?? new List<StoredAnswerRow>();

        return rows
            .GroupBy(r => r.FormBlockId)
            .Select(g => new FormAnswerInputDto
            {
                FormBlockId  = g.Key,
                OptionValues = g.Where(r => !string.IsNullOrEmpty(r.OptionValue))
                                .Select(r => r.OptionValue!)
                                .ToList(),
                Text         = g.Select(r => r.AnswerText).FirstOrDefault(t => !string.IsNullOrEmpty(t)),
                Number       = g.Select(r => r.AnswerNumber).FirstOrDefault(n => n.HasValue)
            })
            .ToList();
    }

    // ── Row types ────────────────────────────────────────────────────────────

    private sealed class FormRow
    {
        public int     Id                   { get; set; }
        public string  Name                 { get; set; } = "";
        public string  FormType             { get; set; } = "";
        public string  Instructions         { get; set; } = "";
        public string  AcademicYear         { get; set; } = "";
        public bool    IsOpen               { get; set; }
        public string? OpensAt              { get; set; }
        public string? ClosesAt             { get; set; }
        public bool    AllowEditAfterSubmit { get; set; }
        public string  Status               { get; set; } = "";
    }

    private sealed class AvailableRow
    {
        public int     FormId               { get; set; }
        public string  Name                 { get; set; } = "";
        public string  Status               { get; set; } = "";
        public bool    IsOpen               { get; set; }
        public string? OpensAt              { get; set; }
        public string? ClosesAt             { get; set; }
        public bool    AllowEditAfterSubmit { get; set; }
        public string  Instructions         { get; set; } = "";
        public string? SubmittedAt          { get; set; }
    }

    private sealed class SubmissionRow
    {
        public int    Id          { get; set; }
        public string SubmittedAt { get; set; } = "";
    }

    private sealed class StoredAnswerRow
    {
        public int     FormBlockId  { get; set; }
        public string? OptionValue  { get; set; }
        public string? AnswerText   { get; set; }
        public int?    AnswerNumber { get; set; }
    }
}
