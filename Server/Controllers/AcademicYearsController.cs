using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  AcademicYearsController — /api/academic-years
//
//  Manages operations that span an entire academic year, such as
//  applying global templates (milestones + tasks) to all active projects.
//
//  The "apply-templates" endpoint is idempotent:
//    • ProjectMilestones  →  INSERT OR IGNORE  (UNIQUE constraint guards)
//    • Tasks              →  NOT EXISTS check on (ProjectId, ProjectMilestoneId, Title)
//
//  Milestone applicability:
//    MilestoneTemplates.ProjectTypeId NULL  = both types
//    1 = Technological only
//    2 = Methodological only
//
//  Only active/assigned projects are processed (Status NOT IN 'Available', 'Unavailable').
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/academic-years")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public class AcademicYearsController : ControllerBase
{
    private readonly DbRepository _db;

    public AcademicYearsController(DbRepository db) => _db = db;

    // ── POST /api/academic-years/{yearId}/apply-templates ───────────────────
    //
    // Converts global templates into operational data for every active project
    // that belongs to the given academic year.
    //
    // Steps:
    //   1. Verify the academic year exists.
    //   2. Load active/assigned projects for the year.
    //   3. Load AcademicYearMilestones scheduled for the year (with type filter info).
    //   4. Load active TaskTemplates.
    //   5. For each project:
    //      a. For each applicable AcademicYearMilestone → INSERT OR IGNORE ProjectMilestone.
    //      b. For each applicable TaskTemplate          → INSERT Task if not already present.
    //
    // Returns an ApplyTemplatesResultDto summary.
    [HttpPost("{yearId:int}/apply-templates")]
    public async Task<IActionResult> ApplyTemplates(int yearId, int authUserId)
    {
        // ── 1. Verify academic year ───────────────────────────────────────────
        var yearExists = (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM AcademicYears WHERE Id = @Id", new { Id = yearId }))
            .FirstOrDefault() > 0;

        if (!yearExists) return NotFound("שנת הלימודים לא נמצאה");

        // ── 2. Active/assigned projects for this year ─────────────────────────
        // Excludes catalog projects (Available / Unavailable) — those have no team yet.
        var projects = (await _db.GetRecordsAsync<ProjectRow>(@"
            SELECT  Id,
                    ProjectTypeId
            FROM    Projects
            WHERE   AcademicYearId = @YearId
              AND   Status NOT IN ('Available', 'Unavailable')",
            new { YearId = yearId }))?.ToList() ?? new();

        if (projects.Count == 0)
            return Ok(new ApplyTemplatesResultDto
            {
                ProjectsProcessed = 0,
                MilestonesCreated = 0,
                MilestonesSkipped = 0,
                TasksCreated      = 0,
                TasksSkipped      = 0,
            });

        // ── 3. AcademicYearMilestones for this year ───────────────────────────
        // Joined with MilestoneTemplates to get the type-applicability filter.
        var ayms = (await _db.GetRecordsAsync<AymRow>(@"
            SELECT  aym.Id,
                    aym.MilestoneTemplateId,
                    aym.DueDate,
                    mt.ProjectTypeId AS MilestoneProjectTypeId
            FROM    AcademicYearMilestones aym
            JOIN    MilestoneTemplates mt ON mt.Id = aym.MilestoneTemplateId
            WHERE   aym.AcademicYearId = @YearId",
            new { YearId = yearId }))?.ToList() ?? new();

        // ── 4. Active task templates ──────────────────────────────────────────
        var taskTemplates = (await _db.GetRecordsAsync<TaskTemplateRow>(@"
            SELECT  Id,
                    Title,
                    Description,
                    MilestoneTemplateId,
                    DueDate,
                    IsSubmission,
                    SubmissionInstructions,
                    MaxFilesCount,
                    MaxFileSizeMb,
                    AllowedFileTypes
            FROM    TaskTemplates
            WHERE   IsActive = 1
            ORDER   BY MilestoneTemplateId, Id"))?.ToList() ?? new();

        // ── 5. Apply per project ──────────────────────────────────────────────
        int milestonesCreated = 0;
        int milestonesSkipped = 0;
        int tasksCreated      = 0;
        int tasksSkipped      = 0;

        foreach (var project in projects)
        {
            // Milestones applicable to this project's type:
            //   NULL  = shared (apply to all)
            //   n     = only the matching project type
            var applicableAyms = ayms
                .Where(a => a.MilestoneProjectTypeId == null
                         || a.MilestoneProjectTypeId == project.ProjectTypeId)
                .ToList();

            // Map MilestoneTemplateId → ProjectMilestoneId for task creation below.
            var milestoneTemplateToProjectMilestone = new Dictionary<int, int>();

            foreach (var aym in applicableAyms)
            {
                // INSERT OR IGNORE — the UNIQUE(ProjectId, AcademicYearMilestoneId) constraint
                // silently skips the insert if the milestone already exists.
                int inserted = await _db.SaveDataAsync(@"
                    INSERT OR IGNORE INTO ProjectMilestones
                        (ProjectId, AcademicYearMilestoneId, Status)
                    VALUES
                        (@ProjectId, @AymId, 'NotStarted')",
                    new { ProjectId = project.Id, AymId = aym.Id });

                if (inserted > 0) milestonesCreated++;
                else              milestonesSkipped++;

                // Retrieve the ID whether newly inserted or pre-existing.
                int pmId = (await _db.GetRecordsAsync<int>(@"
                    SELECT Id FROM ProjectMilestones
                    WHERE  ProjectId = @P AND AcademicYearMilestoneId = @A",
                    new { P = project.Id, A = aym.Id })).FirstOrDefault();

                if (pmId > 0)
                    milestoneTemplateToProjectMilestone[aym.MilestoneTemplateId] = pmId;
            }

            // Create tasks — snapshot from template, no live FK to TaskTemplates.
            foreach (var tt in taskTemplates)
            {
                // Skip if the template's milestone was not applicable to this project.
                if (!milestoneTemplateToProjectMilestone.TryGetValue(
                        tt.MilestoneTemplateId, out int pmId))
                    continue;

                // Deduplication: same title already exists for this project + milestone.
                bool taskExists = (await _db.GetRecordsAsync<int>(@"
                    SELECT COUNT(1) FROM Tasks
                    WHERE  ProjectId         = @P
                      AND  ProjectMilestoneId = @PM
                      AND  Title              = @T",
                    new { P = project.Id, PM = pmId, T = tt.Title }))
                    .FirstOrDefault() > 0;

                if (taskExists) { tasksSkipped++; continue; }

                await _db.SaveDataAsync(@"
                    INSERT INTO Tasks
                        (ProjectId, ProjectMilestoneId, Title, Description,
                         TaskType, Status, DueDate, CreatedByUserId,
                         IsMandatory, IsSystemTask,
                         IsSubmission, SubmissionInstructions,
                         MaxFilesCount, MaxFileSizeMb, AllowedFileTypes)
                    VALUES
                        (@ProjectId, @PmId, @Title, @Description,
                         'System', 'Open', @DueDate, @CreatedBy,
                         0, 0,
                         @IsSubmission, @SubmissionInstructions,
                         @MaxFilesCount, @MaxFileSizeMb, @AllowedFileTypes)",
                    new
                    {
                        ProjectId              = project.Id,
                        PmId                   = pmId,
                        tt.Title,
                        Description            = tt.Description,
                        DueDate                = tt.DueDate,
                        CreatedBy              = authUserId,
                        IsSubmission           = tt.IsSubmission ? 1 : 0,
                        SubmissionInstructions = tt.IsSubmission ? tt.SubmissionInstructions : null,
                        MaxFilesCount          = tt.IsSubmission ? tt.MaxFilesCount   : (int?)null,
                        MaxFileSizeMb          = tt.IsSubmission ? tt.MaxFileSizeMb   : (int?)null,
                        AllowedFileTypes       = tt.IsSubmission ? tt.AllowedFileTypes : null,
                    });

                tasksCreated++;
            }
        }

        return Ok(new ApplyTemplatesResultDto
        {
            ProjectsProcessed = projects.Count,
            MilestonesCreated = milestonesCreated,
            MilestonesSkipped = milestonesSkipped,
            TasksCreated      = tasksCreated,
            TasksSkipped      = tasksSkipped,
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  CYCLE PROGRAM — the milestones configured for ONE academic year
    //
    //  Every row is an AcademicYearMilestones row (AYM): the association of a
    //  MilestoneTemplate with this cycle. That table pre-dates this screen and
    //  is read by the student roadmap, the mentor calendar and the project
    //  workspace; nothing below changes its meaning, it only becomes writable.
    //
    //  Two tables, one screen:
    //     MilestoneTemplates      Title / Description / ProjectTypeId /
    //                             IsRequired / OrderIndex   — SHARED by cycles
    //     AcademicYearMilestones  OpenDate / DueDate / CloseDate / IsActive /
    //                             DisplayOrder              — OWNED by a cycle
    //
    //  ORDER. Resolved as COALESCE(aym.DisplayOrder, mt.OrderIndex) so a cycle
    //  that has never been reordered keeps the template order it always had.
    //  Reordering writes DisplayOrder on this cycle's rows only — it can never
    //  move a milestone in another cycle, which writing mt.OrderIndex would.
    //
    //  DELETION IS GUARDED. An AYM row with ProjectMilestones behind it carries
    //  real per-project progress (status, submissions, tasks). Removing it would
    //  orphan that, so the endpoint refuses and the admin is pointed at
    //  deactivation instead. This mirrors the reference's "מחיקה מהמחזור" being
    //  a removal from the program, not a cascade through student data.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Resolved ordering + the tie-break, restated wherever the program is read.</summary>
    private const string ProgramOrderBy = "ORDER BY COALESCE(aym.DisplayOrder, mt.OrderIndex), aym.Id";

    // ── GET /api/academic-years/{yearId} ─────────────────────────────────────
    //
    // One cycle, in the same shape the list endpoint returns
    // (GET /api/management/academic-years) — same columns, same ProjectCount
    // subquery, same lifecycle derivation — so the detail header and the list
    // row can never disagree about a cycle's status.
    [HttpGet("{yearId:int}")]
    public async Task<IActionResult> GetCycle(int yearId, int authUserId)
    {
        const string sql = @"
            SELECT  ay.Id,
                    ay.Name,
                    ay.StartDate,
                    ay.EndDate,
                    ay.IsActive,
                    ay.IsCurrent,
                    ay.CreatedAt,
                    ay.Status,
                    (SELECT COUNT(DISTINCT pm.ProjectId)
                     FROM   AcademicYearMilestones aym
                     JOIN   ProjectMilestones       pm  ON aym.Id = pm.AcademicYearMilestoneId
                     WHERE  aym.AcademicYearId = ay.Id) AS ProjectCount
            FROM    AcademicYears ay
            WHERE   ay.Id = @Id";

        var row = (await _db.GetRecordsAsync<AcademicYearRow>(sql, new { Id = yearId }))?.FirstOrDefault();
        if (row is null) return NotFound("המחזור לא נמצא");

        return Ok(new AcademicYearDto
        {
            Id           = row.Id,
            Name         = row.Name,
            StartDate    = row.StartDate,
            EndDate      = row.EndDate,
            IsActive     = row.IsActive,
            IsCurrent    = row.IsCurrent,
            CreatedAt    = row.CreatedAt,
            ProjectCount = row.ProjectCount,
            Status = row.Status switch
            {
                "Closed"   => "Closed",
                "Archived" => "Archived",
                _          => row.IsActive ? "Active" : "Inactive",
            },
        });
    }

    // ── GET /api/academic-years/{yearId}/program ─────────────────────────────
    //
    // The cycle's milestones, already sorted. The client renders the list in the
    // order it is handed and never re-sorts, so the "#" column and the up/down
    // controls agree with what a later move endpoint will do.
    [HttpGet("{yearId:int}/program")]
    public async Task<IActionResult> GetProgram(int yearId, int authUserId)
    {
        if (!await CycleExistsAsync(yearId)) return NotFound("המחזור לא נמצא");

        var rows = await LoadProgramAsync(yearId);
        return Ok(rows);
    }

    // ── GET /api/academic-years/{yearId}/available-templates ─────────────────
    //
    // Library templates NOT yet in this cycle — the choices for "החלת תבניות".
    // Already-applied templates are excluded rather than shown disabled: the
    // UNIQUE(AcademicYearId, MilestoneTemplateId) constraint makes re-adding one
    // a no-op, so offering it would be a dead option.
    //
    // Inactive templates are excluded too. An inactive template is one the
    // library has retired; pulling it into a live cycle's program would
    // resurrect it through the back door.
    [HttpGet("{yearId:int}/available-templates")]
    public async Task<IActionResult> GetAvailableTemplates(int yearId, int authUserId)
    {
        if (!await CycleExistsAsync(yearId)) return NotFound("המחזור לא נמצא");

        const string sql = @"
            SELECT  mt.Id,
                    mt.Title,
                    mt.Description,
                    mt.OrderIndex,
                    mt.IsRequired,
                    mt.ProjectTypeId,
                    mt.OpenDate,
                    mt.DueDate,
                    mt.CloseDate,
                    CASE mt.ProjectTypeId
                        WHEN 1 THEN 'טכנולוגי'
                        WHEN 2 THEN 'מתודולוגי'
                        ELSE        'שניהם'
                    END AS Applicability,
                    (SELECT COUNT(1) FROM TaskTemplates tt
                     WHERE  tt.MilestoneTemplateId = mt.Id AND tt.IsActive = 1) AS TaskTemplateCount
            FROM    MilestoneTemplates mt
            WHERE   mt.IsActive = 1
              AND   NOT EXISTS (SELECT 1 FROM AcademicYearMilestones aym
                                WHERE  aym.AcademicYearId      = @YearId
                                  AND  aym.MilestoneTemplateId = mt.Id)
            ORDER   BY mt.OrderIndex, mt.Id";

        var rows = (await _db.GetRecordsAsync<AvailableMilestoneTemplateDto>(
            sql, new { YearId = yearId }))?.ToList()
            ?? new List<AvailableMilestoneTemplateDto>();

        return Ok(rows);
    }

    // ── POST /api/academic-years/{yearId}/program ────────────────────────────
    //
    // Adds a NEW milestone to this cycle. Two writes, in this order:
    //   1. a MilestoneTemplates row  (the definition — name, type, required)
    //   2. an AcademicYearMilestones row binding it to this cycle with the
    //      cycle-level dates and active flag, appended at the end of the order.
    //
    // Step 1 is not optional bookkeeping: every consumer of a cycle milestone
    // in this product (project instantiation, roadmap stages, the mentor
    // calendar) joins through MilestoneTemplateId, so a cycle milestone without
    // a template row cannot exist.
    [HttpPost("{yearId:int}/program")]
    public async Task<IActionResult> CreateProgramMilestone(
        int yearId, [FromBody] SaveCycleMilestoneRequest req, int authUserId)
    {
        var guard = await GuardEditableCycleAsync(yearId);
        if (guard is not null) return guard;

        var err = ValidateMilestone(req);
        if (err is not null) return BadRequest(err);

        if (req.ProjectTypeId.HasValue && !await ProjectTypeExistsAsync(req.ProjectTypeId.Value))
            return BadRequest("סוג הפרויקט לא נמצא");

        // Template order: appended to the library, not squeezed into it.
        int nextTemplateOrder = (await _db.GetRecordsAsync<int>(
            "SELECT COALESCE(MAX(OrderIndex), 0) + 1 FROM MilestoneTemplates")).FirstOrDefault();

        int templateId = await _db.InsertReturnIdAsync(@"
            INSERT INTO MilestoneTemplates
                (Title, Description, OrderIndex, IsRequired, IsActive, ProjectTypeId,
                 OpenDate, DueDate, CloseDate)
            VALUES
                (@Title, @Description, @OrderIndex, @IsRequired, 1, @ProjectTypeId,
                 @OpenDate, @DueDate, NULL)",
            new
            {
                Title       = req.Title.Trim(),
                Description = Nz(req.Description),
                OrderIndex  = nextTemplateOrder,
                IsRequired  = req.IsRequired ? 1 : 0,
                req.ProjectTypeId,
                OpenDate    = D(req.OpenDate),
                DueDate     = D(req.DueDate),
            });

        if (templateId == 0) return StatusCode(500, "שגיאה ביצירת אבן הדרך");

        // Appended to THIS cycle's order. Uses the resolved position of the last
        // row, not MAX(DisplayOrder) — rows that were never reordered still have
        // a NULL DisplayOrder and would otherwise be treated as position 0.
        int nextCycleOrder = await NextCycleOrderAsync(yearId);

        // AcademicYearMilestones.DueDate is NOT NULL. The reference marks both
        // dates optional, so an omitted due date falls back to the cycle's own
        // end date rather than being rejected or written as a sentinel.
        DateTime due = req.DueDate ?? await CycleEndDateAsync(yearId);

        int aymId = await _db.InsertReturnIdAsync(@"
            INSERT INTO AcademicYearMilestones
                (AcademicYearId, MilestoneTemplateId, OpenDate, DueDate, CloseDate,
                 IsActive, DisplayOrder)
            VALUES
                (@YearId, @TemplateId, @OpenDate, @DueDate, NULL, @IsActive, @DisplayOrder)",
            new
            {
                YearId       = yearId,
                TemplateId   = templateId,
                OpenDate     = D(req.OpenDate),
                DueDate      = due.ToString("yyyy-MM-dd"),
                IsActive     = req.IsActive ? 1 : 0,
                DisplayOrder = nextCycleOrder,
            });

        if (aymId == 0) return StatusCode(500, "שגיאה בהוספת אבן הדרך למחזור");

        return Ok(new { id = aymId, milestoneTemplateId = templateId });
    }

    // ── PUT /api/academic-years/{yearId}/program/{aymId} ─────────────────────
    //
    // Writes both tables. The shared fields go to the template — and therefore
    // reach every other cycle using it, which is why the read endpoint returns
    // TemplateCycleUsage and the client warns before saving a shared template.
    // DisplayOrder is deliberately NOT touched here: order is moved with the
    // move endpoint, never as a side effect of an edit.
    [HttpPut("{yearId:int}/program/{aymId:int}")]
    public async Task<IActionResult> UpdateProgramMilestone(
        int yearId, int aymId, [FromBody] SaveCycleMilestoneRequest req, int authUserId)
    {
        var guard = await GuardEditableCycleAsync(yearId);
        if (guard is not null) return guard;

        var err = ValidateMilestone(req);
        if (err is not null) return BadRequest(err);

        if (req.ProjectTypeId.HasValue && !await ProjectTypeExistsAsync(req.ProjectTypeId.Value))
            return BadRequest("סוג הפרויקט לא נמצא");

        int templateId = await TemplateIdForAsync(yearId, aymId);
        if (templateId == 0) return NotFound("אבן הדרך לא נמצאה במחזור זה");

        await _db.SaveDataAsync(@"
            UPDATE MilestoneTemplates
            SET    Title         = @Title,
                   Description   = @Description,
                   IsRequired    = @IsRequired,
                   ProjectTypeId = @ProjectTypeId
            WHERE  Id = @Id",
            new
            {
                Title       = req.Title.Trim(),
                Description = Nz(req.Description),
                IsRequired  = req.IsRequired ? 1 : 0,
                req.ProjectTypeId,
                Id          = templateId,
            });

        DateTime due = req.DueDate ?? await CycleEndDateAsync(yearId);

        await _db.SaveDataAsync(@"
            UPDATE AcademicYearMilestones
            SET    OpenDate = @OpenDate,
                   DueDate  = @DueDate,
                   IsActive = @IsActive
            WHERE  Id = @Id AND AcademicYearId = @YearId",
            new
            {
                OpenDate = D(req.OpenDate),
                DueDate  = due.ToString("yyyy-MM-dd"),
                IsActive = req.IsActive ? 1 : 0,
                Id       = aymId,
                YearId   = yearId,
            });

        return Ok();
    }

    // ── PATCH /api/academic-years/{yearId}/program/{aymId}/toggle-active ─────
    //
    // Per-cycle only: flips AcademicYearMilestones.IsActive, never the
    // template's. Deactivating a milestone here retires it from THIS cycle's
    // program and leaves every other cycle — and the template library — alone.
    [HttpPatch("{yearId:int}/program/{aymId:int}/toggle-active")]
    public async Task<IActionResult> ToggleProgramMilestoneActive(
        int yearId, int aymId, int authUserId)
    {
        var guard = await GuardEditableCycleAsync(yearId);
        if (guard is not null) return guard;

        int affected = await _db.SaveDataAsync(@"
            UPDATE AcademicYearMilestones
            SET    IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END
            WHERE  Id = @Id AND AcademicYearId = @YearId",
            new { Id = aymId, YearId = yearId });

        if (affected == 0) return NotFound("אבן הדרך לא נמצאה במחזור זה");
        return Ok();
    }

    // ── PATCH /api/academic-years/{yearId}/program/{aymId}/move ──────────────
    //
    // One-step reorder. Reads the resolved order, swaps the two neighbours, then
    // normalises DisplayOrder across the WHOLE cycle to 1..n.
    //
    // Normalising rather than swapping two values is what makes this safe on a
    // program that has never been reordered: those rows carry a NULL
    // DisplayOrder, and swapping NULLs would collapse the fallback to
    // mt.OrderIndex for every row at once. After the first move the cycle owns
    // its order outright and the template's OrderIndex stops mattering here.
    [HttpPatch("{yearId:int}/program/{aymId:int}/move")]
    public async Task<IActionResult> MoveProgramMilestone(
        int yearId, int aymId, [FromBody] MoveCycleMilestoneRequest req, int authUserId)
    {
        var guard = await GuardEditableCycleAsync(yearId);
        if (guard is not null) return guard;

        int direction = Math.Sign(req.Direction);
        if (direction == 0) return BadRequest("כיוון ההזזה חייב להיות 1- או 1");

        var ordered = (await LoadProgramAsync(yearId)).ToList();

        int i = ordered.FindIndex(m => m.Id == aymId);
        if (i < 0) return NotFound("אבן הדרך לא נמצאה במחזור זה");

        int j = i + direction;
        if (j < 0 || j >= ordered.Count) return Ok();   // already at an end — no-op, not an error

        (ordered[i], ordered[j]) = (ordered[j], ordered[i]);

        for (int pos = 0; pos < ordered.Count; pos++)
        {
            await _db.SaveDataAsync(
                "UPDATE AcademicYearMilestones SET DisplayOrder = @Pos WHERE Id = @Id AND AcademicYearId = @YearId",
                new { Pos = pos + 1, Id = ordered[pos].Id, YearId = yearId });
        }

        return Ok();
    }

    // ── DELETE /api/academic-years/{yearId}/program/{aymId} ──────────────────
    //
    // Removes the milestone from THIS cycle's program. The MilestoneTemplate
    // survives — it is library content shared with other cycles, and deleting it
    // from here would silently empty another cycle's program.
    //
    // Refused (409) once any project has the milestone instantiated: a
    // ProjectMilestones row carries status, submissions and tasks, and there is
    // no cascade in this product that would clean those up. The admin is told
    // how many projects hold it and pointed at deactivation.
    [HttpDelete("{yearId:int}/program/{aymId:int}")]
    public async Task<IActionResult> DeleteProgramMilestone(int yearId, int aymId, int authUserId)
    {
        var guard = await GuardEditableCycleAsync(yearId);
        if (guard is not null) return guard;

        bool exists = (await _db.GetRecordsAsync<int>(
            "SELECT 1 FROM AcademicYearMilestones WHERE Id = @Id AND AcademicYearId = @YearId LIMIT 1",
            new { Id = aymId, YearId = yearId }))?.Any() ?? false;

        if (!exists) return NotFound("אבן הדרך לא נמצאה במחזור זה");

        int projectCount = (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM ProjectMilestones WHERE AcademicYearMilestoneId = @Id",
            new { Id = aymId })).FirstOrDefault();

        if (projectCount > 0)
            return Conflict(
                $"לא ניתן להסיר את אבן הדרך — היא כבר משויכת ל־{projectCount} פרויקטים במחזור. " +
                "ניתן להשבית אותה במקום זאת.");

        await _db.SaveDataAsync(
            "DELETE FROM AcademicYearMilestones WHERE Id = @Id AND AcademicYearId = @YearId",
            new { Id = aymId, YearId = yearId });

        return Ok();
    }

    // ── POST /api/academic-years/{yearId}/program/apply-templates ────────────
    //
    // "החלת תבניות" — adds library templates to this cycle's PROGRAM.
    //
    // NOT the same operation as POST {yearId}/apply-templates above, and the two
    // are deliberately kept apart:
    //   • this one   templates → AcademicYearMilestones  (defines the program)
    //   • that one   program   → ProjectMilestones + Tasks (rolls it out to projects)
    // The reference puts this one in the Cycle Program menu, which is exactly
    // its scope; the rollout stays a separate, explicitly-confirmed action.
    //
    // Idempotent by construction: INSERT OR IGNORE against
    // UNIQUE(AcademicYearId, MilestoneTemplateId), and the skipped count is
    // reported rather than swallowed.
    [HttpPost("{yearId:int}/program/apply-templates")]
    public async Task<IActionResult> ApplyTemplatesToProgram(
        int yearId, [FromBody] ApplyMilestoneTemplatesRequest req, int authUserId)
    {
        var guard = await GuardEditableCycleAsync(yearId);
        if (guard is not null) return guard;

        var ids = (req.TemplateIds ?? new List<int>()).Distinct().Where(id => id > 0).ToList();
        if (ids.Count == 0) return BadRequest("לא נבחרו תבניות");

        DateTime cycleEnd = await CycleEndDateAsync(yearId);
        int nextOrder     = await NextCycleOrderAsync(yearId);

        int added = 0, skipped = 0;

        foreach (int templateId in ids)
        {
            // Read the template's own defaults — the cycle inherits them and the
            // admin adjusts from there, which is what the OpenDate/DueDate
            // columns on MilestoneTemplates were added for.
            var tpl = (await _db.GetRecordsAsync<TemplateDefaultsRow>(@"
                SELECT Id, OpenDate, DueDate, CloseDate
                FROM   MilestoneTemplates
                WHERE  Id = @Id AND IsActive = 1",
                new { Id = templateId }))?.FirstOrDefault();

            if (tpl is null) { skipped++; continue; }

            int inserted = await _db.SaveDataAsync(@"
                INSERT OR IGNORE INTO AcademicYearMilestones
                    (AcademicYearId, MilestoneTemplateId, OpenDate, DueDate, CloseDate,
                     IsActive, DisplayOrder)
                VALUES
                    (@YearId, @TemplateId, @OpenDate, @DueDate, @CloseDate, 1, @DisplayOrder)",
                new
                {
                    YearId       = yearId,
                    TemplateId   = templateId,
                    OpenDate     = D(tpl.OpenDate),
                    DueDate      = (tpl.DueDate ?? cycleEnd).ToString("yyyy-MM-dd"),
                    CloseDate    = D(tpl.CloseDate),
                    DisplayOrder = nextOrder,
                });

            if (inserted > 0) { added++; nextOrder++; }
            else              { skipped++; }
        }

        return Ok(new ApplyMilestoneTemplatesResultDto { Added = added, Skipped = skipped });
    }

    // ── Cycle-program helpers ────────────────────────────────────────────────

    /// <summary>
    /// The cycle's milestones in resolved order, with both counts attached.
    /// Single source for the read endpoint AND the move endpoint, so a reorder
    /// operates on exactly the list the admin was looking at.
    /// </summary>
    private async Task<List<CycleMilestoneDto>> LoadProgramAsync(int yearId)
    {
        string sql = @"
            SELECT  aym.Id,
                    aym.AcademicYearId,
                    aym.MilestoneTemplateId,
                    mt.Title,
                    mt.Description,
                    mt.IsRequired,
                    mt.ProjectTypeId,
                    COALESCE(aym.DisplayOrder, mt.OrderIndex) AS OrderIndex,
                    aym.OpenDate,
                    aym.DueDate,
                    aym.CloseDate,
                    aym.IsActive,
                    aym.RoadmapStageId,
                    CASE mt.ProjectTypeId
                        WHEN 1 THEN 'טכנולוגי'
                        WHEN 2 THEN 'מתודולוגי'
                        ELSE        'שניהם'
                    END AS Applicability,
                    (SELECT COUNT(1) FROM TaskTemplates tt
                     WHERE  tt.MilestoneTemplateId = mt.Id AND tt.IsActive = 1) AS TaskTemplateCount,
                    (SELECT COUNT(1) FROM ProjectMilestones pm
                     WHERE  pm.AcademicYearMilestoneId = aym.Id)                AS ProjectCount,
                    (SELECT COUNT(DISTINCT a2.AcademicYearId) FROM AcademicYearMilestones a2
                     WHERE  a2.MilestoneTemplateId = mt.Id)                     AS TemplateCycleUsage
            FROM    AcademicYearMilestones aym
            JOIN    MilestoneTemplates     mt ON mt.Id = aym.MilestoneTemplateId
            WHERE   aym.AcademicYearId = @YearId
            " + ProgramOrderBy;

        return (await _db.GetRecordsAsync<CycleMilestoneDto>(sql, new { YearId = yearId }))?.ToList()
               ?? new List<CycleMilestoneDto>();
    }

    /// <summary>
    /// Next position at the end of this cycle's program. Counts rows rather than
    /// reading MAX(DisplayOrder): an un-reordered cycle has NULL everywhere, and
    /// MAX would return NULL → 1 and stack every new milestone at the top.
    /// </summary>
    private async Task<int> NextCycleOrderAsync(int yearId)
    {
        int count = (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM AcademicYearMilestones WHERE AcademicYearId = @YearId",
            new { YearId = yearId })).FirstOrDefault();

        int maxOrder = (await _db.GetRecordsAsync<int>(@"
            SELECT COALESCE(MAX(COALESCE(aym.DisplayOrder, mt.OrderIndex)), 0)
            FROM   AcademicYearMilestones aym
            JOIN   MilestoneTemplates     mt ON mt.Id = aym.MilestoneTemplateId
            WHERE  aym.AcademicYearId = @YearId",
            new { YearId = yearId })).FirstOrDefault();

        return Math.Max(count, maxOrder) + 1;
    }

    /// <summary>The cycle's end date — the fallback for the NOT NULL AYM.DueDate
    /// when the admin leaves the optional due date empty.</summary>
    private async Task<DateTime> CycleEndDateAsync(int yearId)
    {
        var rows = await _db.GetRecordsAsync<DateTime>(
            "SELECT EndDate FROM AcademicYears WHERE Id = @Id", new { Id = yearId });
        var end = rows?.FirstOrDefault() ?? default;
        return end == default ? DateTime.Today : end;
    }

    private async Task<int> TemplateIdForAsync(int yearId, int aymId) =>
        (await _db.GetRecordsAsync<int>(
            "SELECT MilestoneTemplateId FROM AcademicYearMilestones WHERE Id = @Id AND AcademicYearId = @YearId",
            new { Id = aymId, YearId = yearId })).FirstOrDefault();

    private async Task<bool> CycleExistsAsync(int yearId) =>
        (await _db.GetRecordsAsync<int>(
            "SELECT 1 FROM AcademicYears WHERE Id = @Id LIMIT 1", new { Id = yearId }))?.Any() ?? false;

    private async Task<bool> ProjectTypeExistsAsync(int id) =>
        (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM ProjectTypes WHERE Id = @Id", new { Id = id })).FirstOrDefault() > 0;

    /// <summary>
    /// Every program write goes through this. Mirrors the guard the cycle
    /// endpoints already enforce in ManagementController: a Closed or Archived
    /// cycle is immutable, and its program is part of it — letting milestones be
    /// edited inside a closed cycle would reopen it through a side door.
    /// </summary>
    private async Task<IActionResult?> GuardEditableCycleAsync(int yearId)
    {
        var status = (await _db.GetRecordsAsync<string>(@"
            SELECT COALESCE(CASE WHEN Status IN ('Closed','Archived') THEN Status ELSE '' END, '')
            FROM   AcademicYears
            WHERE  Id = @Id",
            new { Id = yearId }))?.ToList();

        if (status is null || status.Count == 0) return NotFound("המחזור לא נמצא");

        return status[0] is "Closed" or "Archived"
            ? BadRequest("לא ניתן לערוך את תוכנית מחזור סגור או מאורכב")
            : null;
    }

    private static string? ValidateMilestone(SaveCycleMilestoneRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return "שם אבן הדרך הוא שדה חובה";

        if (req.OpenDate is not null && req.DueDate is not null
            && req.DueDate.Value.Date < req.OpenDate.Value.Date)
            return "תאריך היעד חייב להיות אחרי תאריך ההתחלה";

        return null;
    }

    /// <summary>Trims to null — the DB stores absent text as NULL, not "".</summary>
    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Dates are stored as yyyy-MM-dd text, as everywhere else in this codebase.</summary>
    private static string? D(DateTime? d) => d?.ToString("yyyy-MM-dd");

    private sealed class TemplateDefaultsRow
    {
        public int       Id        { get; set; }
        public DateTime? OpenDate  { get; set; }
        public DateTime? DueDate   { get; set; }
        public DateTime? CloseDate { get; set; }
    }

    /// <summary>Dapper row for the cycle header read. Mirrors the private row
    /// type ManagementController uses for the list, so the two stay in step.</summary>
    private sealed class AcademicYearRow
    {
        public int      Id           { get; set; }
        public string   Name         { get; set; } = "";
        public DateTime StartDate    { get; set; }
        public DateTime EndDate      { get; set; }
        public bool     IsActive     { get; set; }
        public bool     IsCurrent    { get; set; }
        public DateTime CreatedAt    { get; set; }
        public string?  Status       { get; set; }
        public int      ProjectCount { get; set; }
    }

    // ── Private Dapper row types ─────────────────────────────────────────────

    private sealed class ProjectRow
    {
        public int  Id            { get; set; }
        public int? ProjectTypeId { get; set; }
    }

    private sealed class AymRow
    {
        public int      Id                     { get; set; }
        public int      MilestoneTemplateId    { get; set; }
        public DateTime DueDate                { get; set; }
        /// <summary>NULL = applies to all project types.</summary>
        public int?     MilestoneProjectTypeId { get; set; }
    }

    private sealed class TaskTemplateRow
    {
        public int       Id                     { get; set; }
        public string    Title                  { get; set; } = "";
        public string?   Description            { get; set; }
        public int       MilestoneTemplateId    { get; set; }
        public DateTime  DueDate                { get; set; }
        public bool      IsSubmission           { get; set; }
        public string?   SubmissionInstructions { get; set; }
        public int?      MaxFilesCount          { get; set; }
        public int?      MaxFileSizeMb          { get; set; }
        public string?   AllowedFileTypes       { get; set; }
    }
}
