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
            dto.ClosedMessage = "טופס השיבוצים סגור כרגע. ניתן לפנות למרצה לפרטים.";
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
            dto.ClosedMessage = $"טופס השיבוצים יפתח בתאריך {opensAt.ToLocalTime():dd/MM/yyyy HH:mm}.";
            dto.Status        = FormStatuses.Draft;
            return dto;
        }

        if (DateTime.TryParse(form.ClosesAt, out var closesAt) && nowUtc > closesAt.ToUniversalTime())
        {
            dto.CanSubmit     = false;
            dto.ClosedReason  = "after-close";
            dto.ClosedMessage = $"טופס השיבוצים נסגר בתאריך {closesAt.ToLocalTime():dd/MM/yyyy HH:mm}.";
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
