using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Data;

// ─────────────────────────────────────────────────────────────────────────────
//  FormsRepository — small helper for the form-builder system.
//
//  Lives outside the controllers so that AssignmentController and
//  FormsController can share:
//    · auto-creation of the AssignmentForm + canonical 3 blocks
//    · the submission-window gate ("can the student submit right now?")
// ─────────────────────────────────────────────────────────────────────────────

public static class FormsRepository
{
    public const string AssignmentFormType = "AssignmentForm";

    /// <summary>Looks up the AssignmentForm for a year. Returns null if none.</summary>
    public static async Task<AssignmentFormRow?> GetAssignmentFormAsync(DbRepository db, int academicYearId)
    {
        const string sql = @"
            SELECT  Id,
                    AcademicYearId,
                    Name,
                    COALESCE(Instructions, '') AS Instructions,
                    IsOpen,
                    OpensAt,
                    ClosesAt,
                    AllowEditAfterSubmit,
                    Status
            FROM    Forms
            WHERE   AcademicYearId = @YearId AND FormType = @Type
            LIMIT   1";

        var rows = await db.GetRecordsAsync<AssignmentFormRow>(
            sql, new { YearId = academicYearId, Type = AssignmentFormType });
        return rows?.FirstOrDefault();
    }

    /// <summary>Idempotently creates the AssignmentForm + canonical blocks for a year.</summary>
    public static async Task<AssignmentFormRow?> EnsureAssignmentFormAsync(DbRepository db, int academicYearId)
    {
        if (academicYearId <= 0) return null;

        var existing = await GetAssignmentFormAsync(db, academicYearId);
        if (existing is not null) return existing;

        int newId = await db.InsertReturnIdAsync(@"
            INSERT INTO Forms
                (AcademicYearId, Name, FormType, Instructions, IsOpen, OpensAt, ClosesAt,
                 AllowEditAfterSubmit, Status)
            VALUES
                (@YearId, @Name, @Type, '', 0, NULL, NULL, 1, 'Draft')",
            new
            {
                YearId = academicYearId,
                Name   = "טופס שיבוץ פרויקט",
                Type   = AssignmentFormType
            });

        if (newId == 0) return null;

        await SeedAssignmentBlocksAsync(db, newId);
        return await GetAssignmentFormAsync(db, academicYearId);
    }

    /// <summary>Inserts the 3 canonical assignment-form blocks if missing.</summary>
    public static async Task SeedAssignmentBlocksAsync(DbRepository db, int formId)
    {
        // Strengths multi-choice
        if (!await BlockKeyExistsAsync(db, formId, FormBlockKeys.Strengths))
        {
            int strengthsId = await db.InsertReturnIdAsync(@"
                INSERT INTO FormBlocks (FormId, BlockType, BlockKey, Title, HelperText, IsRequired, SortOrder)
                VALUES (@FormId, 'MultiChoice', @Key, 'נקודות החוזק שלך',
                        'בחרו את התחומים שבהם אתם חזקים — נחשב לציון ההתאמה לפרויקטים', 1, 1)",
                new { FormId = formId, Key = FormBlockKeys.Strengths });

            if (strengthsId > 0)
            {
                var defaults = new (string Value, string Label, int Order)[]
                {
                    ("Design",            "עיצוב",         1),
                    ("Content",           "תוכן",          2),
                    ("Technology",        "טכנולוגיה",     3),
                    ("ProjectManagement", "ניהול פרויקט",  4),
                };

                foreach (var d in defaults)
                {
                    await db.SaveDataAsync(@"
                        INSERT INTO FormBlockOptions (FormBlockId, OptionValue, OptionLabel, SortOrder)
                        VALUES (@BlockId, @Value, @Label, @Order)",
                        new { BlockId = strengthsId, d.Value, d.Label, d.Order });
                }
            }
        }

        // Project preferences ranking — no static options (live catalog).
        if (!await BlockKeyExistsAsync(db, formId, FormBlockKeys.ProjectPreferences))
        {
            await db.InsertReturnIdAsync(@"
                INSERT INTO FormBlocks (FormId, BlockType, BlockKey, Title, HelperText, IsRequired, SortOrder)
                VALUES (@FormId, 'Ranking', @Key, 'דירוג העדפות פרויקט',
                        'דרגו שלושה פרויקטים מהקטלוג לפי סדר העדפה', 1, 2)",
                new { FormId = formId, Key = FormBlockKeys.ProjectPreferences });
        }

        // Notes open text
        if (!await BlockKeyExistsAsync(db, formId, FormBlockKeys.Notes))
        {
            await db.InsertReturnIdAsync(@"
                INSERT INTO FormBlocks (FormId, BlockType, BlockKey, Title, HelperText, IsRequired, SortOrder)
                VALUES (@FormId, 'OpenText', @Key, 'הערות נוספות',
                        'מידע נוסף שתרצו לשתף עם המרצים', 0, 3)",
                new { FormId = formId, Key = FormBlockKeys.Notes });
        }
    }

    /// <summary>
    /// Evaluates the submission gate. When a form row is missing (legacy state),
    /// returns "open with no constraints" so existing flows keep working.
    /// </summary>
    public static AssignmentFormStatusDto EvaluateGate(AssignmentFormRow? form, bool hasExistingSubmission)
    {
        // Legacy fall-through — no form record yet.
        if (form is null)
        {
            return new AssignmentFormStatusDto
            {
                IsOpen               = true,
                Status               = FormStatuses.Open,
                AllowEditAfterSubmit = true,
                CanSubmit            = true
            };
        }

        return EvaluateGate(
            form.IsOpen, form.Status, form.OpensAt, form.ClosesAt,
            form.AllowEditAfterSubmit, form.Instructions, hasExistingSubmission);
    }

    /// <summary>
    /// The same window rules, expressed over plain values so that any form —
    /// not only the assignment form — is gated by one implementation.
    ///
    /// Kept as a single method on purpose: a second copy of "is this form open
    /// right now?" is how the admin's status chip and the student's submit
    /// button start disagreeing.
    /// </summary>
    public static AssignmentFormStatusDto EvaluateGate(
        bool    isOpen,
        string  status,
        string? opensAtRaw,
        string? closesAtRaw,
        bool    allowEditAfterSubmit,
        string  instructions,
        bool    hasExistingSubmission)
    {
        var form = new AssignmentFormRow
        {
            IsOpen               = isOpen,
            Status               = status ?? "",
            OpensAt              = opensAtRaw,
            ClosesAt             = closesAtRaw,
            AllowEditAfterSubmit = allowEditAfterSubmit,
            Instructions         = instructions ?? ""
        };

        var dto = new AssignmentFormStatusDto
        {
            IsOpen               = form.IsOpen,
            OpensAt              = form.OpensAt,
            ClosesAt             = form.ClosesAt,
            AllowEditAfterSubmit = form.AllowEditAfterSubmit,
            Instructions         = form.Instructions,
            Status               = form.Status
        };

        // Closed (manual) or draft → block.
        if (!form.IsOpen || string.Equals(form.Status, FormStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            dto.CanSubmit     = false;
            dto.ClosedReason  = "form-closed";
            dto.ClosedMessage = "הטופס סגור כרגע. ניתן לפנות למרצה לפרטים.";
            dto.Status        = string.Equals(form.Status, FormStatuses.Closed, StringComparison.OrdinalIgnoreCase)
                ? FormStatuses.Closed
                : FormStatuses.Draft;
            return dto;
        }

        var nowUtc = DateTime.UtcNow;

        if (DateTime.TryParse(form.OpensAt, out var opensAt) && nowUtc < opensAt.ToUniversalTime())
        {
            dto.CanSubmit     = false;
            dto.ClosedReason  = "before-open";
            dto.ClosedMessage = $"הטופס יפתח בתאריך {opensAt.ToLocalTime():dd/MM/yyyy HH:mm}.";
            dto.Status        = FormStatuses.Draft;
            return dto;
        }

        if (DateTime.TryParse(form.ClosesAt, out var closesAt) && nowUtc > closesAt.ToUniversalTime())
        {
            dto.CanSubmit     = false;
            dto.ClosedReason  = "after-close";
            dto.ClosedMessage = $"הטופס נסגר בתאריך {closesAt.ToLocalTime():dd/MM/yyyy HH:mm}.";
            dto.Status        = FormStatuses.Closed;
            return dto;
        }

        // Edit lock for already-submitted teams.
        if (hasExistingSubmission && !form.AllowEditAfterSubmit)
        {
            dto.CanSubmit     = false;
            dto.ClosedReason  = "edit-locked";
            dto.ClosedMessage = "הגשתם כבר את הטופס — לא ניתן לערוך לאחר ההגשה.";
            return dto;
        }

        dto.CanSubmit = true;
        dto.Status    = FormStatuses.Open;
        return dto;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Assignment-form layout + generic answers
    //
    //  The assignment form is a HYBRID: three system blocks whose answers are
    //  real business records, plus any number of ordinary admin-added
    //  questions whose answers are rows in FormAnswers. These helpers live
    //  here rather than in a controller because BOTH AssignmentController (the
    //  student's one submit action) and FormsController (the admin's editor)
    //  need the same split, and two copies of "which block is domain-backed?"
    //  is exactly how the two sides start disagreeing again.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits a form's blocks into the three domain-backed ones (matched by
    /// BlockKey) and everything else, which is generic.
    /// </summary>
    public static async Task<AssignmentFormLayoutDto> LoadAssignmentLayoutAsync(DbRepository db, int formId)
    {
        var layout = new AssignmentFormLayoutDto { FormId = formId };

        var blocks = await LoadBlocksAsync(db, formId);

        foreach (var b in blocks)
        {
            switch (b.BlockKey)
            {
                case FormBlockKeys.Strengths:
                    layout.Strengths = b;
                    break;

                case FormBlockKeys.ProjectPreferences:
                    // Never carries options: its choices are the live catalog
                    // and its answers are real Projects.Id values. Cleared
                    // defensively so nothing downstream can mistake a stray row
                    // for a selectable project.
                    b.Options = new List<FormBlockOptionDto>();
                    layout.Preferences = b;
                    break;

                case FormBlockKeys.Notes:
                    layout.Notes = b;
                    break;

                default:
                    // A block with any other key is unknown to the domain, so
                    // it is treated as generic rather than silently dropped.
                    layout.ExtraQuestions.Add(b);
                    break;
            }
        }

        return layout;
    }

    /// <summary>Loads a form's blocks with their options, in stored order.</summary>
    public static async Task<List<FormBlockDto>> LoadBlocksAsync(DbRepository db, int formId)
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

        var blocks = (await db.GetRecordsAsync<FormBlockDto>(blocksSql, new { Id = formId }))?.ToList()
                     ?? new List<FormBlockDto>();

        if (blocks.Count == 0) return blocks;

        const string optsSql = @"
            SELECT  Id, FormBlockId, OptionValue, OptionLabel, SortOrder
            FROM    FormBlockOptions
            WHERE   FormBlockId IN (SELECT Id FROM FormBlocks WHERE FormId = @Id)
            ORDER   BY FormBlockId, SortOrder, Id";

        var opts = (await db.GetRecordsAsync<FormBlockOptionDto>(optsSql, new { Id = formId }))?.ToList()
                   ?? new List<FormBlockOptionDto>();

        var byBlock = opts.GroupBy(o => o.FormBlockId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var b in blocks)
            if (byBlock.TryGetValue(b.Id, out var list)) b.Options = list;

        return blocks;
    }

    /// <summary>Reads back one respondent's generic answers for a form.</summary>
    public static async Task<List<FormAnswerInputDto>> LoadGenericAnswersAsync(
        DbRepository db, int formId, int userId)
    {
        var rows = (await db.GetRecordsAsync<GenericAnswerRow>(@"
            SELECT  a.FormBlockId, a.OptionValue, a.AnswerText, a.AnswerNumber
            FROM    FormAnswers a
            JOIN    FormSubmissions s ON s.Id = a.FormSubmissionId
            WHERE   s.FormId = @FormId AND s.UserId = @UserId
            ORDER   BY a.FormBlockId, a.SortOrder, a.Id",
            new { FormId = formId, UserId = userId }))?.ToList() ?? new List<GenericAnswerRow>();

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

    /// <summary>
    /// Persists answers to the admin-added questions of a form.
    ///
    /// Only the blocks passed in <paramref name="genericBlocks"/> are written,
    /// so a caller can never route a domain block's answer here by accident.
    /// The submission row is upserted per (form, user) and its answers are
    /// replaced wholesale, which is what makes edit-after-submit produce one
    /// row rather than a second visible submission.
    /// </summary>
    public static async Task SaveGenericAnswersAsync(
        DbRepository            db,
        int                     formId,
        int                     userId,
        List<FormBlockDto>      genericBlocks,
        List<FormAnswerInputDto> answers)
    {
        if (genericBlocks.Count == 0) return;

        var allowed = genericBlocks.ToDictionary(b => b.Id, b => b.BlockType);

        var existing = (await db.GetRecordsAsync<int>(
            "SELECT Id FROM FormSubmissions WHERE FormId = @FormId AND UserId = @UserId LIMIT 1",
            new { FormId = formId, UserId = userId }))?.FirstOrDefault() ?? 0;

        int submissionId;
        if (existing > 0)
        {
            submissionId = existing;
            await db.SaveDataAsync(
                "UPDATE FormSubmissions SET UpdatedAt = datetime('now') WHERE Id = @Id",
                new { Id = submissionId });
            await db.SaveDataAsync(
                "DELETE FROM FormAnswers WHERE FormSubmissionId = @Id",
                new { Id = submissionId });
        }
        else
        {
            submissionId = await db.InsertReturnIdAsync(@"
                INSERT INTO FormSubmissions (FormId, UserId, SubmittedAt, UpdatedAt)
                VALUES (@FormId, @UserId, datetime('now'), datetime('now'))",
                new { FormId = formId, UserId = userId });

            if (submissionId == 0) return;
        }

        foreach (var a in answers)
        {
            if (!allowed.TryGetValue(a.FormBlockId, out var type)) continue;   // not a generic block of this form
            if (FormBlockTypes.IsInformational(type)) continue;                // carries no answer

            if (FormBlockTypes.IsRating(type))
            {
                if (a.Number is null) continue;
                await InsertGenericAnswerAsync(db, submissionId, a.FormBlockId, null, null, a.Number, 0);
                continue;
            }

            if (FormBlockTypes.HasOptions(type))
            {
                int order = 0;
                foreach (var v in a.OptionValues.Where(v => !string.IsNullOrWhiteSpace(v)))
                    await InsertGenericAnswerAsync(db, submissionId, a.FormBlockId, v, null, null, order++);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(a.Text))
                await InsertGenericAnswerAsync(db, submissionId, a.FormBlockId, null, a.Text.Trim(), null, 0);
        }
    }

    /// <summary>True when a required generic block has no usable answer.</summary>
    public static bool IsGenericAnswerMissing(FormBlockDto block, FormAnswerInputDto? a)
    {
        if (a is null) return true;
        if (FormBlockTypes.IsRating(block.BlockType))   return a.Number is not > 0;
        if (FormBlockTypes.HasOptions(block.BlockType)) return !a.OptionValues.Any(v => !string.IsNullOrWhiteSpace(v));
        return string.IsNullOrWhiteSpace(a.Text);
    }

    private static async Task InsertGenericAnswerAsync(
        DbRepository db, int submissionId, int blockId,
        string? optionValue, string? text, int? number, int sortOrder) =>
        await db.SaveDataAsync(@"
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

    private sealed class GenericAnswerRow
    {
        public int     FormBlockId  { get; set; }
        public string? OptionValue  { get; set; }
        public string? AnswerText   { get; set; }
        public int?    AnswerNumber { get; set; }
    }

    private static async Task<bool> BlockKeyExistsAsync(DbRepository db, int formId, string key)
    {
        var rows = await db.GetRecordsAsync<int>(
            "SELECT 1 FROM FormBlocks WHERE FormId = @FormId AND BlockKey = @Key LIMIT 1",
            new { FormId = formId, Key = key });
        return rows is not null && rows.Any();
    }

    public sealed class AssignmentFormRow
    {
        public int     Id                   { get; set; }
        public int     AcademicYearId       { get; set; }
        public string  Name                 { get; set; } = "";
        public string  Instructions         { get; set; } = "";
        public bool    IsOpen               { get; set; }
        public string? OpensAt              { get; set; }
        public string? ClosesAt             { get; set; }
        public bool    AllowEditAfterSubmit { get; set; }
        public string  Status               { get; set; } = "";
    }
}
