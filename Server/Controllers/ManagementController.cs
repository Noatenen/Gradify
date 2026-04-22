using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

/// <summary>
/// Admin/staff management operations.
/// All endpoints require Admin or Staff role.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public class ManagementController : ControllerBase
{
    private readonly DbRepository _db;

    public ManagementController(DbRepository db) => _db = db;

    // ── GET /api/management/projects ────────────────────────────────────────
    // Returns all projects with resolved type name, team size, and derived
    // academic year (taken from the team's first active member).
    [HttpGet("projects")]
    public async Task<IActionResult> GetProjects(int authUserId)
    {
        const string sql = @"
            SELECT  p.Id,
                    p.ProjectNumber,
                    p.Title,
                    p.Description,
                    p.Status,
                    p.HealthStatus,
                    pt.Name  AS ProjectType,
                    pt.Id    AS ProjectTypeId,
                    t.Id     AS TeamId,
                    (SELECT COUNT(*)
                     FROM   TeamMembers tm2
                     WHERE  tm2.TeamId = t.Id AND tm2.IsActive = 1)  AS TeamSize,
                    COALESCE(
                        (SELECT u.AcademicYear
                         FROM   TeamMembers tm2
                         JOIN   Users       u  ON tm2.UserId = u.Id
                         WHERE  tm2.TeamId = t.Id AND tm2.IsActive = 1
                         LIMIT  1),
                    '')                                               AS AcademicYear,
                    p.OrganizationName,
                    p.ContactPerson,
                    p.ContactRole,
                    p.Goals,
                    p.TargetAudience,
                    p.InternalNotes,
                    p.Priority,
                    COALESCE(p.SourceType, 'Manual')                 AS SourceType
            FROM    Projects     p
            JOIN    Teams        t   ON p.TeamId       = t.Id
            JOIN    ProjectTypes pt  ON p.ProjectTypeId = pt.Id
            ORDER   BY p.ProjectNumber";

        var rows = await _db.GetRecordsAsync<ProjectManagementDto>(sql);
        return Ok(rows ?? Enumerable.Empty<ProjectManagementDto>());
    }

    // ── GET /api/management/project-types ────────────────────────────────────
    // Returns all project types for the Add Project form dropdown.
    [HttpGet("project-types")]
    public async Task<IActionResult> GetProjectTypes(int authUserId)
    {
        const string sql = "SELECT Id, Name FROM ProjectTypes ORDER BY Name";
        var types = await _db.GetRecordsAsync<ProjectTypeOptionDto>(sql);
        return Ok(types ?? Enumerable.Empty<ProjectTypeOptionDto>());
    }

    // ── POST /api/management/projects ────────────────────────────────────────
    // Creates a new Team record, then the Project linked to it.
    [HttpPost("projects")]
    public async Task<IActionResult> CreateProject([FromBody] CreateProjectRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("שם הפרויקט הוא שדה חובה");

        if (req.ProjectNumber <= 0)
            return BadRequest("מספר פרויקט חייב להיות חיובי");

        if (req.ProjectTypeId <= 0)
            return BadRequest("יש לבחור סוג פרויקט");

        // Verify project type exists
        const string typeSql = "SELECT COUNT(1) FROM ProjectTypes WHERE Id = @Id";
        var typeCount = (await _db.GetRecordsAsync<int>(typeSql, new { Id = req.ProjectTypeId })).FirstOrDefault();
        if (typeCount == 0)
            return BadRequest("סוג הפרויקט לא נמצא");

        // Check project number is unique
        const string dupSql = "SELECT COUNT(1) FROM Projects WHERE ProjectNumber = @ProjectNumber";
        var dupCount = (await _db.GetRecordsAsync<int>(dupSql, new { req.ProjectNumber })).FirstOrDefault();
        if (dupCount > 0)
            return BadRequest("מספר פרויקט זה כבר קיים במערכת");

        // Create the team first
        const string insertTeamSql = "INSERT INTO Teams DEFAULT VALUES";
        int teamId = await _db.InsertReturnIdAsync(insertTeamSql);
        if (teamId == 0)
            return StatusCode(500, "שגיאה ביצירת הצוות");

        // Create the project
        const string insertProjectSql = @"
            INSERT INTO Projects
                (ProjectNumber, Title, Description, Status, TeamId, ProjectTypeId,
                 OrganizationName, ContactPerson, ContactRole,
                 Goals, TargetAudience, InternalNotes, Priority)
            VALUES
                (@ProjectNumber, @Title, @Description, 'Active', @TeamId, @ProjectTypeId,
                 @OrganizationName, @ContactPerson, @ContactRole,
                 @Goals, @TargetAudience, @InternalNotes, @Priority)";

        int projectId = await _db.InsertReturnIdAsync(insertProjectSql, new
        {
            req.ProjectNumber,
            Title           = req.Title.Trim(),
            Description     = Nz(req.Description),
            TeamId          = teamId,
            req.ProjectTypeId,
            OrganizationName = Nz(req.OrganizationName),
            ContactPerson    = Nz(req.ContactPerson),
            ContactRole      = Nz(req.ContactRole),
            Goals            = Nz(req.Goals),
            TargetAudience   = Nz(req.TargetAudience),
            InternalNotes    = Nz(req.InternalNotes),
            Priority         = Nz(req.Priority),
        });

        if (projectId == 0)
            return StatusCode(500, "שגיאה ביצירת הפרויקט");

        return Ok(new { id = projectId, teamId });
    }

    // ── PATCH /api/management/projects/{id}/status ───────────────────────────
    // Updates the project's Status field (e.g. Active ↔ Inactive).
    [HttpPatch("projects/{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateProjectStatusRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.Status))
            return BadRequest("סטטוס לא תקין");

        const string sql = "UPDATE Projects SET Status = @Status WHERE Id = @Id";
        int affected = await _db.SaveDataAsync(sql, new { Status = req.Status, Id = id });

        if (affected == 0)
            return NotFound("הפרויקט לא נמצא");

        return Ok();
    }

    // ── PUT /api/management/projects/{id} ────────────────────────────────────
    // Full update of all editable project fields.
    [HttpPut("projects/{id:int}")]
    public async Task<IActionResult> UpdateProject(int id, [FromBody] UpdateProjectRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("שם הפרויקט הוא שדה חובה");

        if (req.ProjectNumber <= 0)
            return BadRequest("מספר פרויקט חייב להיות חיובי");

        if (req.ProjectTypeId <= 0)
            return BadRequest("יש לבחור סוג פרויקט");

        // Verify project type exists
        var typeCount = (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM ProjectTypes WHERE Id = @Id", new { Id = req.ProjectTypeId })).FirstOrDefault();
        if (typeCount == 0)
            return BadRequest("סוג הפרויקט לא נמצא");

        // Check project number uniqueness (exclude current project)
        var dupCount = (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM Projects WHERE ProjectNumber = @ProjectNumber AND Id != @Id",
            new { req.ProjectNumber, Id = id })).FirstOrDefault();
        if (dupCount > 0)
            return BadRequest("מספר פרויקט זה כבר קיים במערכת");

        const string sql = @"
            UPDATE Projects
            SET    ProjectNumber    = @ProjectNumber,
                   Title            = @Title,
                   Description      = @Description,
                   ProjectTypeId    = @ProjectTypeId,
                   Status           = @Status,
                   OrganizationName = @OrganizationName,
                   ContactPerson    = @ContactPerson,
                   ContactRole      = @ContactRole,
                   Goals            = @Goals,
                   TargetAudience   = @TargetAudience,
                   InternalNotes    = @InternalNotes,
                   Priority         = @Priority
            WHERE  Id = @Id";

        int affected = await _db.SaveDataAsync(sql, new
        {
            req.ProjectNumber,
            Title            = req.Title.Trim(),
            Description      = Nz(req.Description),
            req.ProjectTypeId,
            Status           = string.IsNullOrWhiteSpace(req.Status) ? "Active" : req.Status,
            OrganizationName = Nz(req.OrganizationName),
            ContactPerson    = Nz(req.ContactPerson),
            ContactRole      = Nz(req.ContactRole),
            Goals            = Nz(req.Goals),
            TargetAudience   = Nz(req.TargetAudience),
            InternalNotes    = Nz(req.InternalNotes),
            Priority         = Nz(req.Priority),
            Id               = id,
        });

        if (affected == 0)
            return NotFound("הפרויקט לא נמצא");

        return Ok();
    }

    /// <summary>Returns null for empty/whitespace strings; trims non-empty ones.</summary>
    private static string? Nz(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ════════════════════════════════════════════════════════════════════════════
    //  ACADEMIC YEARS (CYCLES)
    // ════════════════════════════════════════════════════════════════════════════

    // ── GET /api/management/academic-years ──────────────────────────────────
    // Returns all academic years with lifecycle status and project count.
    [HttpGet("academic-years")]
    public async Task<IActionResult> GetAcademicYears(int authUserId)
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
            ORDER   BY ay.StartDate DESC";

        var rows = await _db.GetRecordsAsync<AcademicYearRow>(sql);

        var result = rows?.Select(r => new AcademicYearDto
        {
            Id           = r.Id,
            Name         = r.Name,
            StartDate    = r.StartDate,
            EndDate      = r.EndDate,
            IsActive     = r.IsActive,
            IsCurrent    = r.IsCurrent,
            CreatedAt    = r.CreatedAt,
            ProjectCount = r.ProjectCount,
            // Derive lifecycle status: explicit Status column takes precedence over IsActive flag
            Status = r.Status switch
            {
                "Closed"   => "Closed",
                "Archived" => "Archived",
                _          => r.IsActive ? "Active" : "Inactive",
            },
        }).ToList() ?? new List<AcademicYearDto>();

        return Ok(result);
    }

    // ── POST /api/management/academic-years ─────────────────────────────────
    [HttpPost("academic-years")]
    public async Task<IActionResult> CreateAcademicYear([FromBody] SaveAcademicYearRequest req, int authUserId)
    {
        var validationError = ValidateAcademicYear(req);
        if (validationError != null) return BadRequest(validationError);

        if (req.IsCurrent)
            await _db.SaveDataAsync("UPDATE AcademicYears SET IsCurrent = 0");

        const string sql = @"
            INSERT INTO AcademicYears (Name, StartDate, EndDate, IsActive, IsCurrent)
            VALUES (@Name, @StartDate, @EndDate, @IsActive, @IsCurrent)";

        int newId = await _db.InsertReturnIdAsync(sql, new
        {
            Name      = req.Name.Trim(),
            StartDate = req.StartDate.ToString("yyyy-MM-dd"),
            EndDate   = req.EndDate.ToString("yyyy-MM-dd"),
            IsActive  = req.IsActive ? 1 : 0,
            IsCurrent = req.IsCurrent ? 1 : 0,
        });

        if (newId == 0)
            return StatusCode(500, "שגיאה ביצירת המחזור");

        return Ok(new { id = newId });
    }

    // ── PUT /api/management/academic-years/{id} ──────────────────────────────
    [HttpPut("academic-years/{id:int}")]
    public async Task<IActionResult> UpdateAcademicYear(int id, [FromBody] SaveAcademicYearRequest req, int authUserId)
    {
        var validationError = ValidateAcademicYear(req);
        if (validationError != null) return BadRequest(validationError);

        // Guard: Closed or Archived cycles are immutable — use dedicated business actions
        var lifecycleStatus = await GetLifecycleStatusAsync(id);
        if (lifecycleStatus is null)                    return NotFound("המחזור לא נמצא");
        if (lifecycleStatus is "Closed" or "Archived")  return BadRequest("לא ניתן לערוך מחזור סגור או מאורכב");

        if (req.IsCurrent)
            await _db.SaveDataAsync("UPDATE AcademicYears SET IsCurrent = 0");

        const string sql = @"
            UPDATE AcademicYears
            SET    Name      = @Name,
                   StartDate = @StartDate,
                   EndDate   = @EndDate,
                   IsActive  = @IsActive,
                   IsCurrent = @IsCurrent
            WHERE  Id = @Id";

        int affected = await _db.SaveDataAsync(sql, new
        {
            Name      = req.Name.Trim(),
            StartDate = req.StartDate.ToString("yyyy-MM-dd"),
            EndDate   = req.EndDate.ToString("yyyy-MM-dd"),
            IsActive  = req.IsActive ? 1 : 0,
            IsCurrent = req.IsCurrent ? 1 : 0,
            Id        = id,
        });

        if (affected == 0)
            return NotFound("המחזור לא נמצא");

        return Ok();
    }

    // ── PATCH /api/management/academic-years/{id}/set-current ────────────────
    // Sets this year as the current one. Clears IsCurrent on all others.
    // Also activates the year (a non-active year cannot be current).
    // Guard: Closed or Archived cycles cannot be made current.
    [HttpPatch("academic-years/{id:int}/set-current")]
    public async Task<IActionResult> SetCurrentYear(int id, int authUserId)
    {
        var lifecycleStatus = await GetLifecycleStatusAsync(id);
        if (lifecycleStatus is null)          return NotFound("המחזור לא נמצא");
        if (lifecycleStatus is "Closed" or "Archived")
            return BadRequest("לא ניתן להגדיר מחזור סגור או מאורכב כנוכחי");

        await _db.SaveDataAsync("UPDATE AcademicYears SET IsCurrent = 0");
        await _db.SaveDataAsync(
            "UPDATE AcademicYears SET IsCurrent = 1, IsActive = 1, Status = NULL WHERE Id = @Id",
            new { Id = id });

        return Ok();
    }

    // ── PATCH /api/management/academic-years/{id}/toggle-active ─────────────
    // Toggles IsActive for Active ↔ Inactive cycles only.
    // Guard: Closed or Archived cycles must use dedicated business actions.
    [HttpPatch("academic-years/{id:int}/toggle-active")]
    public async Task<IActionResult> ToggleYearActive(int id, int authUserId)
    {
        var lifecycleStatus = await GetLifecycleStatusAsync(id);
        if (lifecycleStatus is null)          return NotFound("המחזור לא נמצא");
        if (lifecycleStatus is "Closed" or "Archived")
            return BadRequest("לא ניתן לשנות סטטוס של מחזור סגור או מאורכב");

        await _db.SaveDataAsync(
            "UPDATE AcademicYears SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END, Status = NULL WHERE Id = @Id",
            new { Id = id });

        return Ok();
    }

    // ── POST /api/management/academic-years/{id}/close ───────────────────────
    // Business action: closes the cycle. Sets Status = "Closed", IsActive = 0.
    // Future: may trigger project status updates, report generation, etc.
    [HttpPost("academic-years/{id:int}/close")]
    public async Task<IActionResult> CloseYear(int id, int authUserId)
    {
        var lifecycleStatus = await GetLifecycleStatusAsync(id);
        if (lifecycleStatus is null) return NotFound("המחזור לא נמצא");
        if (lifecycleStatus is "Closed" or "Archived")
            return BadRequest("המחזור כבר סגור או מאורכב");

        await _db.SaveDataAsync(
            "UPDATE AcademicYears SET Status = 'Closed', IsActive = 0, IsCurrent = 0 WHERE Id = @Id",
            new { Id = id });

        // TODO: future — cascade status changes to Projects in this cycle

        return Ok();
    }

    // ── POST /api/management/academic-years/{id}/archive ─────────────────────
    // Business action: archives the cycle. Requires the cycle to be Closed first.
    [HttpPost("academic-years/{id:int}/archive")]
    public async Task<IActionResult> ArchiveYear(int id, int authUserId)
    {
        var lifecycleStatus = await GetLifecycleStatusAsync(id);
        if (lifecycleStatus is null)       return NotFound("המחזור לא נמצא");
        if (lifecycleStatus != "Closed")   return BadRequest("ניתן לארכב רק מחזורים שסטטוסם 'סגור'");

        await _db.SaveDataAsync(
            "UPDATE AcademicYears SET Status = 'Archived', IsActive = 0, IsCurrent = 0 WHERE Id = @Id",
            new { Id = id });

        return Ok();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string? ValidateAcademicYear(SaveAcademicYearRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))   return "שם המחזור הוא שדה חובה";
        if (req.EndDate <= req.StartDate)           return "תאריך סיום חייב להיות אחרי תאריך התחלה";
        return null;
    }

    /// <summary>
    /// Returns the resolved lifecycle status for the given academic year, or null if not found.
    /// Empty string means row exists with NULL Status (maps to Active/Inactive via IsActive).
    /// </summary>
    private async Task<string?> GetLifecycleStatusAsync(int id)
    {
        const string sql = @"
            SELECT COALESCE(
                       CASE WHEN Status IN ('Closed','Archived') THEN Status ELSE '' END,
                   '')
            FROM   AcademicYears
            WHERE  Id = @Id";
        var results = await _db.GetRecordsAsync<string>(sql, new { Id = id });
        if (results is null) return null;
        var row = results.FirstOrDefault();
        // FirstOrDefault returns null when the enumerable is empty (= no row found)
        return row;
    }

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
}