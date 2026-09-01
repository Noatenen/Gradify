using System.Text;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services.UserImport;

/// <summary>
/// Header→field mapping, row validation and duplicate detection for the user
/// import. Pure: it takes a parsed grid plus the users already in the system and
/// returns what would happen. Nothing here talks to the network, and nothing
/// here writes — the wizard performs the plan through the SAME
/// IAuthenticationService calls the single-user create/edit flows use.
/// </summary>
public static class UserImportModel
{
    // ─────────────────────────────────────────────────────────────────────
    //  Destination fields
    //
    //  EVERY MEMBER IS A REAL COLUMN ON `users`, with one deliberate
    //  exception: FullName. There is no full-name column — the table stores
    //  FirstName and LastName — but a single "שם מלא" column is the most
    //  common shape of a real roster, so it is offered as a TRANSFORM onto
    //  those two fields and is labelled as such in the UI.
    //
    //  There is NO Username member. The users table has no username column;
    //  identity is Email. A header that looks like a username is therefore
    //  left unmapped and annotated, never guessed onto Email.
    // ─────────────────────────────────────────────────────────────────────
    public enum Field
    {
        Ignore,
        FullName,
        FirstName,
        LastName,
        Email,
        Phone,
        Role,
        AcademicYear,
    }

    public static string FieldLabel(Field f) => f switch
    {
        Field.FullName     => "שם מלא (יפוצל לשם פרטי ומשפחה)",
        Field.FirstName    => "שם פרטי",
        Field.LastName     => "שם משפחה",
        Field.Email        => "אימייל",
        Field.Phone        => "טלפון",
        Field.Role         => "תפקיד",
        Field.AcademicYear => "מחזור",
        _                  => "אל תייבא שדה זה",
    };

    /// <summary>Order the destination dropdown is offered in.</summary>
    public static readonly Field[] SelectableFields =
    {
        Field.Ignore, Field.FullName, Field.FirstName, Field.LastName,
        Field.Email, Field.Phone, Field.Role, Field.AcademicYear,
    };

    // ─────────────────────────────────────────────────────────────────────
    //  Header matching
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Case-folds, strips the separators people actually use between
    /// words in a header (space, underscore, hyphen, dot, slash), and drops the
    /// decorations spreadsheets collect (trailing colon, asterisk, quotes,
    /// bracketed notes). "First_Name*" and "  first name  " both become
    /// "firstname".</summary>
    public static string Normalize(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return "";

        var sb = new StringBuilder(header.Length);
        foreach (var ch in header.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            // everything else — space _ - . / : * " ' ( ) — is a separator and
            // is simply removed, which makes the comparison separator-agnostic.
        }
        return sb.ToString();
    }

    // Aliases are stored already-normalized so a lookup is one Normalize call.
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

        Add(Field.FirstName,
            "שם פרטי", "פרטי", "first name", "firstname", "first", "given name", "givenname");

        Add(Field.LastName,
            "שם משפחה", "משפחה", "last name", "lastname", "last", "surname", "family name", "familyname");

        Add(Field.FullName,
            "שם", "שם מלא", "שם הסטודנט", "שם סטודנט", "שם המשתמש המלא",
            "name", "full name", "fullname", "student name", "display name", "displayname");

        Add(Field.Email,
            "אימייל", "מייל", "אימיל", "דואל", "דואר אלקטרוני", "כתובת מייל", "כתובת אימייל",
            "email", "e-mail", "mail", "email address", "emailaddress", "e mail");

        Add(Field.Phone,
            "טלפון", "נייד", "פלאפון", "מספר טלפון", "טלפון נייד",
            "phone", "mobile", "cell", "cellphone", "telephone", "phone number", "phonenumber", "tel");

        Add(Field.Role,
            "תפקיד", "סוג משתמש", "הרשאה", "הרשאות",
            "role", "user role", "userrole", "type", "user type", "usertype", "permission");

        Add(Field.AcademicYear,
            "מחזור", "שנה", "שנת לימודים", "שנתון", "מחזור לימודים",
            "cycle", "academic cycle", "academiccycle", "academic year", "academicyear", "year", "cohort");

        return map;
    }

    // Headers that name a concept the data model does not have. Recognised ONLY
    // so the UI can explain why they were not mapped instead of leaving the
    // admin to guess.
    private static readonly Dictionary<string, string> UnsupportedHeaders =
        new(StringComparer.Ordinal)
        {
            [Normalize("שם משתמש")]  = "אין שדה \"שם משתמש\" במערכת — משתמשים מזוהים לפי כתובת האימייל.",
            [Normalize("username")]  = "אין שדה \"שם משתמש\" במערכת — משתמשים מזוהים לפי כתובת האימייל.",
            [Normalize("user name")] = "אין שדה \"שם משתמש\" במערכת — משתמשים מזוהים לפי כתובת האימייל.",
            [Normalize("סיסמה")]     = "לא ניתן לייבא סיסמאות. המשתמש מקבל קישור להגדרת סיסמה במייל.",
            [Normalize("password")]  = "לא ניתן לייבא סיסמאות. המשתמש מקבל קישור להגדרת סיסמה במייל.",
            [Normalize("תז")]        = "שדה תעודת הזהות אינו נחשף ב-API של ניהול המשתמשים ולכן אינו ניתן לייבוא.",
            [Normalize("תעודת זהות")] = "שדה תעודת הזהות אינו נחשף ב-API של ניהול המשתמשים ולכן אינו ניתן לייבוא.",
            [Normalize("id number")] = "שדה תעודת הזהות אינו נחשף ב-API של ניהול המשתמשים ולכן אינו ניתן לייבוא.",
            [Normalize("סטטוס")]     = "אין נקודת קצה לעדכון סטטוס פעיל/לא פעיל, ולכן לא ניתן לייבא אותו.",
            [Normalize("status")]    = "אין נקודת קצה לעדכון סטטוס פעיל/לא פעיל, ולכן לא ניתן לייבא אותו.",
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
        /// import" is indistinguishable from a column that was never
        /// recognised, and the badge calls a decision an outstanding
        /// problem.</summary>
        public bool   UserSet    { get; set; }
        /// <summary>A sample value from the first non-empty data row, so the
        /// admin can tell two similar columns apart.</summary>
        public string? Sample    { get; init; }
    }

    /// <summary>Auto-maps what the alias table recognises and leaves the rest
    /// alone. A second column claiming an already-taken field is NOT auto-mapped
    /// — first match wins and the duplicate drops to manual, because guessing
    /// which of two "email" columns is the real one is exactly the guess this
    /// is not allowed to make.</summary>
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
                mapping.Target = Field.Ignore;
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

    /// <summary>A destination claimed by more than one column. The import writes
    /// one value per field, so this has to be resolved before continuing.</summary>
    public static IReadOnlyList<Field> ConflictingTargets(IEnumerable<ColumnMapping> mappings) =>
        mappings.Where(m => m.Target != Field.Ignore)
                .GroupBy(m => m.Target)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

    /// <summary>Full name and its two halves describe the same thing; mapping
    /// both means two columns writing FirstName.</summary>
    public static bool HasNameConflict(IEnumerable<ColumnMapping> mappings)
    {
        var set = mappings.Select(m => m.Target).ToHashSet();
        return set.Contains(Field.FullName) &&
               (set.Contains(Field.FirstName) || set.Contains(Field.LastName));
    }

    /// <summary>What still has to be mapped before a row can be built. Email is
    /// non-negotiable: it is the users table's unique key AND the only handle
    /// this screen has for deciding whether a row is new.</summary>
    public static IReadOnlyList<string> MissingRequirements(IEnumerable<ColumnMapping> mappings)
    {
        var set     = mappings.Select(m => m.Target).ToHashSet();
        var missing = new List<string>();

        if (!set.Contains(Field.Email))
            missing.Add("אימייל — שדה חובה, ולפיו המערכת מזהה משתמש קיים");

        var hasName = set.Contains(Field.FullName) ||
                      (set.Contains(Field.FirstName) && set.Contains(Field.LastName));
        if (!hasName)
            missing.Add("שם — יש למפות עמודת \"שם מלא\", או גם \"שם פרטי\" וגם \"שם משפחה\"");

        return missing;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Value resolution
    // ─────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> RoleAliases = BuildRoleAliases();

    private static Dictionary<string, string> BuildRoleAliases()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string role, params string[] names)
        {
            foreach (var n in names) map[Normalize(n)] = role;
        }

        Add(Roles.Student, "student", "סטודנט", "סטודנטית", "תלמיד", "תלמידה");
        Add(Roles.Mentor,  "mentor", "מנחה", "מנחה אקדמי", "מנטור", "supervisor");
        // Staff is the product's constant; "מרצה"/"lecturer" is what people
        // call it, and the Users table itself has no Lecturer role.
        Add(Roles.Staff,   "staff", "מרצה", "לקטור", "lecturer", "teacher", "סגל", "צוות", "מרצה / צוות");
        Add(Roles.Admin,   "admin", "מנהל", "מנהלת", "מנהל מערכת", "administrator", "sysadmin");

        return map;
    }

    /// <summary>Null when the value is present but means nothing to the system.
    /// An EMPTY value is not an error — it falls back to the default the
    /// single-user create form uses.</summary>
    public static string? ResolveRole(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Roles.Student;
        return RoleAliases.TryGetValue(Normalize(raw), out var role) ? role : null;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Rows
    // ─────────────────────────────────────────────────────────────────────

    public enum RowState
    {
        /// <summary>Passes validation and no existing user has this email.</summary>
        New,
        /// <summary>Passes validation, but a user with this email already exists.</summary>
        Duplicate,
        /// <summary>Cannot be imported as it stands.</summary>
        Invalid,
    }

    /// <summary>What the admin chose to do about one duplicate.</summary>
    public enum DuplicateAction
    {
        /// <summary>Keep the existing user, fill only the fields it is missing.</summary>
        Merge,
        /// <summary>Overwrite the existing user's fields with the file's values.</summary>
        Replace,
        /// <summary>Change nothing; the file row is dropped.</summary>
        Skip,
    }

    public sealed class Row
    {
        public int    LineNumber   { get; init; }     // 1-based, counting the header as line 1
        public string FirstName    { get; set; } = "";
        public string LastName     { get; set; } = "";
        public string Email        { get; set; } = "";
        public string Phone        { get; set; } = "";
        public string AcademicYear { get; set; } = "";
        public string Role         { get; set; } = Roles.Student;

        public RowState State  { get; set; }
        public List<string> Errors  { get; } = new();
        /// <summary>Non-blocking remarks — shown, but never stop an import.</summary>
        public List<string> Notices { get; } = new();

        /// <summary>The existing user this row collides with, when State is Duplicate.</summary>
        public UserForAdmin?   Existing { get; set; }
        public DuplicateAction Action   { get; set; } = DuplicateAction.Merge;

        public string FullName => $"{FirstName} {LastName}".Trim();
    }

    /// <summary>Builds and validates every row against the current mapping and
    /// the users already loaded on the page.</summary>
    public static List<Row> BuildRows(ImportFileParser.Grid grid,
                                      IReadOnlyList<ColumnMapping> mappings,
                                      IReadOnlyList<UserForAdmin> existingUsers)
    {
        int? IndexOf(Field f) => mappings.FirstOrDefault(m => m.Target == f)?.Index;

        var iFull   = IndexOf(Field.FullName);
        var iFirst  = IndexOf(Field.FirstName);
        var iLast   = IndexOf(Field.LastName);
        var iEmail  = IndexOf(Field.Email);
        var iPhone  = IndexOf(Field.Phone);
        var iRole   = IndexOf(Field.Role);
        var iYear   = IndexOf(Field.AcademicYear);

        // Email is the users table's UNIQUE key, and SQLite compares it as
        // written — but AuthRepository lowercases before every lookup and
        // insert, so the effective identity is the lowercased address. Matching
        // any other way would let the import create a row the server then
        // rejects as Exists.
        var byEmail = new Dictionary<string, UserForAdmin>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in existingUsers)
        {
            var key = (u.Email ?? "").Trim();
            if (key.Length > 0) byEmail[key] = u;
        }

        var knownCycles = existingUsers
            .Select(u => (u.AcademicYear ?? "").Trim())
            .Where(y => y.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seenInFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<Row>();

        for (var r = 0; r < grid.Rows.Count; r++)
        {
            var src = grid.Rows[r];
            string Cell(int? i) => i is { } idx && idx < src.Count ? (src[idx] ?? "").Trim() : "";

            var row = new Row { LineNumber = r + 2 };   // +2: 1-based, and the header is line 1

            // ── Name ────────────────────────────────────────────────────
            if (iFull is not null)
            {
                var full = Cell(iFull);
                // First token is the given name, everything after it the family
                // name. A single-token name cannot fill a NOT NULL LastName, so
                // it is an error rather than a silent empty surname.
                var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    row.FirstName = parts[0];
                    row.LastName  = string.Join(' ', parts.Skip(1));
                }
                else if (parts.Length == 1)
                {
                    row.FirstName = parts[0];
                    row.Errors.Add("השם מכיל מילה אחת בלבד — לא ניתן להפריד לשם פרטי ולשם משפחה");
                }
            }
            else
            {
                row.FirstName = Cell(iFirst);
                row.LastName  = Cell(iLast);
            }

            if (string.IsNullOrWhiteSpace(row.FirstName)) row.Errors.Add("חסר שם פרטי");
            if (string.IsNullOrWhiteSpace(row.LastName) && iFull is null) row.Errors.Add("חסר שם משפחה");

            // ── Email ───────────────────────────────────────────────────
            row.Email = Cell(iEmail).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(row.Email))
            {
                row.Errors.Add("חסרה כתובת מייל");
            }
            else if (!IsPlausibleEmail(row.Email))
            {
                // Same shape check the single-user create form applies before
                // it will call the server, kept identical on purpose.
                row.Errors.Add("כתובת מייל לא תקינה");
            }
            else if (seenInFile.TryGetValue(row.Email, out var firstLine))
            {
                row.Errors.Add($"כתובת המייל מופיעה כבר בשורה {firstLine} בקובץ");
            }
            else
            {
                seenInFile[row.Email] = row.LineNumber;
            }

            // ── Phone ───────────────────────────────────────────────────
            row.Phone = Cell(iPhone);

            // ── Role ────────────────────────────────────────────────────
            var rawRole = Cell(iRole);
            var role    = ResolveRole(rawRole);
            if (role is null)
            {
                row.Errors.Add($"תפקיד לא מוכר: \"{Isolate(rawRole)}\"");
                row.Role = Roles.Student;
            }
            else
            {
                row.Role = role;
                if (role == Roles.Staff)
                {
                    // Not a warning about the import — a statement about what
                    // the database will do, via trg_userroles_staff_implies_admin_ins.
                    row.Notices.Add("תפקיד סגל מקנה גם הרשאת מנהל מערכת");
                }
            }

            // ── Cycle ───────────────────────────────────────────────────
            row.AcademicYear = Cell(iYear);
            if (row.AcademicYear.Length > 0 && !knownCycles.Contains(row.AcademicYear))
            {
                // A free-text column with no lookup table behind it: an unknown
                // value is worth flagging, but it is not invalid — the server
                // stores whatever string it is given.
                row.Notices.Add($"מחזור \"{Isolate(row.AcademicYear)}\" אינו קיים כרגע אצל אף משתמש");
            }

            // ── Outcome ─────────────────────────────────────────────────
            if (row.Errors.Count > 0)
            {
                row.State = RowState.Invalid;
            }
            else if (byEmail.TryGetValue(row.Email, out var existing))
            {
                row.State    = RowState.Duplicate;
                row.Existing = existing;
                row.Action   = DuplicateAction.Merge;
            }
            else
            {
                row.State = RowState.New;

                // A NAME collision is not a duplicate. Names are not unique in
                // the users table and nothing may be merged on them — so this
                // is said out loud and the row still creates a new user.
                var nameTwin = existingUsers.FirstOrDefault(u =>
                    string.Equals($"{u.FirstName} {u.LastName}".Trim(), row.FullName,
                                  StringComparison.OrdinalIgnoreCase));
                if (nameTwin is not null)
                    row.Notices.Add($"קיים משתמש בשם זהה ({Isolate(nameTwin.Email)}) — ייווצר משתמש חדש");
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Wraps a value in Unicode FIRST STRONG ISOLATE / POP DIRECTIONAL
    /// ISOLATE — the plain-text equivalent of &lt;bdi&gt;. Without it a Latin
    /// value dropped into a Hebrew sentence drags the surrounding quotes,
    /// parentheses and punctuation to the wrong side: `(a@b.com) — ייווצר`
    /// renders as `— ייווצר (a@b.com` in an RTL run. These strings are rendered
    /// as text, not markup, so the isolation has to travel in the string.</summary>
    private static string Isolate(string? value) => "\u2068" + (value ?? "") + "\u2069";

    /// <summary>The same permissive check SaveCreate performs client-side before
    /// calling AdminAddUser: an '@' and a '.'. Deliberately not a strict RFC
    /// validator — the server's [EmailAddress] attribute is the real gate, and a
    /// stricter client rule would reject rows the system would have accepted.</summary>
    private static bool IsPlausibleEmail(string email) =>
        email.Contains('@') && email.Contains('.') && !email.Contains(' ');

    // ─────────────────────────────────────────────────────────────────────
    //  Duplicate diffing
    // ─────────────────────────────────────────────────────────────────────

    public sealed record FieldDiff(string Label, string ExistingValue, string ImportedValue,
                                   bool ExistingIsEmpty, bool Differs);

    /// <summary>The four fields PUT api/Admin/users/{id} can actually write.
    /// Email is not among them — it is the match key, so it is identical by
    /// definition — and nothing else on the user is reachable from this API.</summary>
    public static List<FieldDiff> Diff(UserForAdmin existing, Row row)
    {
        var list = new List<FieldDiff>();

        void Add(string label, string? existingValue, string importedValue)
        {
            var ex  = (existingValue ?? "").Trim();
            var im  = (importedValue ?? "").Trim();
            var empty = ex.Length == 0;
            var differs = im.Length > 0 && !string.Equals(ex, im, StringComparison.Ordinal);
            list.Add(new FieldDiff(label, ex, im, empty, differs));
        }

        Add("שם פרטי",  existing.FirstName, row.FirstName);
        Add("שם משפחה", existing.LastName,  row.LastName);
        Add("טלפון",    existing.Phone,     row.Phone);
        Add("מחזור",    existing.AcademicYear, row.AcademicYear);
        Add("תפקיד",
            FieldLabelForRole(existing.Roles?.FirstOrDefault(x => x != Roles.User)),
            FieldLabelForRole(row.Role));

        return list;
    }

    private static string FieldLabelForRole(string? role) => role switch
    {
        Roles.Student => "סטודנט",
        Roles.Mentor  => "מנחה",
        Roles.Staff   => "מרצה",
        Roles.Admin   => "מנהל מערכת",
        null or ""    => "",
        _             => role,
    };

    /// <summary>Fields a merge would fill — existing is empty AND the file has a
    /// value. A merge never touches a populated field, which is the whole
    /// difference between it and a replace.</summary>
    public static IReadOnlyList<FieldDiff> MergeFills(UserForAdmin existing, Row row) =>
        Diff(existing, row).Where(d => d.ExistingIsEmpty && d.ImportedValue.Length > 0).ToList();

    /// <summary>Fields a replace would overwrite with a DIFFERENT value. An
    /// empty imported value is not an overwrite — the request falls back to the
    /// existing value, because PUT rejects an empty name or role.</summary>
    public static IReadOnlyList<FieldDiff> ReplaceOverwrites(UserForAdmin existing, Row row) =>
        Diff(existing, row).Where(d => d.Differs && !d.ExistingIsEmpty).ToList();

    /// <summary>Builds the update body for a duplicate. Merge keeps every
    /// populated existing value; Replace prefers the file but still falls back
    /// to the existing value where the file is silent, because the endpoint
    /// requires FirstName, LastName and a valid Role on every call.</summary>
    public static AdminUpdateUserRequest BuildUpdate(UserForAdmin existing, Row row, DuplicateAction action)
    {
        string Pick(string? existingValue, string importedValue)
        {
            var ex = (existingValue ?? "").Trim();
            var im = (importedValue ?? "").Trim();

            return action == DuplicateAction.Replace
                ? (im.Length > 0 ? im : ex)
                : (ex.Length > 0 ? ex : im);
        }

        var existingRole = existing.Roles?.FirstOrDefault(x => x != Roles.User);

        return new AdminUpdateUserRequest
        {
            FirstName    = Pick(existing.FirstName, row.FirstName),
            LastName     = Pick(existing.LastName,  row.LastName),
            Phone        = Pick(existing.Phone,     row.Phone),
            AcademicYear = Pick(existing.AcademicYear, row.AcademicYear),
            Role         = Pick(existingRole, row.Role) is { Length: > 0 } picked ? picked : Roles.Student,
        };
    }

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
                case RowState.Invalid:   invalid++; break;
                case RowState.New:       create++;  break;
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
