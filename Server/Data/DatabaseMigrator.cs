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

        if (!existingColumns.Contains("IdNumber"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE users ADD COLUMN IdNumber TEXT NOT NULL DEFAULT ''");

        if (!existingColumns.Contains("ProfileImagePath"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE users ADD COLUMN ProfileImagePath TEXT");

        // ── Teams.TeamName column ────────────────────────────────────────────
        var teamColumns = await GetColumnsAsync(connection, "Teams");
        if (!teamColumns.Contains("TeamName"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Teams ADD COLUMN TeamName TEXT NOT NULL DEFAULT ''");

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

        if (!projectColumns.Contains("LastSyncedAt"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN LastSyncedAt TEXT");

        // ── Airtable extended fields (added with Hebrew FieldMap support) ────
        if (!projectColumns.Contains("OrganizationType"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN OrganizationType TEXT");

        if (!projectColumns.Contains("ProjectTopic"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN ProjectTopic TEXT");

        if (!projectColumns.Contains("Contents"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN Contents TEXT");

        if (!projectColumns.Contains("ContactEmail"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN ContactEmail TEXT");

        if (!projectColumns.Contains("ContactPhone"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN ContactPhone TEXT");

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
        // Full schema including all columns so new databases get the complete
        // structure from the start. Existing databases get missing columns via
        // the ALTER TABLE guards below.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ResourceFiles (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                ItemType         TEXT    NOT NULL DEFAULT 'File',
                FileName         TEXT    NOT NULL,
                StoredFileName   TEXT    NOT NULL DEFAULT '',
                ContentType      TEXT    NOT NULL DEFAULT '',
                UploadedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                UploadedByUserId INTEGER NOT NULL DEFAULT 0,
                Description      TEXT,
                MilestoneId      INTEGER NOT NULL DEFAULT 0,
                TaskId           INTEGER,
                ProjectType      TEXT,
                ForTechnological  INTEGER NOT NULL DEFAULT 0,
                ForMethodological INTEGER NOT NULL DEFAULT 0,
                VideoUrl         TEXT,
                FOREIGN KEY (TaskId) REFERENCES TaskTemplates(Id) ON DELETE SET NULL
            )");

        // ── ResourceFiles: fix legacy FK constraints ─────────────────────────
        // The original table was created with:
        //   FOREIGN KEY (MilestoneId) REFERENCES ProjectMilestones(Id)
        //   FOREIGN KEY (TaskId)      REFERENCES Tasks(Id)
        // The current design uses MilestoneId = 0 as "no milestone" sentinel
        // (no FK on MilestoneId) and TaskId references TaskTemplates, not Tasks.
        // SQLite cannot DROP a constraint, so we recreate the table when the
        // old definition is detected.
        await using var rfSchemaCmd = connection.CreateCommand();
        rfSchemaCmd.CommandText =
            "SELECT sql FROM sqlite_master WHERE type='table' AND name='ResourceFiles'";
        var rfSchemaSql = (await rfSchemaCmd.ExecuteScalarAsync())?.ToString() ?? "";

        if (rfSchemaSql.Contains("ProjectMilestones") || rfSchemaSql.Contains("REFERENCES Tasks"))
        {
            await connection.ExecuteNonQueryAsync("PRAGMA foreign_keys = OFF");
            try
            {
                await connection.ExecuteNonQueryAsync(@"
                    CREATE TABLE ResourceFiles_new (
                        Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                        ItemType         TEXT    NOT NULL DEFAULT 'File',
                        FileName         TEXT    NOT NULL,
                        StoredFileName   TEXT    NOT NULL DEFAULT '',
                        ContentType      TEXT    NOT NULL DEFAULT '',
                        UploadedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                        UploadedByUserId INTEGER NOT NULL DEFAULT 0,
                        Description      TEXT,
                        MilestoneId      INTEGER NOT NULL DEFAULT 0,
                        TaskId           INTEGER,
                        ProjectType      TEXT,
                        ForTechnological  INTEGER NOT NULL DEFAULT 0,
                        ForMethodological INTEGER NOT NULL DEFAULT 0,
                        VideoUrl         TEXT,
                        FOREIGN KEY (TaskId) REFERENCES TaskTemplates(Id) ON DELETE SET NULL
                    )");

                await connection.ExecuteNonQueryAsync(@"
                    INSERT INTO ResourceFiles_new
                        (Id, ItemType, FileName, StoredFileName, ContentType, UploadedAt,
                         UploadedByUserId, Description, MilestoneId, TaskId, ProjectType,
                         ForTechnological, ForMethodological, VideoUrl)
                    SELECT
                        Id,
                        COALESCE(ItemType, 'File'),
                        FileName,
                        COALESCE(StoredFileName, ''),
                        COALESCE(ContentType, ''),
                        UploadedAt,
                        UploadedByUserId,
                        Description,
                        COALESCE(MilestoneId, 0),
                        NULL,
                        ProjectType,
                        COALESCE(ForTechnological, 0),
                        COALESCE(ForMethodological, 0),
                        VideoUrl
                    FROM ResourceFiles");

                await connection.ExecuteNonQueryAsync("DROP TABLE ResourceFiles");
                await connection.ExecuteNonQueryAsync(
                    "ALTER TABLE ResourceFiles_new RENAME TO ResourceFiles");
            }
            finally
            {
                await connection.ExecuteNonQueryAsync("PRAGMA foreign_keys = ON");
            }
        }

        // ── ALTER TABLE guards for databases created before each column was added ──
        // Each guard is idempotent: skipped when the column already exists.
        var rfColumns = await GetColumnsAsync(connection, "ResourceFiles");

        if (!rfColumns.Contains("ProjectType"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE ResourceFiles ADD COLUMN ProjectType TEXT");

        if (!rfColumns.Contains("ForTechnological"))
        {
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE ResourceFiles ADD COLUMN ForTechnological INTEGER NOT NULL DEFAULT 0");
            // Backfill from legacy ProjectType column
            await connection.ExecuteNonQueryAsync(
                "UPDATE ResourceFiles SET ForTechnological = 1 WHERE ProjectType = 'Technological'");
        }

        if (!rfColumns.Contains("ForMethodological"))
        {
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE ResourceFiles ADD COLUMN ForMethodological INTEGER NOT NULL DEFAULT 0");
            await connection.ExecuteNonQueryAsync(
                "UPDATE ResourceFiles SET ForMethodological = 1 WHERE ProjectType = 'Methodological'");
        }

        // 'File' (default) or 'Video' — determines how the item is stored and rendered.
        if (!rfColumns.Contains("ItemType"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE ResourceFiles ADD COLUMN ItemType TEXT NOT NULL DEFAULT 'File'");

        // YouTube watch or embed URL — populated only when ItemType = 'Video'.
        if (!rfColumns.Contains("VideoUrl"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE ResourceFiles ADD COLUMN VideoUrl TEXT");

        // StoredFileName was originally NOT NULL in the CREATE TABLE.
        // For video items it must be allowed to be empty, which it is (empty string
        // satisfies NOT NULL). No schema change needed — just documenting the intent.
        // ContentType same reasoning.

        // ── TaskTemplates table ───────────────────────────────────────────────
        // Global reusable task definitions linked to a milestone template.
        // Separate from per-project operational Tasks.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS TaskTemplates (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                Title               TEXT    NOT NULL,
                Description         TEXT,
                MilestoneTemplateId INTEGER NOT NULL,
                StartDate           TEXT    NOT NULL,
                DueDate             TEXT    NOT NULL,
                IsActive            INTEGER NOT NULL DEFAULT 1,
                CreatedAt           TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (MilestoneTemplateId) REFERENCES MilestoneTemplates(Id) ON DELETE CASCADE
            )");

        // ── Backfill TaskTemplates from Tasks(TaskType='System') ─────────────
        // One-time bootstrap: copies distinct global/system task definitions from
        // the operational Tasks table into the TaskTemplates master catalog.
        //
        // Field mapping:
        //   Title               ← Tasks.Title
        //   Description         ← Tasks.Description
        //   MilestoneTemplateId ← resolved via ProjectMilestones → AcademicYearMilestones
        //   StartDate           ← AcademicYearMilestones.OpenDate (milestone open date)
        //   DueDate             ← Tasks.DueDate
        //   IsActive            ← 1 (system tasks are active by definition)
        //
        // Idempotent: skips any row whose (Title, MilestoneTemplateId) pair already
        // exists in TaskTemplates, so this is safe to run on every application start.
        //
        // Only Tasks that have a resolvable MilestoneTemplateId are included.
        // Tasks without a ProjectMilestoneId (orphaned system tasks) are excluded.
        await connection.ExecuteNonQueryAsync(@"
            INSERT INTO TaskTemplates (Title, Description, MilestoneTemplateId, StartDate, DueDate, IsActive)
            SELECT
                t.Title,
                t.Description,
                aym.MilestoneTemplateId,
                COALESCE(aym.OpenDate, date(t.CreatedAt)),
                t.DueDate,
                1
            FROM Tasks t
            JOIN ProjectMilestones     pm  ON pm.Id  = t.ProjectMilestoneId
            JOIN AcademicYearMilestones aym ON aym.Id = pm.AcademicYearMilestoneId
            WHERE t.TaskType = 'System'
              AND t.ProjectMilestoneId IS NOT NULL
              AND aym.MilestoneTemplateId IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1
                  FROM   TaskTemplates tt
                  WHERE  tt.Title               = t.Title
                    AND  tt.MilestoneTemplateId = aym.MilestoneTemplateId
              )
            GROUP BY t.Title, aym.MilestoneTemplateId");

        // ── Submission policy columns on Tasks ───────────────────────────────
        // Copied from TaskTemplates when applying templates to a year.
        // Lets the student side enforce upload rules without a live FK to TaskTemplates.
        var tasksColumns = await GetColumnsAsync(connection, "Tasks");

        if (!tasksColumns.Contains("MaxFilesCount"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Tasks ADD COLUMN MaxFilesCount INTEGER");

        if (!tasksColumns.Contains("MaxFileSizeMb"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Tasks ADD COLUMN MaxFileSizeMb INTEGER");

        if (!tasksColumns.Contains("AllowedFileTypes"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Tasks ADD COLUMN AllowedFileTypes TEXT");

        // ── Submission policy columns on TaskTemplates ────────────────────────
        // Defines the upload rules that students must follow when submitting.
        // All columns are nullable — they are only populated when IsSubmission = 1.
        var ttColumns = await GetColumnsAsync(connection, "TaskTemplates");

        if (!ttColumns.Contains("IsSubmission"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskTemplates ADD COLUMN IsSubmission INTEGER NOT NULL DEFAULT 0");

        if (!ttColumns.Contains("SubmissionInstructions"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskTemplates ADD COLUMN SubmissionInstructions TEXT");

        if (!ttColumns.Contains("MaxFilesCount"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskTemplates ADD COLUMN MaxFilesCount INTEGER");

        if (!ttColumns.Contains("MaxFileSizeMb"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskTemplates ADD COLUMN MaxFileSizeMb INTEGER");

        if (!ttColumns.Contains("AllowedFileTypes"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskTemplates ADD COLUMN AllowedFileTypes TEXT");

        // ── TaskTemplateResourceFiles junction table ──────────────────────────
        // Links task templates to supporting/reference resource files.
        // Files are not mandatory — they are reference materials attached to a template.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS TaskTemplateResourceFiles (
                TaskTemplateId  INTEGER NOT NULL,
                ResourceFileId  INTEGER NOT NULL,
                PRIMARY KEY (TaskTemplateId, ResourceFileId),
                FOREIGN KEY (TaskTemplateId) REFERENCES TaskTemplates(Id)  ON DELETE CASCADE,
                FOREIGN KEY (ResourceFileId) REFERENCES ResourceFiles(Id)  ON DELETE CASCADE
            )");

        // ── Tasks.IsSubmission column ─────────────────────────────────────────
        // Snapshot flag copied from TaskTemplates when apply-templates runs.
        // Guarded here in case the original schema pre-dates this column.
        var tasksColumnsV2 = await GetColumnsAsync(connection, "Tasks");

        if (!tasksColumnsV2.Contains("IsSubmission"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Tasks ADD COLUMN IsSubmission INTEGER NOT NULL DEFAULT 0");

        if (!tasksColumnsV2.Contains("SubmissionInstructions"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Tasks ADD COLUMN SubmissionInstructions TEXT");

        // ── TaskSubmissions table ─────────────────────────────────────────────
        // One record per submission attempt for an operational task.
        // Snapshot validation policy (MaxFilesCount / MaxFileSizeMb / AllowedFileTypes)
        // lives on the Tasks row — not referenced from TaskTemplates at runtime.
        //
        // Status lifecycle: Submitted → Reviewed | NeedsRevision
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS TaskSubmissions (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                TaskId            INTEGER NOT NULL,
                SubmittedByUserId INTEGER NOT NULL,
                SubmittedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                Notes             TEXT,
                Status            TEXT    NOT NULL DEFAULT 'Submitted',
                CreatedAt         TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (TaskId)            REFERENCES Tasks(Id)  ON DELETE CASCADE,
                FOREIGN KEY (SubmittedByUserId) REFERENCES users(Id)  ON DELETE RESTRICT
            )");

        // ── TaskSubmissionFiles table ─────────────────────────────────────────
        // One record per file attached to a submission.
        // StoredFileName is the GUID-based filename under wwwroot/submissions/.
        // SizeBytes is stored for policy re-validation and display without re-reading disk.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS TaskSubmissionFiles (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                TaskSubmissionId  INTEGER NOT NULL,
                OriginalFileName  TEXT    NOT NULL,
                StoredFileName    TEXT    NOT NULL,
                ContentType       TEXT    NOT NULL DEFAULT '',
                SizeBytes         INTEGER NOT NULL DEFAULT 0,
                UploadedAt        TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (TaskSubmissionId) REFERENCES TaskSubmissions(Id) ON DELETE CASCADE
            )");

        // ── ProjectRequests table ─────────────────────────────────────────────
        // Unified requests module: one table covers all request types
        // (Extension, SpecialEvent, TechnicalSupport, Meeting, and three
        // challenge types).  RequestType is a controlled string constant
        // matching the RequestTypes static class in Shared.
        //
        // Status lifecycle: New → InProgress → Resolved | Closed
        // Priority:         Low | Normal | High | Urgent (set by staff/admin, not student)
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ProjectRequests (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId        INTEGER NOT NULL,
                CreatedByUserId  INTEGER NOT NULL,
                RequestType      TEXT    NOT NULL,
                Title            TEXT    NOT NULL,
                Description      TEXT,
                Status           TEXT    NOT NULL DEFAULT 'New',
                Priority         TEXT    NOT NULL DEFAULT 'Normal',
                CreatedAt        TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt        TEXT    NOT NULL DEFAULT (datetime('now')),
                AssignedToUserId INTEGER,
                ResolutionNotes  TEXT,
                FOREIGN KEY (ProjectId)        REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (CreatedByUserId)  REFERENCES users(Id)    ON DELETE RESTRICT,
                FOREIGN KEY (AssignedToUserId) REFERENCES users(Id)    ON DELETE SET NULL
            )");

        // ── ProjectRequestEvents table ───────────────────────────────────────
        // Chronological thread / audit log for each request.
        // Every admin action (status change, assignment, comment) creates one row.
        // EventType is a controlled string: Comment | StatusChange | PriorityChange | AssigneeChange
        // OldValue / NewValue carry human-readable labels (not raw IDs).
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ProjectRequestEvents (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                RequestId INTEGER NOT NULL,
                UserId    INTEGER NOT NULL,
                EventType TEXT    NOT NULL,
                Content   TEXT,
                OldValue  TEXT,
                NewValue  TEXT,
                CreatedAt TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (RequestId) REFERENCES ProjectRequests(Id) ON DELETE CASCADE,
                FOREIGN KEY (UserId)    REFERENCES users(Id)           ON DELETE RESTRICT
            )");

        // ── ProjectRequestAttachments table ──────────────────────────────────
        // Image attachments uploaded by students when creating a request.
        // Files are stored in wwwroot/request-attachments/ using the same
        // GUID-filename pattern as TaskSubmissionFiles and ResourceFiles.
        //
        // Allowed content types: image/jpeg, image/png, image/webp
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ProjectRequestAttachments (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                RequestId        INTEGER NOT NULL,
                OriginalFileName TEXT    NOT NULL,
                StoredFileName   TEXT    NOT NULL,
                ContentType      TEXT    NOT NULL DEFAULT '',
                SizeBytes        INTEGER NOT NULL DEFAULT 0,
                UploadedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (RequestId) REFERENCES ProjectRequests(Id) ON DELETE CASCADE
            )");

        // ── ProjectRequestEventAttachments table ─────────────────────────────
        // Files attached to a specific thread event/comment (not to the root
        // request).  Supports images, PDF, and docx.
        // Stored in the same wwwroot/request-attachments/ container.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ProjectRequestEventAttachments (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                EventId          INTEGER NOT NULL,
                RequestId        INTEGER NOT NULL,
                OriginalFileName TEXT    NOT NULL,
                StoredFileName   TEXT    NOT NULL,
                ContentType      TEXT    NOT NULL DEFAULT 'application/octet-stream',
                SizeBytes        INTEGER NOT NULL DEFAULT 0,
                UploadedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (EventId)   REFERENCES ProjectRequestEvents(Id) ON DELETE CASCADE,
                FOREIGN KEY (RequestId) REFERENCES ProjectRequests(Id)      ON DELETE CASCADE
            )");

        // ── TaskSubmissions — mentor + reviewer columns ───────────────────────
        // These columns extend the existing approval workflow to support a
        // two-stage review: mentor first, then lecturer/staff.
        //
        //  MentorStatus     : 'Pending' | 'Approved' | 'Returned'
        //  MentorFeedback   : free-text returned by the mentor when status = Returned
        //  MentorReviewedAt : when the mentor set the status (nullable)
        //  ReviewerFeedback : free-text returned by admin/staff when Status = NeedsRevision
        var submissionsColumns = await GetColumnsAsync(connection, "TaskSubmissions");

        if (!submissionsColumns.Contains("MentorStatus"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN MentorStatus TEXT NOT NULL DEFAULT 'Pending'");

        if (!submissionsColumns.Contains("MentorFeedback"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN MentorFeedback TEXT");

        if (!submissionsColumns.Contains("MentorReviewedAt"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN MentorReviewedAt TEXT");

        if (!submissionsColumns.Contains("ReviewerFeedback"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN ReviewerFeedback TEXT");

        // ── StudentSubTasks table ────────────────────────────────────────────
        // Lightweight internal checklist items created by a student team under
        // a parent system task. These are team-private: not visible to mentors
        // or lecturers. Scoped by TeamId so two teams working on different
        // projects cannot see each other's sub-tasks.
        //
        // IsDone is automatically set to 1 for all rows belonging to a team when
        // a TaskSubmission is created for the parent task (see TaskSubmissionsController).
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS StudentSubTasks (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                TaskId          INTEGER NOT NULL,
                TeamId          INTEGER NOT NULL,
                Title           TEXT    NOT NULL,
                IsDone          INTEGER NOT NULL DEFAULT 0,
                CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                CreatedByUserId INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (TaskId) REFERENCES Tasks(Id) ON DELETE CASCADE
            )");

        // ── StudentSubTasks — extended fields ────────────────────────────────
        // DueDate, Status, and Notes were added after the initial release.
        // ALTER TABLE guards keep existing rows intact.
        var subTaskColumns = await GetColumnsAsync(connection, "StudentSubTasks");

        if (!subTaskColumns.Contains("DueDate"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE StudentSubTasks ADD COLUMN DueDate TEXT");

        if (!subTaskColumns.Contains("Status"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE StudentSubTasks ADD COLUMN Status TEXT NOT NULL DEFAULT 'Open'");

        if (!subTaskColumns.Contains("Notes"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE StudentSubTasks ADD COLUMN Notes TEXT");

        // ── LearningMaterials table ───────────────────────────────────────────
        // Stores learning resources (videos, files, links) visible to students.
        // Targeting rules:
        //   ProjectId NOT NULL                      → only students in that project see it
        //   ProjectId NULL, ProjectType NOT NULL    → all students whose project is that type
        //   ProjectId NULL, ProjectType NULL        → every student (global material)
        // MaterialType: 'Video' | 'File' | 'Link'
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS LearningMaterials (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                Title        TEXT    NOT NULL,
                Description  TEXT,
                MaterialType TEXT    NOT NULL DEFAULT 'Link',
                Url          TEXT,
                FileName     TEXT,
                ProjectId    INTEGER,
                ProjectType  TEXT,
                CreatedAt    TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
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
