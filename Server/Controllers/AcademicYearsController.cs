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
