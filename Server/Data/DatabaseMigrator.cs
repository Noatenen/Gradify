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
