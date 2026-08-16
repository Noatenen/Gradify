using Microsoft.Data.Sqlite;

namespace AuthWithAdmin.Server.Data;

public static class DatabaseMigrator
{
    public static async Task MigrateAsync(IConfiguration config)
    {
        string connectionString = config.GetConnectionString("DefaultConnection")!;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // ── Base schema bootstrap ───────────────────────────────────────────
        //
        // On hosted environments (e.g. Render), the SQLite DB starts EMPTY,
        // so every table the migrator subsequently ALTERs / UPDATEs / INSERTs
        // into must exist first. This block creates the seven core tables
        // with their FINAL canonical column set; the per-column ALTER guards
        // below are then no-ops on a fresh DB and remain effective on older
        // DBs that pre-date a particular column.
        //
        // CREATE TABLE IF NOT EXISTS is naturally idempotent — re-running
        // this block on a populated DB is a no-op.
        await EnsureBaseSchemaAsync(connection);

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

        // Tracks lecturer-side draft assignments. Set to 1 by the assignment
        // board until the global publish action flips it back to 0. Students
        // only see projects with AssignmentIsDraft = 0.
        if (!projectColumns.Contains("AssignmentIsDraft"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN AssignmentIsDraft INTEGER NOT NULL DEFAULT 0");

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
        // ── MilestoneTemplates — default course-level dates ───────────────────
        // OpenDate / DueDate / CloseDate are the global defaults that get copied
        // into AcademicYearMilestones when a new cycle is created. Per-team
        // postponements continue to use TeamMilestoneDueDateOverrides — adding
        // these columns does not change that flow.
        var mtColumns = await GetColumnsAsync(connection, "MilestoneTemplates");
        if (!mtColumns.Contains("OpenDate"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE MilestoneTemplates ADD COLUMN OpenDate DATE");
        if (!mtColumns.Contains("DueDate"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE MilestoneTemplates ADD COLUMN DueDate DATE");
        if (!mtColumns.Contains("CloseDate"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE MilestoneTemplates ADD COLUMN CloseDate DATE");

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

        // Lecturer feedback files share this table with student submission files.
        //  IsLecturerFeedback = 0 → student-uploaded (always visible to the team)
        //  IsLecturerFeedback = 1 → lecturer/admin-uploaded; gated for students
        //                            until FilePublishedAt IS NOT NULL.
        var submissionFilesColumns = await GetColumnsAsync(connection, "TaskSubmissionFiles");
        if (!submissionFilesColumns.Contains("IsLecturerFeedback"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissionFiles ADD COLUMN IsLecturerFeedback INTEGER NOT NULL DEFAULT 0");
        if (!submissionFilesColumns.Contains("FilePublishedAt"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissionFiles ADD COLUMN FilePublishedAt TEXT");

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

        // ── PendingApprovalSettings — singleton config row (2026-05-21) ──────
        // Drives the operational pending-mentor-approvals queue: reminder
        // thresholds, channels, recipients, escalation, lecturer overrides,
        // automation toggles. One canonical row at Id = 1; writes UPSERT it.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS PendingApprovalSettings (
                Id                              INTEGER PRIMARY KEY,
                -- Response-time thresholds (days)
                WarningAfterDays                INTEGER NOT NULL DEFAULT 3,
                EscalationAfterDays             INTEGER NOT NULL DEFAULT 7,
                CriticalAfterDays               INTEGER NOT NULL DEFAULT 14,
                -- Reminder channels
                ChannelEmail                    INTEGER NOT NULL DEFAULT 1,
                ChannelInSystem                 INTEGER NOT NULL DEFAULT 1,
                ChannelSlack                    INTEGER NOT NULL DEFAULT 0,
                -- Reminder frequency: 'Once' | 'Daily' | 'EveryNDays' | 'Weekdays'
                ReminderFrequency               TEXT    NOT NULL DEFAULT 'Once',
                ReminderIntervalDays            INTEGER NOT NULL DEFAULT 1,
                -- Recipients (multi-select)
                RecipientMentor                 INTEGER NOT NULL DEFAULT 1,
                RecipientLecturer               INTEGER NOT NULL DEFAULT 0,
                RecipientCoordinator            INTEGER NOT NULL DEFAULT 0,
                RecipientTeam                   INTEGER NOT NULL DEFAULT 0,
                -- Escalation behaviour
                EscalateNotifyLecturer          INTEGER NOT NULL DEFAULT 1,
                EscalateNotifyCoordinator       INTEGER NOT NULL DEFAULT 0,
                MarkProjectAtRisk               INTEGER NOT NULL DEFAULT 1,
                ShowInManagementDashboard       INTEGER NOT NULL DEFAULT 1,
                -- Lecturer-override capabilities
                LecturerCanApproveWithoutMentor INTEGER NOT NULL DEFAULT 1,
                LecturerCanRejectWithoutMentor  INTEGER NOT NULL DEFAULT 0,
                LecturerCanReopenSubmissions    INTEGER NOT NULL DEFAULT 1,
                LecturerCanForceMilestone       INTEGER NOT NULL DEFAULT 0,
                -- Automations
                AutoEscalation                  INTEGER NOT NULL DEFAULT 1,
                AutoProjectHealthUpdates        INTEGER NOT NULL DEFAULT 1,
                AutoReminderScheduling          INTEGER NOT NULL DEFAULT 1,
                AutoNotificationCleanup         INTEGER NOT NULL DEFAULT 0,
                -- Audit
                UpdatedAt                       TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedByUserId                 INTEGER
            )");

        // ── ProjectRequestSeenStates — per-user read tracking (2026-05-19) ───
        // Records when each user last viewed a given request. The list-view
        // endpoints LEFT JOIN this table and emit HasUnread = r.UpdatedAt > s.LastSeenAt.
        // Write endpoints (reply / recommendation / decision) also bump the
        // actor's seen-state so the writer never sees their own message as
        // unread on the next refresh.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ProjectRequestSeenStates (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId      INTEGER NOT NULL,
                RequestId   INTEGER NOT NULL,
                LastSeenAt  TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE (UserId, RequestId),
                FOREIGN KEY (UserId)    REFERENCES users(Id)          ON DELETE CASCADE,
                FOREIGN KEY (RequestId) REFERENCES ProjectRequests(Id) ON DELETE CASCADE
            )");

        // ── ProjectRequestExtensions — extension-request side-table ──────────
        // 1:1 with ProjectRequests when RequestType = 'Extension'.
        // Carries fields that are meaningful only for an extension request, so
        // the generic ProjectRequests row stays untouched.
        //
        // TaskId / ProjectMilestoneId — exactly one may be set; both NULL means
        // the request is "אחר / כללי" (general — no per-team override).
        // Decision columns track the two-stage flow (mentor first, then lecturer
        // if escalated). FinalDecision / ApprovedDueDate are written when the
        // flow terminates; the override row is created from those.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ProjectRequestExtensions (
                Id                       INTEGER PRIMARY KEY AUTOINCREMENT,
                RequestId                INTEGER NOT NULL UNIQUE,
                TaskId                   INTEGER,
                ProjectMilestoneId       INTEGER,
                CurrentDueDate           TEXT,
                RequestedDueDate         TEXT    NOT NULL,
                Reason                   TEXT,
                MentorDecision           TEXT    NOT NULL DEFAULT 'Pending',
                MentorDecidedByUserId    INTEGER,
                MentorDecidedAt          TEXT,
                MentorNotes              TEXT,
                LecturerDecision         TEXT    NOT NULL DEFAULT 'NotRequired',
                LecturerDecidedByUserId  INTEGER,
                LecturerDecidedAt        TEXT,
                LecturerNotes            TEXT,
                FinalDecision            TEXT    NOT NULL DEFAULT 'Pending',
                ApprovedDueDate          TEXT,
                FOREIGN KEY (RequestId)               REFERENCES ProjectRequests(Id)    ON DELETE CASCADE,
                FOREIGN KEY (TaskId)                  REFERENCES Tasks(Id)              ON DELETE SET NULL,
                FOREIGN KEY (ProjectMilestoneId)      REFERENCES ProjectMilestones(Id)  ON DELETE SET NULL,
                FOREIGN KEY (MentorDecidedByUserId)   REFERENCES users(Id)              ON DELETE SET NULL,
                FOREIGN KEY (LecturerDecidedByUserId) REFERENCES users(Id)              ON DELETE SET NULL
            )");

        // ── TeamTaskDueDateOverrides — per-team task due-date override ───────
        // Written when an extension request that targets a specific Task is
        // approved (mentor or lecturer). Reading code uses
        //     COALESCE(o.OverrideDueDate, t.DueDate)
        // when displaying the due date to the team that owns this override.
        // Other teams continue to see Tasks.DueDate.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS TeamTaskDueDateOverrides (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                TeamId            INTEGER NOT NULL,
                TaskId            INTEGER NOT NULL,
                OriginalDueDate   TEXT,
                OverrideDueDate   TEXT    NOT NULL,
                SourceRequestId   INTEGER,
                ApprovedByUserId  INTEGER,
                CreatedAt         TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt         TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE (TeamId, TaskId),
                FOREIGN KEY (TeamId)           REFERENCES Teams(Id)            ON DELETE CASCADE,
                FOREIGN KEY (TaskId)           REFERENCES Tasks(Id)            ON DELETE CASCADE,
                FOREIGN KEY (SourceRequestId)  REFERENCES ProjectRequests(Id)  ON DELETE SET NULL,
                FOREIGN KEY (ApprovedByUserId) REFERENCES users(Id)            ON DELETE SET NULL
            )");

        // ── TeamMilestoneDueDateOverrides — per-team milestone-level override ─
        // Same shape as TeamTaskDueDateOverrides but keyed on ProjectMilestoneId.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS TeamMilestoneDueDateOverrides (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                TeamId              INTEGER NOT NULL,
                ProjectMilestoneId  INTEGER NOT NULL,
                OriginalDueDate     TEXT,
                OverrideDueDate     TEXT    NOT NULL,
                SourceRequestId     INTEGER,
                ApprovedByUserId    INTEGER,
                CreatedAt           TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt           TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE (TeamId, ProjectMilestoneId),
                FOREIGN KEY (TeamId)              REFERENCES Teams(Id)              ON DELETE CASCADE,
                FOREIGN KEY (ProjectMilestoneId)  REFERENCES ProjectMilestones(Id)  ON DELETE CASCADE,
                FOREIGN KEY (SourceRequestId)     REFERENCES ProjectRequests(Id)    ON DELETE SET NULL,
                FOREIGN KEY (ApprovedByUserId)    REFERENCES users(Id)              ON DELETE SET NULL
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

        if (!submissionsColumns.Contains("CourseSubmittedAt"))
        {
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN CourseSubmittedAt TEXT");
            // Backfill: existing approved submissions are treated as already course-submitted
            // so they remain visible to course staff without requiring student action.
            await connection.ExecuteNonQueryAsync(@"
                UPDATE TaskSubmissions
                SET    CourseSubmittedAt = COALESCE(MentorReviewedAt, SubmittedAt)
                WHERE  MentorStatus = 'Approved'
                  AND  CourseSubmittedAt IS NULL");
        }

        // ── Lecturer/admin final-review fields (idempotent) ─────────────────────
        //  ReviewStatus           : 'PendingReview' | 'InReview' | 'FeedbackReturned' | 'FinalApproved'
        //                            Lives separately from Status — student/mentor
        //                            flows continue to use Status; this column is
        //                            for the lecturer review queue exclusively.
        //  IsFeedbackPublished    : 0 = draft (lecturer-only) | 1 = released to students
        //  FeedbackPublishedAt    : when the lecturer pressed "פרסם משוב"
        //  ReviewedByUserId       : the lecturer/admin who last saved the review
        if (!submissionsColumns.Contains("ReviewStatus"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN ReviewStatus TEXT NOT NULL DEFAULT 'PendingReview'");

        if (!submissionsColumns.Contains("IsFeedbackPublished"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN IsFeedbackPublished INTEGER NOT NULL DEFAULT 0");

        if (!submissionsColumns.Contains("FeedbackPublishedAt"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN FeedbackPublishedAt TEXT");

        if (!submissionsColumns.Contains("ReviewedByUserId"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN ReviewedByUserId INTEGER");

        // ── Lecturer-supervision columns ─────────────────────────────────────
        // Power the "אישורי מנחה ממתינים" inbox.
        //
        //  LastMentorReminderAt        : timestamp of the last lecturer-sent
        //                                reminder. Used to enforce a 24h
        //                                cool-off so reminders can't be spammed.
        //  LastMentorReminderByUserId  : audit — who sent the last reminder.
        //  ApprovalSource              : 'MentorApproved' | 'LecturerOverride'
        //                                set when a final approval lands.
        //  OverrideReason              : internal reason, lecturer/admin/mentor
        //                                only — never returned to students.
        //  OverrideApprovedByUserId / OverrideApprovedAt
        //                              : the lecturer who exercised the
        //                                override + when.
        if (!submissionsColumns.Contains("LastMentorReminderAt"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN LastMentorReminderAt TEXT");

        if (!submissionsColumns.Contains("LastMentorReminderByUserId"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN LastMentorReminderByUserId INTEGER");

        if (!submissionsColumns.Contains("ApprovalSource"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN ApprovalSource TEXT");

        if (!submissionsColumns.Contains("OverrideReason"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN OverrideReason TEXT");

        if (!submissionsColumns.Contains("OverrideApprovedByUserId"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN OverrideApprovedByUserId INTEGER");

        if (!submissionsColumns.Contains("OverrideApprovedAt"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN OverrideApprovedAt TEXT");

        // ── TaskSubmissions: escalation flag ─────────────────────────────────
        // Minimal "needs lecturer attention" toggle introduced 2026-05-17 when
        // the lecturer-final-review flow was retired. Lets a mentor flag a
        // single submission to the lecturer/admin without bringing back the
        // full review queue. No state machine — just a boolean + audit.
        if (!submissionsColumns.Contains("EscalatedToLecturer"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN EscalatedToLecturer INTEGER NOT NULL DEFAULT 0");
        if (!submissionsColumns.Contains("EscalatedAt"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN EscalatedAt TEXT");
        if (!submissionsColumns.Contains("EscalatedByUserId"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN EscalatedByUserId INTEGER");
        if (!submissionsColumns.Contains("EscalationReason"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN EscalationReason TEXT");

        // ── TaskSubmissions: Google Drive URL (2026-05-19) ───────────────────
        // The student submission flow no longer accepts uploaded files — the
        // only allowed payload is a publicly-shared Google Drive link.
        // TaskSubmissionFiles is intentionally left intact so historical
        // submissions still render their attachments for review.
        if (!submissionsColumns.Contains("DriveUrl"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN DriveUrl TEXT");

        // ── TaskSubmissions: manual Moodle-submission confirmation ──────────
        // Students click "הגשתי במודל" after Moodle is done; the server marks
        // the submission as officially handed in. CourseSubmittedAt (legacy,
        // pre-refactor) is left alone — read paths COALESCE the new column
        // with the old one so historical rows still display as submitted.
        if (!submissionsColumns.Contains("MoodleSubmittedAt"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN MoodleSubmittedAt TEXT");
        if (!submissionsColumns.Contains("MoodleSubmittedByUserId"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE TaskSubmissions ADD COLUMN MoodleSubmittedByUserId INTEGER");

        // ── Projects: manual Moodle submission tracking ──────────────────────
        // The Innovation Faculty's official deliverable submission happens in
        // Moodle; we don't have Moodle API access, so we expose a manual flag
        // for admins/lecturers to mark per-project status. One of:
        //   "SubmittedToMoodle" | "NotSubmittedToMoodle" | "Unknown"
        // NULL means "not tracked yet". No DB-side enum — string sentinel.
        var projectMoodleColumns = await GetColumnsAsync(connection, "Projects");
        if (!projectMoodleColumns.Contains("MoodleSubmissionStatus"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN MoodleSubmissionStatus TEXT");
        if (!projectMoodleColumns.Contains("MoodleSubmissionUpdatedAt"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN MoodleSubmissionUpdatedAt TEXT");
        if (!projectMoodleColumns.Contains("MoodleSubmissionUpdatedByUserId"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN MoodleSubmissionUpdatedByUserId INTEGER");
        if (!projectMoodleColumns.Contains("MoodleSubmissionNotes"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE Projects ADD COLUMN MoodleSubmissionNotes TEXT");

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

        // ── SlackIntegrations table ───────────────────────────────────────────
        // One row per user. Stores the OAuth result from Slack so the system can
        // later post DM notifications on the user's behalf.
        //   AccessToken    — bot/user token returned by Slack after OAuth
        //   SlackUserId    — Slack's internal user ID (used to open DMs)
        //   WebhookUrl     — incoming-webhook URL if the app requested that scope
        //   IsActive       — set to 0 on disconnect without deleting history
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS SlackIntegrations (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId        INTEGER NOT NULL UNIQUE,
                SlackUserId   TEXT    NOT NULL DEFAULT '',
                SlackTeamId   TEXT    NOT NULL DEFAULT '',
                SlackTeamName TEXT    NOT NULL DEFAULT '',
                AccessToken   TEXT    NOT NULL DEFAULT '',
                WebhookUrl    TEXT    NOT NULL DEFAULT '',
                ConnectedAt   TEXT    NOT NULL DEFAULT (datetime('now')),
                IsActive      INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE
            )");

        // ── GoogleCalendarConnections table ──────────────────────────────────
        // One row per user — the SINGLE source of truth for "this user has
        // authorized Motiva against their Google Calendar".
        //
        // UserPreferences.GoogleCalendarConnected is NOT that. It is a
        // self-declared checkbox that predates any OAuth flow; it is left in
        // place (nothing reads it for connection state) rather than dropped, so
        // this migration stays additive.
        //
        //   AccessTokenProtected  — access token, ASP.NET Core Data Protection
        //   RefreshTokenProtected — refresh token, same. NEVER plaintext: this
        //                           is a long-lived credential and the SQLite
        //                           file also ships as a demo DB.
        //   AccessTokenExpiresAt  — UTC, SQLite datetime() text format
        //   Scopes                — what Google actually granted (may differ
        //                           from what was requested)
        //   IsActive              — 0 on disconnect / invalid_grant. Both token
        //                           columns are wiped at the same time, so an
        //                           inactive row is inert, not dormant.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS GoogleCalendarConnections (
                Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId                INTEGER NOT NULL UNIQUE,
                GoogleEmail           TEXT    NOT NULL DEFAULT '',
                AccessTokenProtected  TEXT    NOT NULL DEFAULT '',
                RefreshTokenProtected TEXT    NOT NULL DEFAULT '',
                AccessTokenExpiresAt  TEXT,
                Scopes                TEXT    NOT NULL DEFAULT '',
                ConnectedAt           TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt             TEXT    NOT NULL DEFAULT (datetime('now')),
                IsActive              INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE
            )");

        // ── GoogleCalendarEventLinks table ───────────────────────────────────
        // The durable link between a Motiva task and the Google Calendar event a
        // user created for it. NOT a boolean "synced" flag: it stores the real
        // Google event id, which is what makes later update and delete possible.
        //
        // PERSONAL BY DESIGN. The row is keyed on (UserId, TaskType, TaskId), so
        // a team task can hold one row per team member and each one is that
        // member's own event on their own calendar. Nobody is invited to anyone
        // else's — shared invitations are the meetings phase.
        //
        //   GoogleEventId  — client-supplied id (base32hex), generated BEFORE the
        //                    insert and reused on retry. Together with the UNIQUE
        //                    index below this is what prevents duplicate events:
        //                    a retry re-sends the same id and Google answers 409
        //                    instead of creating a second event.
        //   SyncState      — 'pending' until Google confirms, then 'synced'. Only
        //                    'synced' may be shown to the user as "ביומן Google".
        //   ScheduledStart — Israel WALL-CLOCK 'yyyy-MM-dd HH:mm', not UTC. The
        //   ScheduledEnd     user picked a time on a clock, and the event is sent
        //                    to Google with an explicit IANA zone rather than as
        //                    an instant, so DST is Google's problem, not ours.
        //
        // No FK on TaskId: TaskType means the column can point at more than one
        // table later. Deletion is handled explicitly instead — see
        // GoogleCalendarEventService.RemoveLinksForDeletedTaskAsync.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS GoogleCalendarEventLinks (
                Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId         INTEGER NOT NULL,
                TaskId         INTEGER NOT NULL,
                TaskType       TEXT    NOT NULL DEFAULT 'TeamTask',
                GoogleEventId  TEXT    NOT NULL DEFAULT '',
                CalendarId     TEXT    NOT NULL DEFAULT 'primary',
                ScheduledStart TEXT    NOT NULL DEFAULT '',
                ScheduledEnd   TEXT    NOT NULL DEFAULT '',
                SyncState      TEXT    NOT NULL DEFAULT 'pending',
                CreatedAt      TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt      TEXT    NOT NULL DEFAULT (datetime('now')),
                IsActive       INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE
            )");

        // One row per user per task — the mutex behind duplicate prevention. It
        // covers inactive rows too, so a re-schedule reuses the row rather than
        // racing a second one into existence.
        await connection.ExecuteNonQueryAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS UX_GoogleCalendarEventLinks_User_Task " +
            "ON GoogleCalendarEventLinks(UserId, TaskType, TaskId)");

        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS IX_GoogleCalendarEventLinks_Task " +
            "ON GoogleCalendarEventLinks(TaskType, TaskId)");

        // ── OAuthStates table ────────────────────────────────────────────────
        // Short-lived, single-use CSRF states for outbound OAuth flows.
        //
        // Only the SHA-256 hash of the state is kept, so the table cannot be
        // read to forge a live authorization. The row — not the query string —
        // is what binds a callback to a Motiva user, which is why the state
        // itself carries no user id (unlike the older Slack Base64(userId)
        // state, deliberately not reused here).
        //
        // Provider-keyed so a second integration can share the mechanism.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS OAuthStates (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                Provider   TEXT    NOT NULL,
                StateHash  TEXT    NOT NULL UNIQUE,
                UserId     INTEGER NOT NULL,
                CreatedAt  TEXT    NOT NULL DEFAULT (datetime('now')),
                ExpiresAt  TEXT    NOT NULL,
                ConsumedAt TEXT,
                FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE
            )");

        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS IX_OAuthStates_Provider_Expiry " +
            "ON OAuthStates(Provider, ExpiresAt)");

        // ── UserPreferences table ─────────────────────────────────────────────
        // Per-user notification and integration preferences for the student
        // profile/settings page. UserId is the primary key so each user has at
        // most one row; missing rows are treated as all-defaults on read.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS UserPreferences (
                UserId                  INTEGER PRIMARY KEY,
                NotifyOnTasks           INTEGER NOT NULL DEFAULT 1,
                NotifyOnDeadlines       INTEGER NOT NULL DEFAULT 1,
                NotifyOnFeedback        INTEGER NOT NULL DEFAULT 1,
                NotifyOnSubmissions     INTEGER NOT NULL DEFAULT 1,
                NotifyOnMentorUpdates   INTEGER NOT NULL DEFAULT 1,
                GoogleCalendarConnected INTEGER NOT NULL DEFAULT 0,
                SlackConnected          INTEGER NOT NULL DEFAULT 0,
                UpdatedAt               TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE
            )");

        // ── UserPreferences: ThemePreference column ───────────────────────────
        // Added after the initial schema; existing rows default to 'system'.
        var prefColumns = await GetColumnsAsync(connection, "UserPreferences");
        if (!prefColumns.Contains("ThemePreference"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE UserPreferences ADD COLUMN ThemePreference TEXT NOT NULL DEFAULT 'system'");

        // ── IntegrationSettings table ────────────────────────────────────────
        // System-level OAuth configuration for third-party integrations.
        // One row per provider (e.g. 'Slack'). ClientSecret is stored here only;
        // it is never included in API responses sent to the client.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS IntegrationSettings (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                Provider     TEXT    NOT NULL UNIQUE,
                ClientId     TEXT    NOT NULL DEFAULT '',
                ClientSecret TEXT    NOT NULL DEFAULT '',
                RedirectUri  TEXT    NOT NULL DEFAULT '',
                Scopes       TEXT    NOT NULL DEFAULT '',
                IsEnabled    INTEGER NOT NULL DEFAULT 0,
                UpdatedAt    TEXT    NOT NULL DEFAULT (datetime('now'))
            )");

        // ── AirtableIntegrationSettings table ────────────────────────────────
        // Per-academic-year Airtable configuration. The ApiToken column stores
        // the Personal Access Token; it is never included in GET responses
        // (controllers expose a masked summary instead).
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS AirtableIntegrationSettings (
                Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                AcademicYearId     INTEGER NOT NULL,
                Name               TEXT    NOT NULL DEFAULT '',
                ApiToken           TEXT    NOT NULL DEFAULT '',
                BaseId             TEXT    NOT NULL DEFAULT '',
                ProjectsTable      TEXT    NOT NULL DEFAULT '',
                ProjectsView       TEXT    NOT NULL DEFAULT '',
                MentorsTable       TEXT    NOT NULL DEFAULT '',
                MentorsView        TEXT    NOT NULL DEFAULT '',
                StudentsTable      TEXT    NOT NULL DEFAULT '',
                StudentsView       TEXT    NOT NULL DEFAULT '',
                TeamsTable         TEXT    NOT NULL DEFAULT '',
                TeamsView          TEXT    NOT NULL DEFAULT '',
                StudentVisibleOnly INTEGER NOT NULL DEFAULT 1,
                IsActive           INTEGER NOT NULL DEFAULT 0,
                LastTestedAt       TEXT,
                LastTestStatus     TEXT,
                LastImportAt       TEXT,
                LastImportSummary  TEXT,
                CreatedAt          TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt          TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (AcademicYearId) REFERENCES AcademicYears(Id) ON DELETE CASCADE
            )");

        // ── AirtableFieldMappings table ──────────────────────────────────────
        // Per-integration mapping of LocalFieldName → AirtableFieldName.
        // EntityType currently 'Project' (more entity types in the future).
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS AirtableFieldMappings (
                Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                IntegrationSettingsId INTEGER NOT NULL,
                EntityType            TEXT    NOT NULL,
                LocalFieldName        TEXT    NOT NULL,
                AirtableFieldName     TEXT    NOT NULL DEFAULT '',
                IsRequired            INTEGER NOT NULL DEFAULT 0,
                CreatedAt             TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt             TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE (IntegrationSettingsId, EntityType, LocalFieldName),
                FOREIGN KEY (IntegrationSettingsId) REFERENCES AirtableIntegrationSettings(Id) ON DELETE CASCADE
            )");

        // ── Permissions catalog ──────────────────────────────────────────────
        // Master list of permission keys the application understands. Seeded
        // idempotently on every startup so newly added keys appear without
        // manual migrations.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS Permissions (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Key         TEXT    NOT NULL UNIQUE,
                DisplayName TEXT    NOT NULL,
                GroupName   TEXT    NOT NULL,
                Description TEXT    NOT NULL DEFAULT '',
                SortOrder   INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now'))
            )");

        // ── Role → permission assignments ────────────────────────────────────
        // Many-to-many (RoleName, PermissionKey). Role gates ([Authorize(Roles=...)])
        // remain the primary access boundary; permissions refine UI/feature access.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS RolePermissions (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                RoleName      TEXT    NOT NULL,
                PermissionKey TEXT    NOT NULL,
                CreatedAt     TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE (RoleName, PermissionKey)
            )");

        // ── RoleSettings — simple feature-flag matrix per role ────────────────
        // Wide table, one row per role, one column per flag. Decoupled from the
        // larger Permissions/RolePermissions key-based system above; the two
        // coexist. The PermissionService surface that pages will eventually
        // consume reads from this table.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS RoleSettings (
                RoleName                  TEXT    PRIMARY KEY,
                CanManageRequests         INTEGER NOT NULL DEFAULT 0,
                CanManageMilestones       INTEGER NOT NULL DEFAULT 0,
                CanManageAssignments      INTEGER NOT NULL DEFAULT 0,
                CanManageUsers            INTEGER NOT NULL DEFAULT 0,
                CanManageAirtable         INTEGER NOT NULL DEFAULT 0,
                CanOpenRequests           INTEGER NOT NULL DEFAULT 0,
                CanViewTasks              INTEGER NOT NULL DEFAULT 0,
                CanSubmitTasks            INTEGER NOT NULL DEFAULT 0,
                CanViewLecturerDashboard  INTEGER NOT NULL DEFAULT 0,
                UpdatedAt                 TEXT    NOT NULL DEFAULT (datetime('now'))
            )");

        // Seed sensible defaults per role. INSERT OR IGNORE keeps it idempotent —
        // an admin who flipped a flag won't have it reset on the next migration.
        // Tuple order: ManageRequests, ManageMilestones, ManageAssignments,
        // ManageUsers, ManageAirtable, OpenRequests, ViewTasks, SubmitTasks,
        // ViewLecturerDashboard.
        var roleSettingsSeeds = new (string Role,
                                     int MR, int MM, int MA, int MU, int MAir,
                                     int OR, int VT, int ST, int VLD)[]
        {
            ("Admin",   1,1,1,1,1, 1,1,0,1),
            ("Staff",   1,1,1,0,0, 1,1,0,1),
            ("Mentor",  1,0,0,0,0, 1,1,0,0),
            ("Student", 0,0,0,0,0, 1,1,1,0),
            ("User",    0,0,0,0,0, 0,0,0,0),
        };
        foreach (var s in roleSettingsSeeds)
        {
            await connection.ExecuteNonQueryAsync(
                "INSERT OR IGNORE INTO RoleSettings " +
                "(RoleName, CanManageRequests, CanManageMilestones, CanManageAssignments, " +
                " CanManageUsers, CanManageAirtable, CanOpenRequests, CanViewTasks, " +
                " CanSubmitTasks, CanViewLecturerDashboard) VALUES " +
                $"('{s.Role}', {s.MR}, {s.MM}, {s.MA}, {s.MU}, {s.MAir}, " +
                $" {s.OR}, {s.VT}, {s.ST}, {s.VLD})");
        }

        // Seed the canonical permission catalog.
        var permissionSeeds = new (string Key, string Display, string Group, string Desc, int Sort)[]
        {
            // Project catalog (student-facing)
            ("ProjectCatalog.View",                  "צפייה בקטלוג הפרויקטים", "ProjectCatalog",        "סטודנטים יכולים לצפות בקטלוג", 10),
            ("AssignmentForm.Submit",                "הגשת טופס שיבוץ",         "ProjectCatalog",        "הגשה ראשונית של טופס שיבוץ פרויקט", 11),
            ("AssignmentForm.Edit",                  "עריכת הגשה קיימת",         "ProjectCatalog",        "עדכון הגשת טופס שיבוץ קיימת", 12),

            // Assignment management (admin)
            ("AssignmentManagement.ViewSubmissions", "צפייה בכל ההגשות",         "AssignmentManagement",  "צפייה בטפסים שהוגשו על ידי כל הצוותים", 20),
            ("AssignmentManagement.ManageForm",      "ניהול טופס השיבוץ",       "AssignmentManagement",  "פתיחה/סגירה ועריכת טופס השיבוץ", 21),
            ("AssignmentManagement.ViewAnalytics",   "צפייה באנליטיקת שיבוצים",  "AssignmentManagement",  "צפייה בציוני ביקוש והתאמה", 22),
            ("AssignmentManagement.ManualAssign",    "שיבוץ ידני של צוותים",     "AssignmentManagement",  "שיבוץ צוותים לפרויקטים ושינוי שיבוצים", 23),
            ("AssignmentManagement.AssignMentor",    "שיוך מנטור לפרויקט",       "AssignmentManagement",  "הוספה והסרת מנטורים מפרויקטים", 24),
            ("AssignmentManagement.Publish",         "פרסום שיבוצים",            "AssignmentManagement",  "פרסום השיבוצים לסטודנטים", 25),

            // Project management (admin)
            ("ProjectManagement.View",               "צפייה בפרויקטים",          "ProjectManagement",     "צפייה ברשימת הפרויקטים והקטלוג", 30),
            ("ProjectManagement.Create",             "יצירת פרויקט",             "ProjectManagement",     "הוספת פרויקטים חדשים לקטלוג", 31),
            ("ProjectManagement.Edit",               "עריכת פרויקט",             "ProjectManagement",     "עדכון פרטי פרויקט קיים", 32),
            ("ProjectManagement.Delete",             "מחיקת פרויקט",             "ProjectManagement",     "הסרת פרויקטים מהמערכת", 33),
            ("ProjectManagement.ImportAirtable",     "ייבוא פרויקטים מ-Airtable","ProjectManagement",     "הפעלת ייבוא Airtable", 34),

            // Forms management
            ("FormsManagement.View",                 "צפייה בטפסים",             "FormsManagement",       "צפייה ברשימת הטפסים", 40),
            ("FormsManagement.Edit",                 "יצירה/עריכת טפסים",        "FormsManagement",       "עריכת הגדרות, בלוקים ואפשרויות", 41),
            ("FormsManagement.Delete",               "מחיקת טפסים",              "FormsManagement",       "מחיקת טפסים שאינם בשימוש", 42),

            // Integrations
            ("Integrations.View",                    "צפייה באינטגרציות",        "Integrations",          "צפייה בהגדרות האינטגרציות", 50),
            ("Integrations.ManageAirtable",          "ניהול Airtable",           "Integrations",          "יצירה ועריכת תצורות Airtable", 51),
            ("Integrations.TestAirtable",            "בדיקת חיבור Airtable",      "Integrations",          "הרצת בדיקת חיבור", 52),
            ("Integrations.RunAirtableImport",       "ייבוא Airtable",           "Integrations",          "הרצת ייבוא בפועל", 53),

            // Users / teams / mentors
            ("Users.View",                           "צפייה במשתמשים",           "UsersTeamsMentors",     "צפייה ברשימת המשתמשים", 60),
            ("Users.Manage",                         "ניהול משתמשים",            "UsersTeamsMentors",     "הוספה, עריכה והסרה של משתמשים", 61),
            ("Teams.Manage",                         "ניהול צוותים",             "UsersTeamsMentors",     "ניהול הרכב הצוותים", 62),
            ("Mentors.Manage",                       "ניהול מנטורים",            "UsersTeamsMentors",     "ניהול מנטורים והשיוך שלהם", 63),

            // Permissions itself (only Admin)
            ("Permissions.Manage",                   "ניהול הרשאות",             "System",                "עריכת הרשאות לפי תפקיד", 90),
        };

        foreach (var p in permissionSeeds)
        {
            await connection.ExecuteNonQueryAsync(
                "INSERT OR IGNORE INTO Permissions (Key, DisplayName, GroupName, Description, SortOrder) " +
                $"VALUES ('{p.Key.Replace("'", "''")}', '{p.Display.Replace("'", "''")}', '{p.Group.Replace("'", "''")}', '{p.Desc.Replace("'", "''")}', {p.Sort})");
        }

        // Default role → permission mapping. INSERT OR IGNORE keeps user-edited
        // assignments intact across restarts.
        var roleSeeds = new (string Role, string[] Keys)[]
        {
            ("Student", new[]
            {
                "ProjectCatalog.View", "AssignmentForm.Submit", "AssignmentForm.Edit"
            }),
            ("Admin", new[]
            {
                "AssignmentManagement.ViewSubmissions", "AssignmentManagement.ManageForm",
                "AssignmentManagement.ViewAnalytics", "AssignmentManagement.ManualAssign",
                "AssignmentManagement.AssignMentor", "AssignmentManagement.Publish",
                "ProjectManagement.View", "ProjectManagement.Create", "ProjectManagement.Edit",
                "ProjectManagement.Delete", "ProjectManagement.ImportAirtable",
                "FormsManagement.View", "FormsManagement.Edit", "FormsManagement.Delete",
                "Integrations.View", "Integrations.ManageAirtable",
                "Integrations.TestAirtable", "Integrations.RunAirtableImport",
                "Users.View", "Users.Manage", "Teams.Manage", "Mentors.Manage",
                "Permissions.Manage"
            }),
            ("Staff", new[]
            {
                "AssignmentManagement.ViewSubmissions", "AssignmentManagement.ManageForm",
                "AssignmentManagement.ViewAnalytics", "AssignmentManagement.ManualAssign",
                "AssignmentManagement.AssignMentor",
                "ProjectManagement.View", "ProjectManagement.Create", "ProjectManagement.Edit",
                "ProjectManagement.ImportAirtable",
                "FormsManagement.View", "FormsManagement.Edit",
                "Integrations.View", "Integrations.RunAirtableImport",
                "Users.View", "Teams.Manage", "Mentors.Manage"
            }),
            ("Mentor", Array.Empty<string>()),
        };

        foreach (var r in roleSeeds)
        {
            foreach (var key in r.Keys)
            {
                await connection.ExecuteNonQueryAsync(
                    "INSERT OR IGNORE INTO RolePermissions (RoleName, PermissionKey) " +
                    $"VALUES ('{r.Role.Replace("'", "''")}', '{key.Replace("'", "''")}')");
            }
        }

        // ── ProjectMentors table ─────────────────────────────────────────────
        // Many-to-many junction: links mentor users to the projects they supervise.
        // UNIQUE(ProjectId, UserId) prevents duplicate assignments.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ProjectMentors (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId  INTEGER NOT NULL,
                UserId     INTEGER NOT NULL,
                AssignedAt TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE (ProjectId, UserId),
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (UserId)    REFERENCES users(Id)    ON DELETE CASCADE
            )");

        // ── StudentProjectFavorites table ─────────────────────────────────────
        // Students bookmark projects they are interested in before submitting
        // the assignment / preference form.
        // UNIQUE(UserId, ProjectId) prevents duplicate bookmarks.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS StudentProjectFavorites (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId    INTEGER NOT NULL,
                ProjectId INTEGER NOT NULL,
                CreatedAt TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE (UserId, ProjectId),
                FOREIGN KEY (UserId)    REFERENCES users(Id)    ON DELETE CASCADE,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
            )");

        // ── Notifications table ───────────────────────────────────────────────
        // In-system notification feed per user.
        // Type: 'SubmissionReceived' | 'SubmissionReturned' | 'General'
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS Notifications (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId            INTEGER NOT NULL,
                Title             TEXT    NOT NULL,
                Message           TEXT    NOT NULL,
                Type              TEXT    NOT NULL DEFAULT 'General',
                RelatedEntityType TEXT,
                RelatedEntityId   INTEGER,
                IsRead            INTEGER NOT NULL DEFAULT 0,
                CreatedAt         TEXT    NOT NULL DEFAULT (datetime('now')),
                ReadAt            TEXT,
                FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE
            )");

        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_notifications_user ON Notifications(UserId, CreatedAt DESC)");

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

        // ── StudentStrengths table ────────────────────────────────────────────
        // Records which domain strengths each student selected in the assignment form.
        // Strength values: 'Design' | 'Content' | 'Technology' | 'ProjectManagement'
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS StudentStrengths (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId    INTEGER NOT NULL,
                Strength  TEXT    NOT NULL,
                CreatedAt TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE (UserId, Strength),
                FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE
            )");

        // ── TeamProjectPreferences table ──────────────────────────────────────
        // Ordered project preferences submitted by a team (up to 3 priorities).
        // UNIQUE(TeamId, Priority) ensures only one project per rank.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS TeamProjectPreferences (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                TeamId    INTEGER NOT NULL,
                Priority  INTEGER NOT NULL,
                ProjectId INTEGER NOT NULL,
                UNIQUE (TeamId, Priority),
                FOREIGN KEY (TeamId)    REFERENCES Teams(Id)    ON DELETE CASCADE,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
            )");

        // ── AssignmentFormSubmissions table ───────────────────────────────────
        // One record per team; stores the free-text fields from the preference form.
        // UNIQUE(TeamId) enforced via ON CONFLICT UPDATE (upsert).
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS AssignmentFormSubmissions (
                Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                TeamId               INTEGER NOT NULL UNIQUE,
                HasOwnProject        INTEGER NOT NULL DEFAULT 0,
                OwnProjectDescription TEXT,
                Notes                TEXT,
                SubmittedAt          TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (TeamId) REFERENCES Teams(Id) ON DELETE CASCADE
            )");

        // ── AssignmentSettings table ──────────────────────────────────────────
        // Per academic-year publish flag for assignments. While
        // AssignmentsPublished = 0, all draft project assignments
        // (Projects.AssignmentIsDraft = 1) stay hidden from students.
        // Publishing flips all drafts in the year to visible at once.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS AssignmentSettings (
                AcademicYearId       INTEGER PRIMARY KEY,
                AssignmentsPublished INTEGER NOT NULL DEFAULT 0,
                PublishedAt          TEXT,
                UpdatedAt            TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (AcademicYearId) REFERENCES AcademicYears(Id) ON DELETE CASCADE
            )");

        // ── Reusable form-builder system ──────────────────────────────────────
        // Forms              : top-level form (one per AcademicYear+FormType)
        // FormBlocks         : ordered blocks (sections / fields) inside a form
        // FormBlockOptions   : options for choice/ranking blocks
        //
        // The existing student assignment-form submissions still live in their
        // dedicated tables (AssignmentFormSubmissions / TeamProjectPreferences /
        // StudentStrengths). These three tables describe the FORM STRUCTURE and
        // SETTINGS only.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS Forms (
                Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                AcademicYearId       INTEGER NOT NULL,
                Name                 TEXT    NOT NULL,
                FormType             TEXT    NOT NULL,
                Instructions         TEXT,
                IsOpen               INTEGER NOT NULL DEFAULT 0,
                OpensAt              TEXT,
                ClosesAt             TEXT,
                AllowEditAfterSubmit INTEGER NOT NULL DEFAULT 1,
                Status               TEXT    NOT NULL DEFAULT 'Draft',
                CreatedAt            TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt            TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE (AcademicYearId, FormType),
                FOREIGN KEY (AcademicYearId) REFERENCES AcademicYears(Id) ON DELETE CASCADE
            )");

        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS FormBlocks (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                FormId      INTEGER NOT NULL,
                BlockType   TEXT    NOT NULL,
                BlockKey    TEXT,
                Title       TEXT    NOT NULL,
                HelperText  TEXT,
                IsRequired  INTEGER NOT NULL DEFAULT 0,
                SortOrder   INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt   TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (FormId) REFERENCES Forms(Id) ON DELETE CASCADE
            )");

        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS FormBlockOptions (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                FormBlockId INTEGER NOT NULL,
                OptionValue TEXT    NOT NULL,
                OptionLabel TEXT    NOT NULL,
                SortOrder   INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (FormBlockId) REFERENCES FormBlocks(Id) ON DELETE CASCADE
            )");

        // ── External Forms (Innovation Team iframe-embedded forms) ───────────
        // These forms live OUTSIDE Gradify — they're hosted by the Innovation
        // Team and embedded via iframe. We only store metadata + URL so
        // admins/lecturers can swap the link per academic year without code
        // changes. AcademicYearId is nullable so forms can be marked global
        // (e.g. an evergreen feedback form not tied to a specific cycle).
        //
        // IsDeleted is a soft-delete flag — rows stay in the table for audit
        // purposes and are filtered out of every read query.
        // *ByUserId columns power the audit log on the controller side.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ExternalForms (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                Name            TEXT    NOT NULL,
                Description     TEXT    NOT NULL DEFAULT '',
                FormType        TEXT    NOT NULL DEFAULT '',
                IframeUrl       TEXT    NOT NULL DEFAULT '',
                IsActive        INTEGER NOT NULL DEFAULT 1,
                IsDeleted       INTEGER NOT NULL DEFAULT 0,
                AcademicYearId  INTEGER,
                CreatedByUserId INTEGER,
                UpdatedByUserId INTEGER,
                DeletedByUserId INTEGER,
                DeletedAt       TEXT,
                CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (AcademicYearId) REFERENCES AcademicYears(Id) ON DELETE SET NULL
            )");

        // Idempotent ALTERs so databases created before the audit/soft-delete
        // columns existed get them on the next start.
        var efColumns = await GetColumnsAsync(connection, "ExternalForms");
        if (!efColumns.Contains("IsDeleted"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE ExternalForms ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0");
        if (!efColumns.Contains("CreatedByUserId"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE ExternalForms ADD COLUMN CreatedByUserId INTEGER");
        if (!efColumns.Contains("UpdatedByUserId"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE ExternalForms ADD COLUMN UpdatedByUserId INTEGER");
        if (!efColumns.Contains("DeletedByUserId"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE ExternalForms ADD COLUMN DeletedByUserId INTEGER");
        if (!efColumns.Contains("DeletedAt"))
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE ExternalForms ADD COLUMN DeletedAt TEXT");

        // ── External Forms audit log ─────────────────────────────────────────
        // One row per mutation (Create / Update / Delete / Toggle). Stores the
        // old + new value snapshots as JSON; the controller is responsible for
        // serialising them.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ExternalFormAuditLog (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                ExternalFormId  INTEGER NOT NULL,
                Action          TEXT    NOT NULL,
                ChangedByUserId INTEGER,
                OldValuesJson   TEXT    NOT NULL DEFAULT '',
                NewValuesJson   TEXT    NOT NULL DEFAULT '',
                ChangedAt       TEXT    NOT NULL DEFAULT (datetime('now'))
            )");

        // ── External Requests (status updates pushed by Innovation Team) ─────
        // The Innovation Team owns the source of truth; we receive status
        // pushes via /api/external-requests/update (X-External-Api-Key auth).
        // The shape is intentionally loose:
        //  • ExternalRequestId is the upstream key — UNIQUE so re-pushes upsert.
        //  • StudentId / ProjectId are nullable — we may only have an email.
        //  • RawPayload stores the full JSON for forensics and lets us evolve
        //    the parsed columns later without losing data.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ExternalRequests (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                ExternalRequestId TEXT    NOT NULL UNIQUE,
                StudentEmail      TEXT    NOT NULL DEFAULT '',
                StudentId         INTEGER,
                ProjectId         INTEGER,
                RequestType       TEXT    NOT NULL DEFAULT '',
                Status            TEXT    NOT NULL DEFAULT '',
                StatusLabel       TEXT    NOT NULL DEFAULT '',
                Notes             TEXT    NOT NULL DEFAULT '',
                RawPayload        TEXT    NOT NULL DEFAULT '',
                CreatedAt         TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt         TEXT    NOT NULL DEFAULT (datetime('now'))
            )");

        // ── External Requests — status timeline ──────────────────────────────
        // Captures every status transition so the UI can show a history later.
        // One row per change; the most recent row matches ExternalRequests.Status.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ExternalRequestStatusHistory (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                ExternalRequestId TEXT    NOT NULL,
                OldStatus         TEXT    NOT NULL DEFAULT '',
                NewStatus         TEXT    NOT NULL DEFAULT '',
                OldStatusLabel    TEXT    NOT NULL DEFAULT '',
                NewStatusLabel    TEXT    NOT NULL DEFAULT '',
                Notes             TEXT    NOT NULL DEFAULT '',
                ChangedAt         TEXT    NOT NULL DEFAULT (datetime('now')),
                RawPayload        TEXT    NOT NULL DEFAULT ''
            )");

        // ── External Requests — failed inbound payloads ──────────────────────
        // We persist every webhook body we couldn't process (bad JSON, missing
        // ExternalRequestId, unexpected error) so the Innovation Team has
        // something to investigate. Kept small on purpose; no PII parsing.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ExternalRequestFailedPayloads (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                ReceivedAt    TEXT    NOT NULL DEFAULT (datetime('now')),
                RemoteIp      TEXT    NOT NULL DEFAULT '',
                ErrorMessage  TEXT    NOT NULL DEFAULT '',
                RawPayload    TEXT    NOT NULL DEFAULT ''
            )");

        // ── Indexes ──────────────────────────────────────────────────────────
        // Hot lookups: webhook upsert by ExternalRequestId, student "/me" list
        // by StudentId/Email, admin list filtered by year/active.
        // CREATE INDEX IF NOT EXISTS is idempotent.
        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_ExternalRequests_StudentEmail ON ExternalRequests (StudentEmail)");
        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_ExternalRequests_StudentId    ON ExternalRequests (StudentId)");
        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_ExternalRequests_UpdatedAt    ON ExternalRequests (UpdatedAt)");
        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_ExternalForms_AcademicYearId  ON ExternalForms (AcademicYearId)");
        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_ExternalForms_IsActive        ON ExternalForms (IsActive)");
        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_ExternalForms_IsDeleted       ON ExternalForms (IsDeleted)");
        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_ExternalRequestStatusHistory_Eid ON ExternalRequestStatusHistory (ExternalRequestId)");
        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_ExternalFormAuditLog_FormId   ON ExternalFormAuditLog (ExternalFormId)");

        // ── Innovation-Team field & status mapping tables ────────────────────
        // Admin-configurable mappings that translate the *external* payload
        // (e.g. Airtable column names + Hebrew status values) into our
        // internal canonical fields and ExternalRequestStatuses tokens.
        // The webhook applies these transparently — when no rows exist, it
        // behaves identically to the previous version.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ExternalIntegrationFieldMappings (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceSystem    TEXT    NOT NULL DEFAULT 'Airtable',
                SourceFieldName TEXT    NOT NULL,
                TargetFieldName TEXT    NOT NULL,
                IsRequired      INTEGER NOT NULL DEFAULT 0,
                DefaultValue    TEXT    NOT NULL DEFAULT '',
                IsActive        INTEGER NOT NULL DEFAULT 1,
                Notes           TEXT    NOT NULL DEFAULT '',
                CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE (SourceSystem, SourceFieldName, TargetFieldName)
            )");

        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ExternalIntegrationStatusMappings (
                Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceSystem      TEXT    NOT NULL DEFAULT 'Airtable',
                SourceStatusValue TEXT    NOT NULL,
                InternalStatus    TEXT    NOT NULL,
                DisplayLabel      TEXT    NOT NULL DEFAULT '',
                IsTerminal        INTEGER NOT NULL DEFAULT 0,
                IsActive          INTEGER NOT NULL DEFAULT 1,
                CreatedAt         TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt         TEXT    NOT NULL DEFAULT (datetime('now')),
                UNIQUE (SourceSystem, SourceStatusValue)
            )");

        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_ExtIntField_System  ON ExternalIntegrationFieldMappings  (SourceSystem, IsActive)");
        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_ExtIntStatus_System ON ExternalIntegrationStatusMappings (SourceSystem, IsActive)");

        // ── Lecturer ⇒ Admin invariant ──────────────────────────────────────
        // Business rule: every user with the Staff role ("מרצה" / Lecturer)
        // MUST also have the Admin role. Enforced at the DB layer so it
        // survives every write path (admin endpoints, scripts, future code).
        await EnsureLecturerImpliesAdminAsync(connection);

        // ── Request type definitions + per-type configurable fields ──────────
        // Backs the admin-managed catalogue at /management/request-types.
        // Existing 7 built-in request types are seeded once so historical
        // ProjectRequests rows (which reference the Code string) keep working.
        await EnsureRequestTypeDefinitionsAsync(connection);

        // ── Roadmap stages — per-cycle "ציר התקדמות פרויקט" config ───────────
        // A thin layer on top of AcademicYearMilestones: each stage is a
        // per-cycle row with Hebrew name, suggested dates, and display order.
        // Milestone-templates are bound to a stage via the new
        // AcademicYearMilestones.RoadmapStageId column. Seven default stages
        // are seeded per existing cycle from the mentor-guide structure;
        // year-specific dates are intentionally left NULL so admins fill them.
        await EnsureRoadmapStagesAsync(connection);

        // ── Airtable bootstrap: one-time seed from appsettings.json ──────────
        // The DB is the sole runtime source of Airtable integration config.
        // For existing dev/staging machines that still have an "Airtable"
        // section in appsettings.json, this migrator block copies that
        // section once into a per-current-cycle AirtableIntegrationSettings
        // row so nothing breaks on the very first boot after the appsettings
        // fallback is removed from AirtableService. Strictly idempotent:
        // skipped when (a) no current cycle is set, (b) a row already
        // exists for that cycle, or (c) the appsettings token/baseId are
        // empty. Re-running the migrator does nothing on subsequent boots.
        await EnsureAirtableSeedFromAppsettingsAsync(connection, config);

        // ── Import data-integrity guards + lightweight audit trail ──────────
        // Two partial UNIQUE indexes close existing race windows around
        // user IdNumber and project AirtableRecordId. Plus a small
        // AirtableImportRuns table for post-mortem visibility on each
        // Airtable import (was previously only in-memory + a truncated
        // text blob on AirtableIntegrationSettings.LastImportSummary).
        await EnsureImportIntegrityGuardsAsync(connection);

        // ── Selection-milestone state invariant ─────────────────────────────
        // Business rule: any project that has been published-assigned to a
        // team (TeamId IS NOT NULL AND AssignmentIsDraft = 0) implies the
        // "selection & assignment" milestone is already done. Existing data
        // pre-dates the publish hook so a one-time backfill is required.
        // Subsequent publishes maintain the invariant via the publish flow
        // in AssignmentManagementController.
        await EnsureSelectionMilestoneBackfillAsync(connection);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EnsureLecturerImpliesAdminAsync
    //
    //  Enforces "Staff ⇒ Admin" at the database layer in four steps, all
    //  idempotent and safe to re-run on every startup:
    //
    //    1. Dedupe any accidental duplicate (UserId, Role) rows so a UNIQUE
    //       index can be added (rows already inserted by older code paths
    //       that lacked WHERE NOT EXISTS guards).
    //    2. UNIQUE index on (UserId, Role). Required by the INSERT OR IGNORE
    //       used in the trigger and backfill below; also prevents duplicate
    //       role rows going forward.
    //    3. Backfill: for every existing Staff user that lacks Admin, insert
    //       the missing Admin row.
    //    4. Triggers:
    //         • AFTER INSERT of Staff           → also insert Admin
    //         • AFTER DELETE of Admin (while
    //           the user still has Staff)       → cascade-delete Staff
    //       These guarantee the invariant under any future write path,
    //       including legacy `AdminController.UpdateUser` which wipes all
    //       non-User roles and inserts a single new primary role.
    //
    //  Note: SQLite defaults to PRAGMA recursive_triggers = OFF, so the
    //  Admin insert performed by the INSERT trigger does not re-fire it.
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task EnsureLecturerImpliesAdminAsync(SqliteConnection connection)
    {
        // 1. Dedupe (UserId, Role) — keep the lowest Id per pair.
        await connection.ExecuteNonQueryAsync(@"
            DELETE FROM UserRoles
            WHERE  Id NOT IN (
                SELECT MIN(Id) FROM UserRoles GROUP BY UserId, Role
            )");

        // 2. UNIQUE index — idempotent.
        await connection.ExecuteNonQueryAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS ux_userroles_user_role ON UserRoles(UserId, Role)");

        // 3. Backfill missing Admin rows for existing Lecturers.
        await connection.ExecuteNonQueryAsync(@"
            INSERT OR IGNORE INTO UserRoles (UserId, Role)
            SELECT DISTINCT UserId, 'Admin'
            FROM   UserRoles
            WHERE  Role = 'Staff'");

        // 4a. INSERT trigger — Staff inserted ⇒ ensure Admin row exists.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TRIGGER IF NOT EXISTS trg_userroles_staff_implies_admin_ins
            AFTER INSERT ON UserRoles
            WHEN NEW.Role = 'Staff'
            BEGIN
                INSERT OR IGNORE INTO UserRoles (UserId, Role)
                VALUES (NEW.UserId, 'Admin');
            END");

        // 4b. DELETE trigger — Admin removed from a Lecturer ⇒ also remove
        //     their Staff row so the invariant holds. Application-level
        //     validation in AuthRepository.ChangeRoles surfaces a Hebrew
        //     error before reaching here, but this trigger guarantees the
        //     invariant under any code path that bypasses that validation
        //     (raw SQL, future endpoints, admin-edit wholesale-replace).
        await connection.ExecuteNonQueryAsync(@"
            CREATE TRIGGER IF NOT EXISTS trg_userroles_admin_removal_cascade
            AFTER DELETE ON UserRoles
            WHEN OLD.Role = 'Admin'
              AND EXISTS (SELECT 1 FROM UserRoles
                          WHERE UserId = OLD.UserId AND Role = 'Staff')
            BEGIN
                DELETE FROM UserRoles
                WHERE  UserId = OLD.UserId AND Role = 'Staff';
            END");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EnsureRequestTypeDefinitionsAsync
    //
    //  Two tables back the admin-managed request type catalogue:
    //
    //    RequestTypeDefinitions — master row per type. Code is the stable
    //      string identifier stored in ProjectRequests.RequestType. Seed
    //      codes match the legacy RequestTypes constants 1:1 so existing
    //      data keeps resolving to a label.
    //
    //    RequestTypeFields — child rows, the configurable form-field
    //      builder. FieldType is one of the controlled values in
    //      RequestTypeFieldTypes (ShortText / LongText / Date / File /
    //      Select / Checkbox).
    //
    //  Seed: the 7 built-in types (IsBuiltIn = 1). Built-in types can be
    //  edited and deactivated but never hard-deleted — the controller
    //  enforces that, the seed sets the flag.
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task EnsureRequestTypeDefinitionsAsync(SqliteConnection connection)
    {
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS RequestTypeDefinitions (
                Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                Code               TEXT    NOT NULL UNIQUE,
                Name               TEXT    NOT NULL,
                Description        TEXT    NOT NULL DEFAULT '',
                IsActive           INTEGER NOT NULL DEFAULT 1,
                HandlerRole        TEXT    NOT NULL DEFAULT 'Admin,Staff',
                AllowInternalNotes INTEGER NOT NULL DEFAULT 0,
                FileUploadPolicy   TEXT    NOT NULL DEFAULT 'Optional',
                IsBuiltIn          INTEGER NOT NULL DEFAULT 0,
                CreatedAt          TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt          TEXT    NOT NULL DEFAULT (datetime('now'))
            )");

        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS RequestTypeFields (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                RequestTypeId   INTEGER NOT NULL,
                Label           TEXT    NOT NULL,
                FieldKey        TEXT    NOT NULL DEFAULT '',
                FieldType       TEXT    NOT NULL,
                IsRequired      INTEGER NOT NULL DEFAULT 0,
                Options         TEXT    NOT NULL DEFAULT '',
                SortOrder       INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (RequestTypeId) REFERENCES RequestTypeDefinitions(Id) ON DELETE CASCADE
            )");

        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_rtf_typeid ON RequestTypeFields(RequestTypeId)");

        // Idempotent seed of the 7 historical built-in types. INSERT OR IGNORE
        // means re-running the migrator on an already-seeded DB is a no-op.
        await connection.ExecuteNonQueryAsync(@"
            INSERT OR IGNORE INTO RequestTypeDefinitions
                (Code, Name, Description, IsActive, HandlerRole, AllowInternalNotes, FileUploadPolicy, IsBuiltIn)
            VALUES
                ('Extension',                 'בקשת דחייה',         'בקשה לדחיית מועד הגשת משימה או אבן דרך',     1, 'Admin,Staff,Mentor', 1, 'Optional', 1),
                ('SpecialEvent',              'אירוע מיוחד',         'בקשה הקשורה לאירוע, מילואים או מחלה',         1, 'Admin,Staff',        0, 'Optional', 1),
                ('TechnicalSupport',          'פנייה טכנולוגית',    'תקלות טכניות במערכת או בכלי הפרויקט',         1, 'Admin,Staff',        0, 'Optional', 1),
                ('Meeting',                   'פגישה / בקשה כללית', 'בקשת פגישה או נושא כללי לצוות האקדמי',         1, 'Admin,Staff',        0, 'Optional', 1),
                ('ClientChallenge',           'אתגר מול לקוח',       'קושי או בעיה מול לקוח הפרויקט',               1, 'Admin,Staff',        0, 'Optional', 1),
                ('ContentChallenge',          'אתגר תוכן',           'בעיית תוכן או נושאים אקדמיים בפרויקט',         1, 'Admin,Staff',        0, 'Optional', 1),
                ('CharacterizationChallenge', 'אתגר אפיון',          'בעיה באפיון הפרויקט או בדרישות',               1, 'Admin,Staff',        0, 'Optional', 1)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EnsureRoadmapStagesAsync
    //
    //  Backs the "ציר התקדמות פרויקט" feature. The roadmap is one-level deep:
    //  each AcademicYear has an ordered list of stages, and each existing
    //  AcademicYearMilestones row (per-cycle milestone instantiation) can be
    //  bound to at most one stage via the new RoadmapStageId column.
    //
    //  Schema:
    //   - RoadmapStages: per-cycle stage definitions (name / dates / order /
    //     active). UNIQUE(AcademicYearId, Code) keeps the seed idempotent and
    //     guards against duplicate codes inside a single cycle.
    //   - AcademicYearMilestones.RoadmapStageId: nullable FK with
    //     ON DELETE SET NULL so that deleting a stage doesn't take milestones
    //     down with it — they just become "unassigned" and fall back to the
    //     existing milestone-only display.
    //
    //  Seed: seven default Hebrew stages drawn from the mentor-guide for
    //  every existing AcademicYear that doesn't already have any stages.
    //  Year-specific dates are intentionally left NULL — admins set them per
    //  cycle from the management UI. INSERT OR IGNORE on the UNIQUE constraint
    //  means re-running the migrator is a no-op for already-seeded cycles.
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task EnsureRoadmapStagesAsync(SqliteConnection connection)
    {
        // 1. RoadmapStages table.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS RoadmapStages (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                AcademicYearId      INTEGER NOT NULL,
                Code                TEXT    NOT NULL,
                Name                TEXT    NOT NULL,
                Description         TEXT    NOT NULL DEFAULT '',
                SuggestedStartDate  TEXT,
                SuggestedEndDate    TEXT,
                DisplayOrder        INTEGER NOT NULL DEFAULT 0,
                IsActive            INTEGER NOT NULL DEFAULT 1,
                CreatedAt           TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt           TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (AcademicYearId) REFERENCES AcademicYears(Id) ON DELETE CASCADE,
                UNIQUE (AcademicYearId, Code)
            )");

        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_roadmap_stages_year ON RoadmapStages(AcademicYearId, DisplayOrder)");

        // 2. Nullable RoadmapStageId column on AcademicYearMilestones.
        //    Guarded by PRAGMA table_info so re-running on an already-migrated
        //    DB doesn't error. ON DELETE SET NULL preserves the milestone row
        //    when a stage is deleted (milestone reverts to "unassigned").
        var aymColumns = await GetColumnsAsync(connection, "AcademicYearMilestones");
        if (!aymColumns.Contains("RoadmapStageId"))
        {
            // SQLite ALTER TABLE ADD COLUMN doesn't support inline FK clauses
            // in older versions, but adding a plain INTEGER column is safe.
            // The FK is enforced via the explicit RoadmapStages.OnDelete logic
            // executed by trg_roadmap_stage_delete below.
            await connection.ExecuteNonQueryAsync(
                "ALTER TABLE AcademicYearMilestones ADD COLUMN RoadmapStageId INTEGER");
        }

        // SET NULL on stage delete — mirrors a real FK ON DELETE SET NULL.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TRIGGER IF NOT EXISTS trg_roadmap_stage_delete_nullify
            AFTER DELETE ON RoadmapStages
            BEGIN
                UPDATE AcademicYearMilestones
                SET    RoadmapStageId = NULL
                WHERE  RoadmapStageId = OLD.Id;
            END");

        // 3. Seed seven default stages per existing cycle, idempotent via
        //    UNIQUE(AcademicYearId, Code). Year-specific dates stay NULL.
        var yearIds = (await connection.QueryYearIdsAsync()).ToList();
        foreach (var yearId in yearIds)
        {
            await SeedDefaultStagesForCycleAsync(connection, yearId);
        }
    }

    private static async Task SeedDefaultStagesForCycleAsync(SqliteConnection connection, int academicYearId)
    {
        var defaults = new (string Code, string Name, string Description, int Order)[]
        {
            ("Selection",         "בחירה ושיבוצים",                        "תהליך בחירת פרויקטים ושיבוץ סטודנטים לצוותים",        1),
            ("Kickoff",           "התנעה",                                  "מפגשי פתיחה, היכרות עם הלקוח והגדרת אופן העבודה",     2),
            ("Definition",        "הגדרת הבעיה וכיוון ראשוני לפתרון",      "ניסוח הצורך, מטרות ויעדים ראשוניים",                  3),
            ("Specification",     "אפיון",                                  "סקירות, מחקר משתמשים ואפיון הפתרון",                  4),
            ("Development",       "פיתוח",                                  "מוקאפים, אבי-טיפוס וגרסאות עובדות של המוצר",          5),
            ("Evaluation",        "הערכה",                                  "תכנון, ביצוע וסיכום הערכת הפתרון מול משתמשים",       6),
            ("SubmissionGrading", "הגשות וציונים",                          "הגשות סופיות, הגנה ומתן ציונים",                      7),
        };

        const string insertSql = @"
            INSERT OR IGNORE INTO RoadmapStages
                (AcademicYearId, Code, Name, Description, DisplayOrder, IsActive)
            VALUES
                (@AcademicYearId, @Code, @Name, @Description, @DisplayOrder, 1)";

        foreach (var s in defaults)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = insertSql;
            cmd.Parameters.AddWithValue("@AcademicYearId", academicYearId);
            cmd.Parameters.AddWithValue("@Code",           s.Code);
            cmd.Parameters.AddWithValue("@Name",           s.Name);
            cmd.Parameters.AddWithValue("@Description",    s.Description);
            cmd.Parameters.AddWithValue("@DisplayOrder",   s.Order);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EnsureAirtableSeedFromAppsettingsAsync
    //
    //  One-time bootstrap that copies the legacy appsettings.json "Airtable"
    //  section into AirtableIntegrationSettings + AirtableFieldMappings for
    //  the current academic year. After this block runs, the runtime no
    //  longer reads appsettings — AirtableService.ResolveActiveOptionsAsync
    //  uses the DB exclusively. The block is idempotent and self-skipping:
    //
    //    • no current cycle → skip (admin must mark one cycle current first)
    //    • a row already exists for that cycle → skip (already migrated)
    //    • appsettings has empty Token OR empty BaseId → skip
    //      (no useful data to copy)
    //
    //  On subsequent boots all three guards short-circuit, so the block is
    //  a no-op and safe to leave wired in indefinitely.
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task EnsureAirtableSeedFromAppsettingsAsync(
        SqliteConnection connection, IConfiguration config)
    {
        // Read the legacy "Airtable" section directly so we don't depend on
        // the AirtableOptions class at migrator level (cleaner DI boundary).
        var section = config.GetSection("Airtable");
        if (!section.Exists()) return;

        string token  = section.GetValue<string>("Token")     ?? "";
        string baseId = section.GetValue<string>("BaseId")    ?? "";
        string table  = section.GetValue<string>("TableName") ?? "";
        string view   = section.GetValue<string>("ViewName")  ?? "";
        bool   svo    = section.GetValue<bool?>("StudentVisibleOnly") ?? true;

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(baseId))
            return;

        // Find the current cycle. If no cycle is marked current, the admin
        // must set one before the appsettings values can land in the DB.
        int currentCycleId;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id FROM AcademicYears WHERE IsCurrent = 1 LIMIT 1";
            var raw = await cmd.ExecuteScalarAsync();
            if (raw is null || raw is DBNull) return;
            currentCycleId = Convert.ToInt32(raw);
        }

        // Skip if a row already exists for this cycle (re-run safety).
        await using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "SELECT 1 FROM AirtableIntegrationSettings WHERE AcademicYearId = @Y LIMIT 1";
            checkCmd.Parameters.AddWithValue("@Y", currentCycleId);
            var existing = await checkCmd.ExecuteScalarAsync();
            if (existing is not null && existing is not DBNull) return;
        }

        // Insert the integration row + grab its Id. Marked IsActive=1 so the
        // import flow continues to find the same config as before the
        // fallback was removed.
        long newId;
        await using (var insertCmd = connection.CreateCommand())
        {
            insertCmd.CommandText = @"
                INSERT INTO AirtableIntegrationSettings
                    (AcademicYearId, Name, ApiToken, BaseId,
                     ProjectsTable, ProjectsView, StudentVisibleOnly,
                     IsActive, CreatedAt, UpdatedAt)
                VALUES
                    (@Y, @N, @T, @B, @PT, @PV, @SVO, 1,
                     datetime('now'), datetime('now'));
                SELECT last_insert_rowid();";
            insertCmd.Parameters.AddWithValue("@Y",   currentCycleId);
            insertCmd.Parameters.AddWithValue("@N",   "Imported from appsettings.json");
            insertCmd.Parameters.AddWithValue("@T",   token);
            insertCmd.Parameters.AddWithValue("@B",   baseId);
            insertCmd.Parameters.AddWithValue("@PT",  table);
            insertCmd.Parameters.AddWithValue("@PV",  view);
            insertCmd.Parameters.AddWithValue("@SVO", svo ? 1 : 0);
            newId = Convert.ToInt64(await insertCmd.ExecuteScalarAsync() ?? 0L);
        }
        if (newId == 0) return;

        // Copy each FieldMap key into AirtableFieldMappings(EntityType='Project').
        // Only non-empty values; we don't want to materialise default
        // English placeholders as if they were configured Hebrew labels.
        var fieldMapSection = section.GetSection("FieldMap");
        if (!fieldMapSection.Exists()) return;

        foreach (var kv in fieldMapSection.AsEnumerable(makePathsRelative: true))
        {
            if (string.IsNullOrEmpty(kv.Key)) continue;
            string airtableName = kv.Value ?? "";
            if (string.IsNullOrWhiteSpace(airtableName)) continue;

            await using var mapCmd = connection.CreateCommand();
            mapCmd.CommandText = @"
                INSERT OR IGNORE INTO AirtableFieldMappings
                    (IntegrationSettingsId, EntityType, LocalFieldName,
                     AirtableFieldName, IsRequired, CreatedAt, UpdatedAt)
                VALUES
                    (@S, 'Project', @L, @A, 0,
                     datetime('now'), datetime('now'))";
            mapCmd.Parameters.AddWithValue("@S", newId);
            mapCmd.Parameters.AddWithValue("@L", kv.Key);
            mapCmd.Parameters.AddWithValue("@A", airtableName);
            await mapCmd.ExecuteNonQueryAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EnsureImportIntegrityGuardsAsync
    //
    //  Three things, all idempotent:
    //
    //  1. Trim leading/trailing whitespace on existing users.IdNumber so the
    //     UNIQUE index in step 2 sees the same normalized form that future
    //     write paths use. Cheap one-time write — live DB audit on 2026-06-04
    //     showed zero rows with whitespace, so this is effectively a no-op
    //     today but guards against quietly-imported padding later.
    //
    //  2. Partial UNIQUE indexes (SQLite 3.8+):
    //       users.IdNumber WHERE non-empty — students legitimately register
    //           without an IdNumber (admins do too), so NULL/empty must
    //           still be allowed multiple times. Only "I have a real ID"
    //           rows must be unique.
    //       Projects.AirtableRecordId WHERE non-null & non-empty — manually
    //           created projects don't carry an Airtable record id; only
    //           imported ones do, and only those must be unique.
    //
    //     The partial WHERE clauses are essential: a plain UNIQUE on a
    //     NULL-permitting column would still allow multiple NULLs in SQLite,
    //     but several empty strings would still collide. The WHERE guards
    //     express the intent exactly.
    //
    //  3. AirtableImportRuns — lightweight per-import audit log so failed
    //     imports can be investigated days later, not just within the
    //     lifetime of the in-memory result DTO.
    //
    //  This entire block runs after all other migrations. Re-runs are no-ops:
    //  CREATE UNIQUE INDEX IF NOT EXISTS + CREATE TABLE IF NOT EXISTS.
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task EnsureImportIntegrityGuardsAsync(SqliteConnection connection)
    {
        // 1. Normalize existing IdNumbers (cheap one-time UPDATE).
        await connection.ExecuteNonQueryAsync(@"
            UPDATE users
            SET    IdNumber = TRIM(IdNumber)
            WHERE  IdNumber IS NOT NULL
              AND  IdNumber <> TRIM(IdNumber)");

        // 2a. Partial UNIQUE on users.IdNumber.
        //     Live-DB audit (2026-06-04) confirmed zero existing duplicates,
        //     so the index will create cleanly. If a future deploy hits a
        //     dup, the CREATE will fail loudly with a constraint violation
        //     — surfaces the data problem before silent corruption.
        await connection.ExecuteNonQueryAsync(@"
            CREATE UNIQUE INDEX IF NOT EXISTS ux_users_idnumber_partial
            ON users(IdNumber)
            WHERE IdNumber IS NOT NULL AND IdNumber <> ''");

        // 2b. Partial UNIQUE on Projects.AirtableRecordId.
        //     Closes the race window in AirtableService.UpsertRecordAsync —
        //     two concurrent imports of the same Airtable record can no
        //     longer create two project rows. The service now uses INSERT
        //     OR IGNORE on top of this; see AirtableService.cs for the
        //     paired change.
        await connection.ExecuteNonQueryAsync(@"
            CREATE UNIQUE INDEX IF NOT EXISTS ux_projects_airtable_record_partial
            ON Projects(AirtableRecordId)
            WHERE AirtableRecordId IS NOT NULL AND AirtableRecordId <> ''");

        // 3. AirtableImportRuns audit table.
        //    One row per actual import (not per preview). Captures who, when,
        //    counts, and a short error summary. ErrorDetails is capped at
        //    8 KB so a runaway error list can't balloon the table. No FK
        //    cascade on TriggeredByUserId so an audit row survives a user
        //    deletion; we just store the id and let the join LEFT-resolve.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS AirtableImportRuns (
                Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                IntegrationSettingsId INTEGER NOT NULL,
                TriggeredByUserId     INTEGER,
                StartedAt             TEXT    NOT NULL DEFAULT (datetime('now')),
                FinishedAt            TEXT,
                TotalFetched          INTEGER NOT NULL DEFAULT 0,
                Inserted              INTEGER NOT NULL DEFAULT 0,
                Updated               INTEGER NOT NULL DEFAULT 0,
                Failed                INTEGER NOT NULL DEFAULT 0,
                Skipped               INTEGER NOT NULL DEFAULT 0,
                Status                TEXT    NOT NULL DEFAULT 'Success',
                ErrorSummary          TEXT    NOT NULL DEFAULT '',
                ErrorDetails          TEXT    NOT NULL DEFAULT '',
                FOREIGN KEY (IntegrationSettingsId)
                    REFERENCES AirtableIntegrationSettings(Id) ON DELETE CASCADE
            )");

        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_airtable_import_runs_settings " +
            "ON AirtableImportRuns(IntegrationSettingsId, StartedAt DESC)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EnsureSelectionMilestoneBackfillAsync
    //
    //  Business rule: a team that already holds a published project
    //  assignment is, by definition, past the "selection & assignment"
    //  phase — that milestone should be Completed. Pre-existing data
    //  from before the publish hook didn't enforce this, producing
    //  cases where the milestone screen showed "טרם נפתח" for teams
    //  that clearly had a project.
    //
    //  One-time UPDATE: for every project where TeamId IS NOT NULL AND
    //  AssignmentIsDraft = 0, mark its "בחירת פרויקטים ושיבוצים"
    //  milestone Completed (with a Hebrew Notes value mirroring the
    //  existing convention for seed-completed rows). Idempotent — the
    //  WHERE clause excludes already-Completed rows, so re-running on
    //  every boot is a no-op once the backfill is applied.
    //
    //  Edge cases:
    //   • Drafts (AssignmentIsDraft=1) are skipped — the publish step
    //     is the moment the assignment becomes "real".
    //   • Projects with TeamId=NULL are skipped — no team, no completion.
    //   • The "selection" milestone is identified by title match. If a
    //     future cycle renames or replaces it, the backfill no-ops for
    //     that cycle, which is the safe default.
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task EnsureSelectionMilestoneBackfillAsync(SqliteConnection connection)
    {
        await connection.ExecuteNonQueryAsync(@"
            UPDATE ProjectMilestones
            SET    Status      = 'Completed',
                   CompletedAt = COALESCE(CompletedAt, datetime('now')),
                   Notes       = CASE WHEN COALESCE(Notes, '') = ''
                                      THEN 'הפרויקט שובץ בהצלחה'
                                      ELSE Notes END
            WHERE  Status <> 'Completed'
              AND  AcademicYearMilestoneId IN (
                    SELECT aym.Id
                    FROM   AcademicYearMilestones aym
                    JOIN   MilestoneTemplates mt ON mt.Id = aym.MilestoneTemplateId
                    WHERE  mt.Title = 'בחירת פרויקטים ושיבוצים'
              )
              AND  ProjectId IN (
                    SELECT p.Id FROM Projects p
                    WHERE  p.TeamId IS NOT NULL
                      AND  COALESCE(p.AssignmentIsDraft, 0) = 0
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

    // Returns every AcademicYears.Id (used by the roadmap-stage seed to
    // populate default stages for every existing cycle).
    private static async Task<List<int>> QueryYearIdsAsync(this SqliteConnection connection)
    {
        var ids = new List<int>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id FROM AcademicYears ORDER BY Id";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetInt32(0));
        return ids;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EnsureBaseSchemaAsync
    //
    //  Creates the seven core tables required by everything else in the
    //  migrator. Uses CREATE TABLE IF NOT EXISTS, so re-running is a no-op.
    //
    //  Each CREATE statement declares the FINAL canonical column set for the
    //  table (i.e. base columns + every column that older code added later
    //  via ALTER). The per-column ALTER guards further down then become
    //  no-ops on a freshly-bootstrapped DB while still bringing forward
    //  legacy DBs that pre-date a particular column.
    //
    //  Order is dependency-friendly even though SQLite does not enforce FKs
    //  by default — keeps it readable.
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task EnsureBaseSchemaAsync(SqliteConnection connection)
    {
        // AcademicYears — referenced by Teams, Projects, etc.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS AcademicYears (
                Id        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Name      TEXT    NOT NULL,
                StartDate DATE    NOT NULL,
                EndDate   DATE    NOT NULL,
                IsActive  BOOLEAN NOT NULL DEFAULT 0,
                IsCurrent BOOLEAN NOT NULL DEFAULT 0,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                Status    TEXT,
                CONSTRAINT unique_AY_Name UNIQUE (Name)
            )");

        // ProjectTypes — referenced by Projects.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ProjectTypes (
                Id   INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Name TEXT    NOT NULL UNIQUE
            )");

        // users — auth root. Final column set including columns historically
        // added via ALTER (Phone, IsActive, AcademicYear, IdNumber, ProfileImagePath).
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS users (
                Id               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Email            TEXT    NOT NULL,
                PasswordHash     TEXT    NOT NULL,
                FirstName        TEXT    NOT NULL,
                LastName         TEXT    NOT NULL,
                IsVerified       BOOLEAN NOT NULL DEFAULT 0,
                RegisterDate     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                Phone            TEXT    NOT NULL DEFAULT '',
                IsActive         INTEGER NOT NULL DEFAULT 1,
                UpdatedAt        DATETIME,
                CreatedAt        DATETIME,
                AcademicYearId   INTEGER,
                AcademicYear     TEXT    NOT NULL DEFAULT '2025-2026',
                IdNumber         TEXT    NOT NULL DEFAULT '',
                ProfileImagePath TEXT,
                CONSTRAINT unique_users_Email UNIQUE (Email)
            )");

        // UserRoles — many-to-many between users and role names.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS UserRoles (
                Id     INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Role   TEXT    NOT NULL,
                CONSTRAINT lnk_users_UserRoles
                    FOREIGN KEY (UserId) REFERENCES users(Id)
                    ON DELETE CASCADE ON UPDATE CASCADE
            )");

        // Teams — final column set including TeamName.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS Teams (
                Id              INTEGER  NOT NULL PRIMARY KEY AUTOINCREMENT,
                AcademicYearId  INTEGER  NOT NULL,
                IsExceptional   BOOLEAN  NOT NULL DEFAULT 0,
                CreatedAt       DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                TeamName        TEXT     NOT NULL DEFAULT '',
                CONSTRAINT fk_Teams_AcademicYear
                    FOREIGN KEY (AcademicYearId) REFERENCES AcademicYears(Id)
            )");

        // TeamMembers — links users to teams.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS TeamMembers (
                Id         INTEGER  NOT NULL PRIMARY KEY AUTOINCREMENT,
                TeamId     INTEGER  NOT NULL,
                UserId     INTEGER  NOT NULL,
                JoinedAt   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                IsActive   BOOLEAN  NOT NULL DEFAULT 1,
                MemberRole TEXT,
                CONSTRAINT fk_TeamMembers_Team FOREIGN KEY (TeamId) REFERENCES Teams(Id),
                CONSTRAINT fk_TeamMembers_User FOREIGN KEY (UserId) REFERENCES users(Id),
                CONSTRAINT unique_Team_User UNIQUE (TeamId, UserId)
            )");

        // Projects — final column set including all metadata columns added
        // historically via ALTER. Column-existence guards further down stay
        // valid because they only ALTER when the column is missing.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS Projects (
                Id                INTEGER  NOT NULL PRIMARY KEY AUTOINCREMENT,
                ProjectNumber     INTEGER  NOT NULL UNIQUE,
                AcademicYearId    INTEGER  NOT NULL,
                TeamId            INTEGER  UNIQUE,
                Title             TEXT     NOT NULL,
                Description       TEXT,
                ProjectTypeId     INTEGER  NOT NULL,
                Status            TEXT     NOT NULL,
                HealthStatus      TEXT,
                CreatedAt         DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt         DATETIME,
                SourceType        TEXT     NOT NULL DEFAULT 'Manual',
                AirtableRecordId  TEXT,
                OrganizationName  TEXT,
                ContactPerson     TEXT,
                ContactRole       TEXT,
                Goals             TEXT,
                TargetAudience    TEXT,
                InternalNotes     TEXT,
                Priority          TEXT,
                LastSyncedAt      TEXT,
                OrganizationType  TEXT,
                ProjectTopic      TEXT,
                Contents          TEXT,
                ContactEmail      TEXT,
                ContactPhone      TEXT,
                AssignmentIsDraft INTEGER  NOT NULL DEFAULT 0,
                CONSTRAINT fk_Projects_AcademicYear
                    FOREIGN KEY (AcademicYearId) REFERENCES AcademicYears(Id),
                CONSTRAINT fk_Projects_Team
                    FOREIGN KEY (TeamId) REFERENCES Teams(Id),
                CONSTRAINT fk_Projects_Type
                    FOREIGN KEY (ProjectTypeId) REFERENCES ProjectTypes(Id)
            )");

        // ── Milestone backbone ──────────────────────────────────────────────
        // These three tables were originally shipped in the local DB dump
        // but never declared in the migrator. On a fresh empty DB (Render)
        // they must be created here so subsequent INSERT/SELECT statements
        // (and the per-project initialisation flow) succeed.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS MilestoneTemplates (
                Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Title         TEXT    NOT NULL,
                Description   TEXT,
                OrderIndex    INTEGER NOT NULL,
                IsRequired    BOOLEAN NOT NULL DEFAULT 1,
                ProjectTypeId INTEGER,
                IsActive      BOOLEAN NOT NULL DEFAULT 1,
                OpenDate      DATE,
                DueDate       DATE,
                CloseDate     DATE,
                CONSTRAINT fk_MilestoneTemplates_ProjectType
                    FOREIGN KEY (ProjectTypeId) REFERENCES ProjectTypes(Id)
            )");

        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS AcademicYearMilestones (
                Id                  INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                AcademicYearId      INTEGER NOT NULL,
                MilestoneTemplateId INTEGER NOT NULL,
                OpenDate            DATE,
                DueDate             DATE    NOT NULL,
                CloseDate           DATE,
                IsActive            BOOLEAN NOT NULL DEFAULT 1,
                CONSTRAINT fk_AYM_Year     FOREIGN KEY (AcademicYearId)      REFERENCES AcademicYears(Id),
                CONSTRAINT fk_AYM_Template FOREIGN KEY (MilestoneTemplateId) REFERENCES MilestoneTemplates(Id),
                CONSTRAINT unique_AYM_Year_Template UNIQUE (AcademicYearId, MilestoneTemplateId)
            )");

        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ProjectMilestones (
                Id                      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ProjectId               INTEGER NOT NULL,
                AcademicYearMilestoneId INTEGER NOT NULL,
                Status                  TEXT    NOT NULL,
                CompletedAt             DATETIME,
                Notes                   TEXT,
                CONSTRAINT fk_PM_Project        FOREIGN KEY (ProjectId)               REFERENCES Projects(Id),
                CONSTRAINT fk_PM_YearMilestone  FOREIGN KEY (AcademicYearMilestoneId) REFERENCES AcademicYearMilestones(Id),
                CONSTRAINT unique_PM_Project_AYM UNIQUE (ProjectId, AcademicYearMilestoneId)
            )");

        // Tasks — operational task rows (system / personal / mentor) plus
        // submission policy snapshot fields.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS Tasks (
                Id                     INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ProjectId              INTEGER NOT NULL,
                ProjectMilestoneId     INTEGER,
                Title                  TEXT    NOT NULL,
                Description            TEXT,
                TaskType               TEXT    NOT NULL,
                Status                 TEXT    NOT NULL,
                DueDate                DATETIME,
                CreatedByUserId        INTEGER NOT NULL,
                AssignedToUserId       INTEGER,
                IsMandatory            BOOLEAN NOT NULL DEFAULT 0,
                IsSystemTask           BOOLEAN NOT NULL DEFAULT 0,
                RequiresClosure        BOOLEAN NOT NULL DEFAULT 0,
                ClosedAt               DATETIME,
                CreatedAt              DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                IsSubmission           BOOLEAN,
                SubmissionLink         TEXT,
                SubmittedAt            DATETIME DEFAULT CURRENT_TIMESTAMP,
                SubmittedByUserId      INTEGER,
                MaxFilesCount          INTEGER,
                MaxFileSizeMb          INTEGER,
                AllowedFileTypes       TEXT,
                SubmissionInstructions TEXT,
                CONSTRAINT fk_Tasks_Project          FOREIGN KEY (ProjectId)          REFERENCES Projects(Id),
                CONSTRAINT fk_Tasks_ProjectMilestone FOREIGN KEY (ProjectMilestoneId) REFERENCES ProjectMilestones(Id),
                CONSTRAINT fk_Tasks_CreatedBy        FOREIGN KEY (CreatedByUserId)    REFERENCES users(Id),
                CONSTRAINT fk_Tasks_AssignedTo       FOREIGN KEY (AssignedToUserId)   REFERENCES users(Id),
                CONSTRAINT fk_Tasks_SubmittedBy      FOREIGN KEY (SubmittedByUserId)  REFERENCES users(Id)
            )");

        // BlackList — JWT revocation list used by TokenBlacklistMiddleware.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS BlackList (
                ID            INTEGER  NOT NULL PRIMARY KEY AUTOINCREMENT,
                Token         TEXT     NOT NULL,
                BlacklistedAt DATETIME NOT NULL,
                ExpiresAt     DATETIME NOT NULL
            )");

        // Legacy Requests / RequestTypes — present in the original DB dump
        // but superseded by the ProjectRequests* tables. Created here so
        // anything that still references them at startup keeps working.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS RequestTypes (
                Id   INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Name TEXT    NOT NULL UNIQUE
            )");
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS Requests (
                Id               INTEGER  NOT NULL PRIMARY KEY AUTOINCREMENT,
                ProjectId        INTEGER  NOT NULL,
                CreatedByUserId  INTEGER  NOT NULL,
                RequestTypeId    INTEGER  NOT NULL,
                Title            TEXT     NOT NULL,
                Description      TEXT,
                Status           TEXT     NOT NULL,
                OpenedAt         DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                ResolvedAt       DATETIME,
                AssignedToUserId INTEGER,
                CONSTRAINT fk_Requests_Project    FOREIGN KEY (ProjectId)        REFERENCES Projects(Id),
                CONSTRAINT fk_Requests_CreatedBy  FOREIGN KEY (CreatedByUserId)  REFERENCES users(Id),
                CONSTRAINT fk_Requests_Type       FOREIGN KEY (RequestTypeId)    REFERENCES RequestTypes(Id),
                CONSTRAINT fk_Requests_AssignedTo FOREIGN KEY (AssignedToUserId) REFERENCES users(Id)
            )");

        // ── UserNotificationPreferences — per-user toggle matrix ──────────
        // One row per (UserId, NotificationType). Missing rows are treated as
        // sensible defaults by the read endpoint (in-app on for everything,
        // email on for high-priority types only — see controller for the
        // canonical default list).
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS UserNotificationPreferences (
                Id               INTEGER  NOT NULL PRIMARY KEY AUTOINCREMENT,
                UserId           INTEGER  NOT NULL,
                NotificationType TEXT     NOT NULL,
                InAppEnabled     INTEGER  NOT NULL DEFAULT 1,
                EmailEnabled     INTEGER  NOT NULL DEFAULT 0,
                CreatedAt        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE (UserId, NotificationType),
                CONSTRAINT fk_UNP_User FOREIGN KEY (UserId) REFERENCES users(Id)
                    ON DELETE CASCADE ON UPDATE CASCADE
            )");

        // ── MentorPreferences ───────────────────────────────────────────────
        // One row per mentor, keyed by UserId. Every field is nullable / has a
        // safe default so a mentor who hasn't filled the form yet still has a
        // valid row when the GET endpoint upserts on first access. Multi-select
        // fields use comma-separated TEXT values to match patterns elsewhere
        // in the codebase (Tasks.AllowedFileTypes, etc.).
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS MentorPreferences (
                UserId                            INTEGER  NOT NULL PRIMARY KEY,

                -- 1. Mentorship style
                RoleDescription                   TEXT,
                InvolvementLevel                  TEXT,           -- low|medium|high

                -- 2. Communication preferences
                PreferredChannel                  TEXT,           -- email|whatsapp|slack|other
                PreferredChannelOther             TEXT,
                ExpectedResponseTime              TEXT,           -- 24h|1to3d|week
                CommunicationFrequency            TEXT,           -- weekly|biweekly|asneeded

                -- 3. Student updates
                RequirePeriodicUpdates            INTEGER  NOT NULL DEFAULT 0,
                UpdateFrequency                   TEXT,           -- weekly|biweekly
                UpdateContent                     TEXT,           -- comma-separated: progress,plans,blockers

                -- 4. Client interaction
                ClientInteractionFrequency        TEXT,           -- monthly|biweekly|custom
                ClientInteractionFrequencyCustom  TEXT,
                MentorInClientInteraction         TEXT,           -- yes|no|key_milestones

                -- 5. Submission & feedback
                SubmissionLeadTime                TEXT,           -- 2d|3to5d|custom
                SubmissionLeadTimeCustom          TEXT,
                ReviewIterations                  INTEGER,        -- 1, 2, 3 (3+)
                FeedbackType                      TEXT,           -- written|verbal|both

                -- 6. Decision involvement
                DecisionInvolvement               TEXT,           -- comma-separated: major,milestones,ongoing

                -- 7. Quality expectations
                QualityDescription                TEXT,
                QualityFocusAreas                 TEXT,           -- comma-separated: ux,technical,pedagogical,research

                -- 8. Red lines
                RedLines                          TEXT,

                -- 9. Availability
                AvailableTimes                    TEXT,
                MeetingFormat                     TEXT,           -- online|in_person|both
                SchedulingMethod                  TEXT,           -- student|fixed

                -- 10. Notification preferences (mentor-side digest controls)
                PreferenceEvents                  TEXT,           -- comma-separated: new_submission,delays,no_response
                PreferenceFrequency               TEXT,           -- immediate|daily|weekly

                CreatedAt                         DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt                         DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT fk_MP_User FOREIGN KEY (UserId) REFERENCES users(Id)
                    ON DELETE CASCADE ON UPDATE CASCADE
            )");

        // ── PersonalTasks — student-private reminder/checklist items ────────
        // One row per task, scoped to a single user. Not linked to project
        // milestones or the shared Tasks table. Created / toggled by the
        // student via the dashboard Personal Tasks card.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS PersonalTasks (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId      INTEGER NOT NULL,
                Title       TEXT    NOT NULL,
                Description TEXT,
                DueDate     TEXT,
                IsDone      INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (UserId) REFERENCES users(Id) ON DELETE CASCADE
            )");

        // ── TeamTasks — student-created work items, team-visible ────────────
        // Completely separate from the official Tasks table.
        // No milestone, no mentor review, no Moodle workflow, no progress impact.
        // All active members of the team (ProjectId + TeamId) can create, edit,
        // complete and delete any row.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS TeamTasks (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId        INTEGER NOT NULL,
                TeamId           INTEGER NOT NULL,
                CreatedByUserId  INTEGER NOT NULL,
                Title            TEXT    NOT NULL,
                Description      TEXT,
                AssignedToUserId INTEGER,
                DueDate          TEXT,
                IsDone           INTEGER NOT NULL DEFAULT 0,
                CreatedAt        TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedAt        TEXT,
                FOREIGN KEY (ProjectId)        REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (TeamId)           REFERENCES Teams(Id)    ON DELETE CASCADE,
                FOREIGN KEY (CreatedByUserId)  REFERENCES users(Id),
                FOREIGN KEY (AssignedToUserId) REFERENCES users(Id)
            )");

        // ── ProjectTeamProfile — the team's own project identity ────────────
        // The student-editable display name and description of a project, kept
        // OUT of the Projects row on purpose.
        //
        // Projects.Title / Projects.Description are catalog fields: 108 of the
        // 117 seeded projects are SourceType='Airtable', and AirtableService's
        // sync overwrites both columns on every run (AirtableService.cs:957).
        // Writing a student's edit there would be silently reverted by the next
        // sync, and would also change what lecturers, mentors and the catalog
        // see. This table is Motiva-owned, the sync never touches it, and the
        // student-facing name resolves as
        //     COALESCE(ptp.DisplayTitle, p.Title)
        // so a project with no row here behaves exactly as it did before.
        //
        // One row per project (PK on ProjectId), because the identity belongs
        // to the project, not to whichever team member last edited it.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ProjectTeamProfile (
                ProjectId       INTEGER PRIMARY KEY,
                DisplayTitle    TEXT,
                Description     TEXT,
                UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedByUserId INTEGER,
                FOREIGN KEY (ProjectId)       REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (UpdatedByUserId) REFERENCES users(Id)
            )");

        // ── ProjectResources — the team's own links ─────────────────────────
        // "משאבי הפרויקט": the Google Doc, the Drive folder, the Figma file,
        // the repo. Team-owned and team-writable, exactly like TeamTasks, and
        // deliberately NOT part of ResourceFiles — that table is the course's
        // knowledge base, published by staff to every project.
        //
        // Only a label and a URL are stored. The resource's KIND (Figma /
        // GitHub / Drive …) is derived from the URL at render time rather than
        // persisted, so a stored kind can never disagree with the link it
        // describes.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ProjectResources (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId       INTEGER NOT NULL,
                TeamId          INTEGER NOT NULL,
                Label           TEXT    NOT NULL,
                Url             TEXT    NOT NULL,
                CreatedByUserId INTEGER NOT NULL,
                CreatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (ProjectId)       REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (TeamId)          REFERENCES Teams(Id)    ON DELETE CASCADE,
                FOREIGN KEY (CreatedByUserId) REFERENCES users(Id)
            )");

        await connection.ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS ix_projectresources_project ON ProjectResources(ProjectId)");

        // ── ProjectSubmissionStatuses — team progress on תוצרי ההגשה ────────
        // Motiva's progress layer on top of the course's submission guidance.
        //
        // Only the STATUS is persisted. The guidance itself (what a "חוברת" or
        // a "פוסטר" requires) is course content with no authoring UI behind it,
        // so it lives in the client-side catalog
        // (Client/Pages/ProjectWorkspace/SubmissionDeliverablesCatalog.cs) and
        // is keyed from here by DeliverableKey. A key that disappears from the
        // catalog leaves an orphan row that nothing reads — harmless — and a
        // new key simply has no row until the team sets one.
        //
        // Deliberately NOT modelled on Tasks/TaskSubmissions: those are the
        // milestone submission pipeline (submit → mentor approve → Moodle), and
        // overloading them would corrupt /tasks, /submissions and the mentor
        // review queue.
        await connection.ExecuteNonQueryAsync(@"
            CREATE TABLE IF NOT EXISTS ProjectSubmissionStatuses (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId       INTEGER NOT NULL,
                DeliverableKey  TEXT    NOT NULL,
                Status          TEXT    NOT NULL,
                UpdatedAt       TEXT    NOT NULL DEFAULT (datetime('now')),
                UpdatedByUserId INTEGER,
                UNIQUE (ProjectId, DeliverableKey),
                FOREIGN KEY (ProjectId)       REFERENCES Projects(Id) ON DELETE CASCADE,
                FOREIGN KEY (UpdatedByUserId) REFERENCES users(Id)
            )");

        // ── Canonical reference-data seeds ──────────────────────────────────
        // The two ProjectTypes rows are referenced by the MilestoneTemplates
        // seed below in the migrator (ProjectTypeId = 1 / 2). On a fresh DB
        // those targets do not exist and the FK check trips. INSERT OR IGNORE
        // keeps this idempotent.
        await connection.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO ProjectTypes (Id, Name) VALUES (1, 'Technological')");
        await connection.ExecuteNonQueryAsync(
            "INSERT OR IGNORE INTO ProjectTypes (Id, Name) VALUES (2, 'Methodological')");
    }

    private static async Task ExecuteNonQueryAsync(this SqliteConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
