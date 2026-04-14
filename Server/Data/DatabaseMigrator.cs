using Microsoft.Data.Sqlite;

namespace AuthWithAdmin.Server.Data;

public static class DatabaseMigrator
{
    public static async Task MigrateAsync(IConfiguration config)
    {
        string connectionString = config.GetConnectionString("DefaultConnection")!;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var existingColumns = await GetColumnsAsync(connection, "users");

        if (!existingColumns.Contains("Phone"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE users ADD COLUMN Phone TEXT NOT NULL DEFAULT ''");

        if (!existingColumns.Contains("AcademicYear"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE users ADD COLUMN AcademicYear TEXT NOT NULL DEFAULT '2025-2026'");

        if (!existingColumns.Contains("IsActive"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE users ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1");

        // ── AcademicYears.Status column (lifecycle: Active / Closed / Archived) ─
        // NULL means "derive from IsActive" for backward compatibility.
        var ayColumns = await GetColumnsAsync(connection, "AcademicYears");
        if (!ayColumns.Contains("Status"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE AcademicYears ADD COLUMN Status TEXT");

        // ── Projects — catalog / proposal metadata columns ──────────────────
        var projectColumns = await GetColumnsAsync(connection, "Projects");

        if (!projectColumns.Contains("SourceType"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN SourceType TEXT NOT NULL DEFAULT 'Manual'");

        if (!projectColumns.Contains("AirtableRecordId"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN AirtableRecordId TEXT");

        if (!projectColumns.Contains("OrganizationName"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN OrganizationName TEXT");

        if (!projectColumns.Contains("ContactPerson"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN ContactPerson TEXT");

        if (!projectColumns.Contains("ContactRole"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN ContactRole TEXT");

        if (!projectColumns.Contains("Goals"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN Goals TEXT");

        if (!projectColumns.Contains("TargetAudience"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN TargetAudience TEXT");

        if (!projectColumns.Contains("InternalNotes"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN InternalNotes TEXT");

        if (!projectColumns.Contains("Priority"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN Priority TEXT");

        // ── Seed milestone templates with applicability examples ────────────
        // Adds two type-specific templates only if they don't already exist.
        // Existing 6 templates have ProjectTypeId=NULL (shared / both types).
        await connection.ExecuteNonQueryAsync(@"
            INSERT OR IGNORE INTO MilestoneTemplates (Id, Title, Description, OrderIndex, IsRequired, IsActive, ProjectTypeId)
            VALUES
                (7, 'הצגת אב-טיפוס', 'הדגמת מוצר עובד / פרוטוטייפ בשלב הביניים', 7, 1, 1, 1),
                (8, 'סקירת ספרות', 'סקירה שיטתית של המחקר הקיים בתחום', 2, 1, 1, 2)");

        // ── Seed catalog test projects (run only if not already present) ────
        // Provides 4 representative test rows:
        //   1. Manual, unassigned, Available  (new row)
        //   2. Manual, unassigned, Unavailable (new row)
        //   3. Airtable-sourced, unassigned   (new row)
        //   (Assigned projects already exist in the DB via other seeding)
        await using var seedCmd = connection.CreateCommand();
        seedCmd.CommandText = "SELECT COUNT(*) FROM Projects WHERE SourceType IS NOT NULL AND SourceType != 'Manual'";
        var seedCheckResult = await seedCmd.ExecuteScalarAsync();
        int seedCheck = seedCheckResult is DBNull || seedCheckResult is null ? 0 : Convert.ToInt32(seedCheckResult);
        if (seedCheck == 0)
        {
            // Airtable-synced proposal (unassigned, Available)
            await connection.ExecuteNonQueryAsync(@"
                INSERT OR IGNORE INTO Projects
                    (ProjectNumber, Title, Description, Status, AcademicYearId, ProjectTypeId,
                     SourceType, AirtableRecordId, OrganizationName, ContactPerson, ContactRole,
                     Goals, TargetAudience, Priority)
                SELECT 99, 'מערכת ניהול תורים דיגיטלית', 'פיתוח מערכת תורים לקופת חולים',
                       'Available', Id, 1,
                       'Airtable', 'rec_AT_001', 'מכבי שירותי בריאות', 'רינת כהן', 'מנהלת IT',
                       'שיפור חווית המטופל וצמצום זמני המתנה', 'מטופלים ועובדי קופת חולים',
                       'High'
                FROM AcademicYears WHERE IsCurrent = 1 LIMIT 1");

            // Manual proposal with no org info — minimalist entry
            await connection.ExecuteNonQueryAsync(@"
                INSERT OR IGNORE INTO Projects
                    (ProjectNumber, Title, Description, Status, AcademicYearId, ProjectTypeId,
                     SourceType, Priority, InternalNotes)
                SELECT 100, 'כלי BI לניתוח נתוני סטודנטים', 'לוח מחוונים לניטור ביצועים אקדמיים',
                       'Unavailable', Id, 2,
                       'Manual', 'Medium', 'ממתין לאישור הנהלה'
                FROM AcademicYears WHERE IsCurrent = 1 LIMIT 1");
        }

        // ── ResourceFiles table ──────────────────────────────────────────────
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ResourceFiles (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                FileName         TEXT    NOT NULL,
                StoredFileName   TEXT    NOT NULL,
                ContentType      TEXT    NOT NULL DEFAULT '',
                UploadedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                UploadedByUserId INTEGER NOT NULL DEFAULT 0,
                Description      TEXT,
                MilestoneId      INTEGER NOT NULL,
                TaskId           INTEGER,
                FOREIGN KEY (MilestoneId) REFERENCES ProjectMilestones(Id) ON DELETE RESTRICT,
                FOREIGN KEY (TaskId)      REFERENCES Tasks(Id)             ON DELETE SET NULL
            )");
    }

    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection connection, string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName})";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1)); // column index 1 = name
        }

        return columns;
    }

    private static async Task ExecuteNonQueryAsync(this SqliteConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
