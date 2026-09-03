using System.Text;
using AuthWithAdmin.Client.Services.UserImport;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services.ProjectImport;

/// <summary>
/// Header→field mapping, row validation and duplicate detection for the PROJECT
/// CATALOG import. The sibling of <see cref="UserImportModel"/>, built the same
/// way and for the same reason: it takes a parsed grid plus the catalog already
/// on the page and returns what WOULD happen. Pure — nothing here talks to the
/// network and nothing here writes.
///
/// <para>The file reader itself is NOT duplicated: <see cref="ImportFileParser"/>
/// knows nothing about users, so the projects wizard reads its CSV/XLSX through
/// the very same parser.</para>
///
/// <para>The wizard performs the plan through ICatalogService.CreateProject… /
/// UpdateProject… — the SAME two calls the catalog's own add/edit form makes, so
/// every server-side rule (Validate, the FK checks, the duplicate-number check)
/// applies unchanged and no endpoint was added for this feature.</para>
/// </summary>
public static class ProjectImportModel
{
    // ─────────────────────────────────────────────────────────────────────
    //  Destination fields
    //
    //  EVERY MEMBER IS A REAL COLUMN THAT SaveCatalogProjectRequest CAN
    //  WRITE. Two catalog concepts are deliberately NOT here:
    //
    //  • SourceType. The vocabulary the catalog renders is "Manual" /
    //    "Airtable", and neither describes a spreadsheet. A create is filed
    //    as Manual (what the manual add form sends); an update carries the
    //    project's EXISTING source forward untouched, so re-importing an
    //    Airtable-synced project never re-labels it.
    //  • Team / assignment. TeamId is set by the assignment flow, and no
    //    catalog endpoint exposes it. A "צוות" column is annotated, not
    //    guessed onto anything.
    // ─────────────────────────────────────────────────────────────────────
    public enum Field
    {
        Ignore,
        ProjectNumber,
        Title,
        ProjectType,
        AcademicYear,
        Description,
        Goals,
        TargetAudience,
        OrganizationName,
        ContactPerson,
        ContactRole,
        Priority,
        Status,
        InternalNotes,
    }

    public static string FieldLabel(Field f) => f switch
    {
        Field.ProjectNumber    => "מספר פרויקט",
        Field.Title            => "שם הפרויקט",
        Field.ProjectType      => "סוג פרויקט",
        Field.AcademicYear     => "מחזור אקדמי",
        Field.Description      => "תיאור / צורך",
        Field.Goals            => "מטרות הפרויקט",
        Field.TargetAudience   => "קהל יעד",
        Field.OrganizationName => "שם ארגון",
        Field.ContactPerson    => "איש קשר",
        Field.ContactRole      => "תפקיד איש קשר",
        Field.Priority         => "עדיפות",
        Field.Status           => "זמינות",
        Field.InternalNotes    => "הערות פנימיות",
        _                      => "אל תייבא שדה זה",
    };

    /// <summary>Order the destination dropdown is offered in — required fields
    /// first, then content, then organization, then the internal values.</summary>
    public static readonly Field[] SelectableFields =
    {
        Field.Ignore,
        Field.ProjectNumber, Field.Title, Field.ProjectType, Field.AcademicYear,
        Field.Description, Field.Goals, Field.TargetAudience,
        Field.OrganizationName, Field.ContactPerson, Field.ContactRole,
        Field.Priority, Field.Status, Field.InternalNotes,
    };

    // ─────────────────────────────────────────────────────────────────────
    //  Header matching
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Case-folds and drops every separator and decoration a header
    /// collects, so "Project_Number*" and "  project number  " both become
    /// "projectnumber". Same rule as the users import, restated here rather
    /// than reached across namespaces for one helper.</summary>
    public static string Normalize(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return "";

        var sb = new StringBuilder(header.Length);
        foreach (var ch in header.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    private static readonly Dictionary<string, Field> Aliases = BuildAliases();

    private static Dictionary<string, Field> BuildAliases()
    {
        var map = new Dictionary<string, Field>(StringComparer.Ordinal);

        void Add(Field field, params string[] names)
        {
            foreach (var n in names)
            {
                var key = Normalize(n);
                if (key.Length > 0) map[key] = field;
            }
        }

        Add(Field.ProjectNumber,
            "מספר פרויקט", "מספר", "מס פרויקט", "מס'", "מזהה פרויקט",
            "project number", "projectnumber", "project no", "number", "no", "num", "code", "project code");

        Add(Field.Title,
            "שם הפרויקט", "שם פרויקט", "שם", "נושא", "נושא הפרויקט", "כותרת",
            "title", "project title", "project name", "name", "subject", "topic");

        Add(Field.ProjectType,
            "סוג פרויקט", "סוג", "סוג הפרויקט", "מסלול",
            "type", "project type", "projecttype", "track", "category");

        Add(Field.AcademicYear,
            "מחזור", "מחזור אקדמי", "שנה", "שנת לימודים", "שנתון",
            "cycle", "academic year", "academicyear", "academic cycle", "year", "cohort");

        Add(Field.Description,
            "תיאור", "תאור", "צורך", "תיאור הפרויקט", "תיאור צורך", "רקע",
            "description", "need", "summary", "background", "details");

        Add(Field.Goals,
            "מטרות", "מטרות הפרויקט", "יעדים", "תפוקות",
            "goals", "objectives", "outcomes", "deliverables");

        Add(Field.TargetAudience,
            "קהל יעד", "קהל היעד", "משתמשי קצה",
            "target audience", "targetaudience", "audience", "end users");

        Add(Field.OrganizationName,
            "ארגון", "שם ארגון", "שם הארגון", "חברה", "לקוח", "גוף",
            "organization", "organisation", "organization name", "company", "client", "customer");

        Add(Field.ContactPerson,
            "איש קשר", "אשת קשר", "שם איש קשר", "נציג",
            "contact", "contact person", "contactperson", "contact name");

        Add(Field.ContactRole,
            "תפקיד איש קשר", "תפקיד", "תפקיד הנציג",
            "contact role", "contactrole", "role", "position", "job title");

        Add(Field.Priority,
            "עדיפות", "דחיפות", "רמת עדיפות",
            "priority", "urgency");

        Add(Field.Status,
            "זמינות", "סטטוס", "מצב", "זמין",
            "status", "availability", "available");

        Add(Field.InternalNotes,
            "הערות פנימיות", "הערות", "הערה", "הערות צוות",
            "internal notes", "internalnotes", "notes", "comments", "remarks");

        return map;
    }

    /// <summary>Headers that name a concept the catalog API does not expose.
    /// Recognised ONLY so the mapping stage can say why they were left out
    /// instead of leaving the admin to guess.</summary>
    private static readonly Dictionary<string, string> UnsupportedHeaders =
        new(StringComparer.Ordinal)
        {
            [Normalize("צוות")]        = "שיוך צוות נקבע במסך השיבוץ ואינו נחשף ב-API של הקטלוג, ולכן אינו ניתן לייבוא.",
            [Normalize("team")]        = "שיוך צוות נקבע במסך השיבוץ ואינו נחשף ב-API של הקטלוג, ולכן אינו ניתן לייבוא.",
            [Normalize("סטודנטים")]    = "שיוך סטודנטים נקבע במסך השיבוץ ואינו נחשף ב-API של הקטלוג, ולכן אינו ניתן לייבוא.",
            [Normalize("students")]    = "שיוך סטודנטים נקבע במסך השיבוץ ואינו נחשף ב-API של הקטלוג, ולכן אינו ניתן לייבוא.",
            [Normalize("מנחה")]        = "אין שדה מנחה בקטלוג — שיוך מנחים נעשה במסך הפרויקטים הפעילים.",
            [Normalize("mentor")]      = "אין שדה מנחה בקטלוג — שיוך מנחים נעשה במסך הפרויקטים הפעילים.",
            [Normalize("מקור")]        = "מקור הרשומה נקבע לפי אופן ההזנה (ידני / Airtable) ואינו נקבע מהקובץ.",
            [Normalize("source")]      = "מקור הרשומה נקבע לפי אופן ההזנה (ידני / Airtable) ואינו נקבע מהקובץ.",
            [Normalize("airtable record id")] = "מזהה Airtable נכתב רק על-ידי סנכרון Airtable.",
            [Normalize("מזהה airtable")]      = "מזהה Airtable נכתב רק על-ידי סנכרון Airtable.",
            [Normalize("id")]          = "המזהה הפנימי אינו ניתן לייבוא — פרויקטים מזוהים לפי מספר הפרויקט.",
            [Normalize("תאריך יצירה")] = "תאריך היצירה נקבע על-ידי השרת ואינו ניתן לייבוא.",
            [Normalize("created at")]  = "תאריך היצירה נקבע על-ידי השרת ואינו ניתן לייבוא.",
        };

    /// <summary>One source column and where it is going.</summary>
    public sealed class ColumnMapping
    {
        public int    Index      { get; init; }
        public string Header     { get; init; } = "";
        public Field  Target     { get; set; }
        /// <summary>True when <see cref="Target"/> came from the alias table
        /// rather than from the admin. Reset the moment the admin changes it.</summary>
        public bool   AutoMapped { get; set; }
        /// <summary>Why this column could not be mapped, when the reason is
        /// known and worth saying out loud.</summary>
        public string? Note      { get; set; }
        /// <summary>The admin has set this column's destination by hand —
        /// including setting it to Ignore. Without it an explicit "do not
        /// import" is indistinguishable from a column nobody recognised.</summary>
        public bool   UserSet    { get; set; }
        /// <summary>A sample value from the first non-empty data row.</summary>
        public string? Sample    { get; init; }
    }

    /// <summary>Auto-maps what the alias table recognises and leaves the rest
    /// alone. A second column claiming an already-taken field is NOT
    /// auto-mapped — first match wins and the duplicate drops to manual.</summary>
    public static List<ColumnMapping> AutoMap(ImportFileParser.Grid grid)
    {
        var result = new List<ColumnMapping>();
        var taken  = new HashSet<Field>();

        for (var i = 0; i < grid.Headers.Count; i++)
        {
            var header = grid.Headers[i];
            var key    = Normalize(header);
            var sample = grid.Rows
                .Select(r => i < r.Count ? r[i] : "")
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            var mapping = new ColumnMapping { Index = i, Header = header, Sample = sample };

            if (Aliases.TryGetValue(key, out var field) && !taken.Contains(field))
            {
                mapping.Target     = field;
                mapping.AutoMapped = true;
                taken.Add(field);
            }
            else
            {
                mapping.Target     = Field.Ignore;
                mapping.AutoMapped = false;

                if (UnsupportedHeaders.TryGetValue(key, out var note))
                    mapping.Note = note;
                else if (Aliases.ContainsKey(key))
                    mapping.Note = "עמודה אחרת כבר ממופה לשדה זה.";
            }

            result.Add(mapping);
        }

        return result;
    }

    /// <summary>A destination claimed by more than one column. The import
    /// writes one value per field, so this has to be resolved first.</summary>
    public static IReadOnlyList<Field> ConflictingTargets(IEnumerable<ColumnMapping> mappings) =>
        mappings.Where(m => m.Target != Field.Ignore)
                .GroupBy(m => m.Target)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

    /// <summary>What still has to be mapped before a row can be built.
    ///
    /// <para>THE THREE THE SERVER REFUSES TO DEFAULT. CatalogController.Validate
    /// rejects a save without a positive ProjectNumber, a Title and a
    /// ProjectTypeId, so all three are required here rather than guessed.
    /// AcademicYear is the deliberate exception: it is equally required by the
    /// server, but the system designates one current cycle, so an unmapped
    /// column falls back to it and the review step states which.</para></summary>
    public static IReadOnlyList<string> MissingRequirements(IEnumerable<ColumnMapping> mappings)
    {
        var set     = mappings.Select(m => m.Target).ToHashSet();
        var missing = new List<string>();

        if (!set.Contains(Field.ProjectNumber))
            missing.Add("מספר פרויקט — שדה חובה, ולפיו המערכת מזהה פרויקט קיים");

        if (!set.Contains(Field.Title))
            missing.Add("שם הפרויקט — שדה חובה");

        if (!set.Contains(Field.ProjectType))
            missing.Add("סוג פרויקט — שדה חובה, ואין לו ערך ברירת מחדל שניתן להסיק");

        return missing;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Value resolution
    //
    //  Types and cycles are LOOKUP TABLES, not free text: both are foreign
    //  keys, and the server refuses an id that does not exist. So a value
    //  that does not resolve is an error on the row, never a new row in a
    //  lookup table the import has no business creating.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Resolves a project-type cell against the types the system
    /// actually has. Matches the stored name first, then the Hebrew names the
    /// UI uses for the two seeded types.</summary>
    public static ProjectTypeOptionDto? ResolveType(string? raw,
                                                    IReadOnlyList<ProjectTypeOptionDto> types)
    {
        var key = Normalize(raw);
        if (key.Length == 0) return null;

        var direct = types.FirstOrDefault(t => Normalize(t.Name) == key);
        if (direct is not null) return direct;

        // The catalog stores the English names ("Technological" /
        // "Methodological") but every screen and every Hebrew roster says
        // טכנולוגי / מתודולוגי. Both spellings have to land on the same row.
        var english = key switch
        {
            "טכנולוגי" or "טכנולוגית" or "פרויקטטכנולוגי" or "tech" => "technological",
            "מתודולוגי" or "מתודולוגית" or "פרויקטמתודולוגי"        => "methodological",
            _                                                       => null,
        };

        return english is null
            ? null
            : types.FirstOrDefault(t => Normalize(t.Name) == english);
    }

    /// <summary>Resolves an academic-cycle cell against the cycles that exist.
    /// "2025-2026", "2025 2026" and "תשפ״ו" only match if that is what the
    /// AcademicYears row is actually called — nothing is invented.</summary>
    public static AcademicYearDto? ResolveYear(string? raw,
                                               IReadOnlyList<AcademicYearDto> years)
    {
        var key = Normalize(raw);
        if (key.Length == 0) return null;
        return years.FirstOrDefault(y => Normalize(y.Name) == key);
    }

    /// <summary>The catalog's three priorities. NOTE the middle one is
    /// "Medium" here — the ACTIVE-projects screen calls its middle rung
    /// "Normal", and mixing the two vocabularies is how a value that renders
    /// nowhere gets written.</summary>
    public static string? ResolvePriority(string? raw)
    {
        var key = Normalize(raw);
        return key switch
        {
            ""                                             => "",   // not stated
            "low" or "נמוכה" or "נמוך"                      => "Low",
            "medium" or "normal" or "בינונית" or "בינוני" or "רגילה" => "Medium",
            "high" or "urgent" or "גבוהה" or "גבוה" or "דחוף"        => "High",
            _                                              => null, // stated, unrecognised
        };
    }

    /// <summary>Availability. The catalog's own vocabulary is exactly two
    /// values; a project can carry others ("Active", "InProgress") once it is
    /// assigned, but those are written by the assignment flow and are not
    /// something a spreadsheet may set.</summary>
    public static string? ResolveStatus(string? raw)
    {
        var key = Normalize(raw);
        return key switch
        {
            ""                                                       => "",
            "available" or "זמין" or "זמינלשיוך" or "כן" or "yes" or "1" => "Available",
            "unavailable" or "לאזמין" or "לא" or "no" or "0"           => "Unavailable",
            _                                                        => null,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Rows
    // ─────────────────────────────────────────────────────────────────────

    public enum RowState
    {
        /// <summary>Passes validation and no project carries this number.</summary>
        New,
        /// <summary>Passes validation, but this project number already exists.</summary>
        Duplicate,
        /// <summary>Cannot be imported as it stands.</summary>
        Invalid,
    }

    public enum DuplicateAction
    {
        /// <summary>Keep the existing project, fill only the fields it is missing.</summary>
        Merge,
        /// <summary>Overwrite the existing project's fields with the file's values.</summary>
        Replace,
        /// <summary>Change nothing; the file row is dropped.</summary>
        Skip,
    }

    public sealed class Row
    {
        public int LineNumber { get; init; }     // 1-based, counting the header as line 1

        public int    ProjectNumber { get; set; }
        public string Title         { get; set; } = "";

        public int    ProjectTypeId   { get; set; }
        public string ProjectTypeName { get; set; } = "";

        public int    AcademicYearId   { get; set; }
        public string AcademicYearName { get; set; } = "";
        /// <summary>True when the cycle came from the current-cycle fallback
        /// rather than from the file. Counted on the review step.</summary>
        public bool   CycleDefaulted   { get; set; }

        public string Description      { get; set; } = "";
        public string Goals            { get; set; } = "";
        public string TargetAudience   { get; set; } = "";
        public string OrganizationName { get; set; } = "";
        public string ContactPerson    { get; set; } = "";
        public string ContactRole      { get; set; } = "";
        public string InternalNotes    { get; set; } = "";
        /// <summary>"" when the file did not state one.</summary>
        public string Priority         { get; set; } = "";
        /// <summary>"" when the file did not state one.</summary>
        public string Status           { get; set; } = "";

        public RowState     State   { get; set; }
        public List<string> Errors  { get; } = new();
        /// <summary>Non-blocking remarks — shown, but never stop an import.</summary>
        public List<string> Notices { get; } = new();

        /// <summary>The catalog entry this row collides with, when State is
        /// Duplicate. The LIST row — enough to identify it in the table.</summary>
        public CatalogProjectListDto? Existing { get; set; }

        /// <summary>The full record behind <see cref="Existing"/>, fetched when
        /// the duplicates stage is reached. Required because PUT api/catalog/{id}
        /// writes the WHOLE record — an update built from the list row alone
        /// would blank Description, Goals, notes and contact on every merge.</summary>
        public CatalogProjectDetailDto? ExistingDetail { get; set; }

        public DuplicateAction Action { get; set; } = DuplicateAction.Merge;
    }

    /// <summary>Builds and validates every row against the current mapping, the
    /// catalog already loaded on the page, and the two lookup tables.</summary>
    public static List<Row> BuildRows(ImportFileParser.Grid grid,
                                      IReadOnlyList<ColumnMapping> mappings,
                                      IReadOnlyList<CatalogProjectListDto> existingProjects,
                                      IReadOnlyList<ProjectTypeOptionDto> types,
                                      IReadOnlyList<AcademicYearDto> years)
    {
        int? IndexOf(Field f) => mappings.FirstOrDefault(m => m.Target == f)?.Index;

        var iNumber  = IndexOf(Field.ProjectNumber);
        var iTitle   = IndexOf(Field.Title);
        var iType    = IndexOf(Field.ProjectType);
        var iYear    = IndexOf(Field.AcademicYear);
        var iDesc    = IndexOf(Field.Description);
        var iGoals   = IndexOf(Field.Goals);
        var iAudience = IndexOf(Field.TargetAudience);
        var iOrg     = IndexOf(Field.OrganizationName);
        var iContact = IndexOf(Field.ContactPerson);
        var iRole    = IndexOf(Field.ContactRole);
        var iPriority = IndexOf(Field.Priority);
        var iStatus  = IndexOf(Field.Status);
        var iNotes   = IndexOf(Field.InternalNotes);

        // ProjectNumber is the Projects table's UNIQUE key and the only handle
        // this screen has for deciding whether a row is new. Matching on the
        // title instead would merge two different projects that happen to be
        // called the same thing.
        var byNumber = new Dictionary<int, CatalogProjectListDto>();
        foreach (var p in existingProjects) byNumber[p.ProjectNumber] = p;

        var currentYear = years.FirstOrDefault(y => y.IsCurrent) ?? years.FirstOrDefault();

        var seenInFile = new Dictionary<int, int>();
        var rows = new List<Row>();

        for (var r = 0; r < grid.Rows.Count; r++)
        {
            var src = grid.Rows[r];
            string Cell(int? i) => i is { } idx && idx < src.Count ? (src[idx] ?? "").Trim() : "";

            var row = new Row { LineNumber = r + 2 };   // +2: 1-based, header is line 1

            // ── Project number ──────────────────────────────────────────
            var rawNumber = Cell(iNumber);
            if (rawNumber.Length == 0)
            {
                row.Errors.Add("חסר מספר פרויקט");
            }
            // A number that arrived from XLSX as "101.0" is still 101. The
            // parser does not read number formats, so the decimal tail is
            // trimmed here rather than rejected as non-numeric.
            else if (!TryParseProjectNumber(rawNumber, out var number))
            {
                row.Errors.Add($"מספר פרויקט לא תקין: \"{Isolate(rawNumber)}\"");
            }
            else if (number <= 0)
            {
                row.Errors.Add("מספר פרויקט חייב להיות חיובי");
            }
            else if (seenInFile.TryGetValue(number, out var firstLine))
            {
                row.Errors.Add($"מספר הפרויקט מופיע כבר בשורה {firstLine} בקובץ");
                row.ProjectNumber = number;
            }
            else
            {
                row.ProjectNumber   = number;
                seenInFile[number]  = row.LineNumber;
            }

            // ── Title ───────────────────────────────────────────────────
            row.Title = Cell(iTitle);
            if (row.Title.Length == 0) row.Errors.Add("חסר שם פרויקט");

            // ── Type ────────────────────────────────────────────────────
            var rawType = Cell(iType);
            var type    = ResolveType(rawType, types);
            if (rawType.Length == 0)
            {
                row.Errors.Add("חסר סוג פרויקט");
            }
            else if (type is null)
            {
                var known = string.Join(" / ", types.Select(t => t.Name));
                row.Errors.Add($"סוג פרויקט לא מוכר: \"{Isolate(rawType)}\" — הסוגים הקיימים הם {Isolate(known)}");
            }
            else
            {
                row.ProjectTypeId   = type.Id;
                row.ProjectTypeName = type.Name;
            }

            // ── Cycle ───────────────────────────────────────────────────
            var rawYear = Cell(iYear);
            if (rawYear.Length > 0)
            {
                var year = ResolveYear(rawYear, years);
                if (year is null)
                {
                    var known = string.Join(" / ", years.Select(y => y.Name));
                    row.Errors.Add($"מחזור לא מוכר: \"{Isolate(rawYear)}\" — המחזורים הקיימים הם {Isolate(known)}");
                }
                else
                {
                    row.AcademicYearId   = year.Id;
                    row.AcademicYearName = year.Name;
                }
            }
            else if (currentYear is not null)
            {
                row.AcademicYearId   = currentYear.Id;
                row.AcademicYearName = currentYear.Name;
                row.CycleDefaulted   = true;
                // Only worth a per-row remark when the column EXISTS and this
                // row left it blank; a file with no cycle column at all is
                // reported once, on the review step.
                if (iYear is not null)
                    row.Notices.Add($"אין מחזור בשורה — ישויך למחזור הנוכחי ({Isolate(currentYear.Name)})");
            }
            else
            {
                row.Errors.Add("אין מחזור בשורה ולא הוגדר מחזור נוכחי במערכת");
            }

            // ── Free-text content ───────────────────────────────────────
            row.Description      = Cell(iDesc);
            row.Goals            = Cell(iGoals);
            row.TargetAudience   = Cell(iAudience);
            row.OrganizationName = Cell(iOrg);
            row.ContactPerson    = Cell(iContact);
            row.ContactRole      = Cell(iRole);
            row.InternalNotes    = Cell(iNotes);

            // ── Priority ────────────────────────────────────────────────
            var rawPriority = Cell(iPriority);
            var priority    = ResolvePriority(rawPriority);
            if (priority is null)
            {
                // Not an error: priority is optional everywhere in the catalog,
                // so an unreadable value is dropped and said out loud.
                row.Notices.Add($"עדיפות לא מוכרת: \"{Isolate(rawPriority)}\" — תיובא ללא עדיפות");
                row.Priority = "";
            }
            else
            {
                row.Priority = priority;
            }

            // ── Availability ────────────────────────────────────────────
            var rawStatus = Cell(iStatus);
            var status    = ResolveStatus(rawStatus);
            if (status is null)
            {
                row.Notices.Add($"ערך זמינות לא מוכר: \"{Isolate(rawStatus)}\" — תיובא כזמינה לשיוך");
                row.Status = "";
            }
            else
            {
                row.Status = status;
            }

            // ── Outcome ─────────────────────────────────────────────────
            if (row.Errors.Count > 0)
            {
                row.State = RowState.Invalid;
            }
            else if (byNumber.TryGetValue(row.ProjectNumber, out var existing))
            {
                row.State    = RowState.Duplicate;
                row.Existing = existing;
                row.Action   = DuplicateAction.Merge;

                // Two facts worth stating before the admin picks an action —
                // both are about what the EXISTING row is, not about the file.
                if (existing.IsAssigned)
                    row.Notices.Add("הפרויקט כבר משויך לצוות — עריכה שלו מהקטלוג חסומה במסך, והייבוא כן יכתוב אליו");
                if (existing.SourceType == "Airtable")
                    row.Notices.Add("הפרויקט מסונכרן מ-Airtable — סנכרון עתידי עשוי לדרוס את ערכי הקובץ");
            }
            else
            {
                row.State = RowState.New;

                // A TITLE collision is not a duplicate. Titles are not unique
                // in Projects and nothing may be merged on them, so this is
                // said out loud and the row still creates a new project.
                var titleTwin = existingProjects.FirstOrDefault(p =>
                    string.Equals(p.Title.Trim(), row.Title, StringComparison.OrdinalIgnoreCase));
                if (titleTwin is not null)
                    row.Notices.Add($"קיים פרויקט בשם זהה (מספר {titleTwin.ProjectNumber}) — ייווצר פרויקט חדש");
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Accepts "101", " 101 " and the "101.0" an XLSX numeric cell
    /// produces. Rejects everything else rather than silently taking the
    /// leading digits of "P-101".</summary>
    private static bool TryParseProjectNumber(string raw, out int value)
    {
        var text = raw.Trim();
        var dot  = text.IndexOf('.');
        if (dot >= 0 && text[(dot + 1)..].All(c => c == '0'))
            text = text[..dot];

        return int.TryParse(text, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Wraps a value in FIRST STRONG ISOLATE / POP DIRECTIONAL ISOLATE
    /// — the plain-text equivalent of &lt;bdi&gt;. Without it a Latin value
    /// dropped into a Hebrew sentence drags the surrounding quotes and
    /// punctuation to the wrong side. These strings are rendered as text, not
    /// markup, so the isolation has to travel in the string.</summary>
    private static string Isolate(string? value) => "⁨" + (value ?? "") + "⁩";

    // ─────────────────────────────────────────────────────────────────────
    //  Duplicate diffing
    //
    //  Against the DETAIL record, not the list row: PUT api/catalog/{id}
    //  writes every column with a plain `= @X` (only Status and SourceType
    //  are COALESCEd), so an update built from the list row alone would blank
    //  Description, Goals, TargetAudience, the contact block, the internal
    //  notes and the priority of every project it touched.
    // ─────────────────────────────────────────────────────────────────────

    public sealed record FieldDiff(string Label, string ExistingValue, string ImportedValue,
                                   bool ExistingIsEmpty, bool Differs);

    public static List<FieldDiff> Diff(CatalogProjectDetailDto existing, Row row)
    {
        var list = new List<FieldDiff>();

        void Add(string label, string? existingValue, string importedValue)
        {
            var ex      = (existingValue ?? "").Trim();
            var im      = (importedValue ?? "").Trim();
            var empty   = ex.Length == 0;
            var differs = im.Length > 0 && !string.Equals(ex, im, StringComparison.Ordinal);
            list.Add(new FieldDiff(label, ex, im, empty, differs));
        }

        Add(FieldLabel(Field.Title),            existing.Title,            row.Title);
        Add(FieldLabel(Field.ProjectType),      existing.ProjectType,      row.ProjectTypeName);
        Add(FieldLabel(Field.AcademicYear),     existing.AcademicYear,     row.AcademicYearName);
        Add(FieldLabel(Field.Description),      existing.Description,      row.Description);
        Add(FieldLabel(Field.Goals),            existing.Goals,            row.Goals);
        Add(FieldLabel(Field.TargetAudience),   existing.TargetAudience,   row.TargetAudience);
        Add(FieldLabel(Field.OrganizationName), existing.OrganizationName, row.OrganizationName);
        Add(FieldLabel(Field.ContactPerson),    existing.ContactPerson,    row.ContactPerson);
        Add(FieldLabel(Field.ContactRole),      existing.ContactRole,      row.ContactRole);
        Add(FieldLabel(Field.Priority),         PriorityLabel(existing.Priority), PriorityLabel(row.Priority));
        Add(FieldLabel(Field.Status),           StatusLabel(existing.Status),     StatusLabel(row.Status));
        Add(FieldLabel(Field.InternalNotes),    existing.InternalNotes,    row.InternalNotes);

        return list;
    }

    /// <summary>Fields a merge would fill — existing is empty AND the file has
    /// a value. A merge never touches a populated field, which is the whole
    /// difference between it and a replace.</summary>
    public static IReadOnlyList<FieldDiff> MergeFills(CatalogProjectDetailDto existing, Row row) =>
        Diff(existing, row).Where(d => d.ExistingIsEmpty && d.ImportedValue.Length > 0).ToList();

    /// <summary>Fields a replace would overwrite with a DIFFERENT value. An
    /// empty imported value is not an overwrite — the request falls back to
    /// the existing value, because the endpoint requires a title, a type and a
    /// cycle on every call.</summary>
    public static IReadOnlyList<FieldDiff> ReplaceOverwrites(CatalogProjectDetailDto existing, Row row) =>
        Diff(existing, row).Where(d => d.Differs && !d.ExistingIsEmpty).ToList();

    /// <summary>The body for a NEW project.
    ///
    /// <para>SourceType is "Manual" — the value the catalog's own add form
    /// sends, and the only non-Airtable value the source badge and the source
    /// filter know how to render. A third value invented for imports would
    /// draw as an unstyled chip on every screen that reads it.</para></summary>
    public static SaveCatalogProjectRequest BuildCreate(Row row) => new()
    {
        ProjectNumber    = row.ProjectNumber,
        Title            = row.Title.Trim(),
        ProjectTypeId    = row.ProjectTypeId,
        AcademicYearId   = row.AcademicYearId,
        Description      = Nz(row.Description),
        Goals            = Nz(row.Goals),
        TargetAudience   = Nz(row.TargetAudience),
        OrganizationName = Nz(row.OrganizationName),
        ContactPerson    = Nz(row.ContactPerson),
        ContactRole      = Nz(row.ContactRole),
        InternalNotes    = Nz(row.InternalNotes),
        Priority         = Nz(row.Priority),
        // The server COALESCEs a null to 'Available' on insert; sending the
        // literal keeps the two paths reading the same.
        Status           = string.IsNullOrEmpty(row.Status) ? "Available" : row.Status,
        SourceType       = "Manual",
    };

    /// <summary>The body for a duplicate. Merge keeps every populated existing
    /// value; Replace prefers the file but still falls back to the existing
    /// value where the file is silent.
    ///
    /// <para>SourceType is carried over from the existing record and never
    /// recomputed: BuildParams on the server turns a blank SourceType into
    /// "Manual", so omitting it would silently re-label every Airtable-synced
    /// project this import touched.</para></summary>
    public static SaveCatalogProjectRequest BuildUpdate(CatalogProjectDetailDto existing,
                                                        Row row, DuplicateAction action)
    {
        string? Pick(string? existingValue, string importedValue)
        {
            var ex = (existingValue ?? "").Trim();
            var im = (importedValue ?? "").Trim();

            var chosen = action == DuplicateAction.Replace
                ? (im.Length > 0 ? im : ex)
                : (ex.Length > 0 ? ex : im);

            return chosen.Length == 0 ? null : chosen;
        }

        // The two foreign keys cannot fall back to an empty string, so they are
        // picked by id rather than through Pick().
        var typeId = action == DuplicateAction.Replace && row.ProjectTypeId > 0
            ? row.ProjectTypeId
            : existing.ProjectTypeId;

        var yearId = action == DuplicateAction.Replace && row.AcademicYearId > 0 && !row.CycleDefaulted
            ? row.AcademicYearId
            : existing.AcademicYearId;

        return new SaveCatalogProjectRequest
        {
            ProjectNumber    = existing.ProjectNumber,   // the match key — identical by definition
            Title            = Pick(existing.Title, row.Title) ?? existing.Title,
            ProjectTypeId    = typeId,
            AcademicYearId   = yearId,
            Description      = Pick(existing.Description,      row.Description),
            Goals            = Pick(existing.Goals,            row.Goals),
            TargetAudience   = Pick(existing.TargetAudience,   row.TargetAudience),
            OrganizationName = Pick(existing.OrganizationName, row.OrganizationName),
            ContactPerson    = Pick(existing.ContactPerson,    row.ContactPerson),
            ContactRole      = Pick(existing.ContactRole,      row.ContactRole),
            InternalNotes    = Pick(existing.InternalNotes,    row.InternalNotes),
            Priority         = Pick(existing.Priority,         row.Priority),
            Status           = Pick(existing.Status,           row.Status) ?? existing.Status,
            SourceType       = existing.SourceType,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Labels shared with the wizard
    // ─────────────────────────────────────────────────────────────────────

    public static string PriorityLabel(string? priority) => priority switch
    {
        "Low"    => "נמוכה",
        "Medium" => "בינונית",
        "High"   => "גבוהה",
        null or "" => "",
        _        => priority,
    };

    /// <summary>Every status a project can actually hold, not only the two the
    /// catalog writes. A duplicate's diff shows the EXISTING value beside the
    /// file's, and an assigned project carries "Active" / "InProgress" from the
    /// assignment flow — printing those raw put untranslated English in a
    /// column the table next to it renders as "פעיל". Kept in step with
    /// CatalogManagement.StatusLabel.</summary>
    public static string StatusLabel(string? status) => status switch
    {
        "Available"   => "זמין לשיוך",
        "Unavailable" => "לא זמין",
        "Active"      => "פעיל",
        "InProgress"  => "בתהליך",
        "Archived"    => "בארכיון",
        null or ""    => "",
        _             => status,
    };

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ─────────────────────────────────────────────────────────────────────
    //  Plan
    // ─────────────────────────────────────────────────────────────────────

    public sealed record Plan(int Create, int Merge, int Replace, int Skip, int Invalid)
    {
        public int Total => Create + Merge + Replace + Skip + Invalid;
        /// <summary>Nothing to do — the final action must not be offered.</summary>
        public bool IsNoop => Create + Merge + Replace == 0;
    }

    public static Plan BuildPlan(IEnumerable<Row> rows)
    {
        int create = 0, merge = 0, replace = 0, skip = 0, invalid = 0;

        foreach (var row in rows)
        {
            switch (row.State)
            {
                case RowState.Invalid: invalid++; break;
                case RowState.New:     create++;  break;
                case RowState.Duplicate:
                    switch (row.Action)
                    {
                        case DuplicateAction.Merge:   merge++;   break;
                        case DuplicateAction.Replace: replace++; break;
                        default:                      skip++;    break;
                    }
                    break;
            }
        }

        return new Plan(create, merge, replace, skip, invalid);
    }
}
