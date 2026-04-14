using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  CatalogController — /api/catalog
//
//  Manages the Project Catalog (proposals pool).
//  Covers ALL rows in the Projects table (both unassigned and assigned).
//    TeamId IS NULL     → catalog/proposal only (not yet assigned)
//    TeamId IS NOT NULL → assigned; also an active project
//
//  SourceType values: "Manual" (created in Gradify) | "Airtable" (synced)
//  Priority values: null | "Low" | "Medium" | "High"
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/[controller]")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public class CatalogController : ControllerBase
{
    private readonly DbRepository _db;

    public CatalogController(DbRepository db) => _db = db;

    // ── GET /api/catalog ────────────────────────────────────────────────────
    // Returns all catalog projects with assignment info (first member name + count).
    [HttpGet]
    public async Task<IActionResult> GetCatalog(int authUserId)
    {
        const string sql = @"
            SELECT  p.Id,
                    p.ProjectNumber,
                    p.Title,
                    pt.Name  AS ProjectType,
                    pt.Id    AS ProjectTypeId,
                    p.AcademicYearId,
                    ay.Name  AS AcademicYear,
                    p.Status,
                    COALESCE(p.SourceType, 'Manual')  AS SourceType,
                    p.OrganizationName,
                    p.Priority,
                    CASE WHEN p.TeamId IS NOT NULL THEN 1 ELSE 0 END AS IsAssigned,
                    CASE
                        WHEN p.TeamId IS NOT NULL THEN
                            (SELECT u.FirstName || ' ' || u.LastName
                             FROM   TeamMembers tm
                             JOIN   Users       u  ON tm.UserId = u.Id
                             WHERE  tm.TeamId = p.TeamId AND tm.IsActive = 1
                             ORDER  BY tm.Id
                             LIMIT  1)
                        ELSE NULL
                    END AS AssignedFirstMemberName,
                    CASE
                        WHEN p.TeamId IS NOT NULL THEN
                            (SELECT COUNT(*)
                             FROM   TeamMembers tm2
                             WHERE  tm2.TeamId = p.TeamId AND tm2.IsActive = 1)
                        ELSE 0
                    END AS AssignedTeamSize,
                    p.CreatedAt
            FROM    Projects     p
            JOIN    AcademicYears ay ON p.AcademicYearId = ay.Id
            JOIN    ProjectTypes  pt ON p.ProjectTypeId  = pt.Id
            ORDER   BY p.AcademicYearId DESC, p.ProjectNumber";

        var rows = await _db.GetRecordsAsync<CatalogProjectListDto>(sql);
        return Ok(rows ?? Enumerable.Empty<CatalogProjectListDto>());
    }

    // ── GET /api/catalog/{id} ───────────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCatalogProject(int id, int authUserId)
    {
        const string sql = @"
            SELECT  p.Id,
                    p.ProjectNumber,
                    p.Title,
                    p.Description,
                    pt.Name  AS ProjectType,
                    pt.Id    AS ProjectTypeId,
                    p.AcademicYearId,
                    ay.Name  AS AcademicYear,
                    p.Status,
                    COALESCE(p.SourceType, 'Manual')  AS SourceType,
                    p.AirtableRecordId,
                    p.OrganizationName,
                    p.ContactPerson,
                    p.ContactRole,
                    p.Goals,
                    p.TargetAudience,
                    p.InternalNotes,
                    p.Priority,
                    CASE WHEN p.TeamId IS NOT NULL THEN 1 ELSE 0 END AS IsAssigned,
                    p.TeamId,
                    CASE
                        WHEN p.TeamId IS NOT NULL THEN
                            (SELECT u.FirstName || ' ' || u.LastName
                             FROM   TeamMembers tm
                             JOIN   Users       u  ON tm.UserId = u.Id
                             WHERE  tm.TeamId = p.TeamId AND tm.IsActive = 1
                             ORDER  BY tm.Id
                             LIMIT  1)
                        ELSE NULL
                    END AS AssignedFirstMemberName,
                    CASE
                        WHEN p.TeamId IS NOT NULL THEN
                            (SELECT COUNT(*)
                             FROM   TeamMembers tm2
                             WHERE  tm2.TeamId = p.TeamId AND tm2.IsActive = 1)
                        ELSE 0
                    END AS AssignedTeamSize,
                    p.CreatedAt,
                    p.UpdatedAt
            FROM    Projects     p
            JOIN    AcademicYears ay ON p.AcademicYearId = ay.Id
            JOIN    ProjectTypes  pt ON p.ProjectTypeId  = pt.Id
            WHERE   p.Id = @Id";

        var rows  = await _db.GetRecordsAsync<CatalogProjectDetailDto>(sql, new { Id = id });
        var project = rows?.FirstOrDefault();
        if (project is null) return NotFound("הפרויקט לא נמצא");

        return Ok(project);
    }

    // ── POST /api/catalog ───────────────────────────────────────────────────
    // Creates a new catalog project. TeamId stays NULL (unassigned).
    [HttpPost]
    public async Task<IActionResult> CreateCatalogProject(
        [FromBody] SaveCatalogProjectRequest req, int authUserId)
    {
        var err = Validate(req);
        if (err != null) return BadRequest(err);

        if (await AcademicYearExistsAsync(req.AcademicYearId) == false)
            return BadRequest("המחזור האקדמי לא נמצא");

        if (await ProjectTypeExistsAsync(req.ProjectTypeId) == false)
            return BadRequest("סוג הפרויקט לא נמצא");

        var dupCount = (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM Projects WHERE ProjectNumber = @ProjectNumber",
            new { req.ProjectNumber })).FirstOrDefault();
        if (dupCount > 0) return BadRequest("מספר פרויקט זה כבר קיים במערכת");

        const string sql = @"
            INSERT INTO Projects
                (ProjectNumber, Title, Description, Status, AcademicYearId, ProjectTypeId,
                 SourceType, OrganizationName, ContactPerson, ContactRole,
                 Goals, TargetAudience, InternalNotes, Priority)
            VALUES
                (@ProjectNumber, @Title, @Description,
                 COALESCE(@Status, 'Available'),
                 @AcademicYearId, @ProjectTypeId,
                 COALESCE(@SourceType, 'Manual'),
                 @OrganizationName, @ContactPerson, @ContactRole,
                 @Goals, @TargetAudience, @InternalNotes, @Priority)";

        int newId = await _db.InsertReturnIdAsync(sql, BuildParams(req));
        if (newId == 0) return StatusCode(500, "שגיאה ביצירת הפרויקט");

        return Ok(new { id = newId });
    }

    // ── PUT /api/catalog/{id} ───────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateCatalogProject(
        int id, [FromBody] SaveCatalogProjectRequest req, int authUserId)
    {
        var err = Validate(req);
        if (err != null) return BadRequest(err);

        const string sql = @"
            UPDATE Projects
            SET    ProjectNumber   = @ProjectNumber,
                   Title           = @Title,
                   Description     = @Description,
                   AcademicYearId  = @AcademicYearId,
                   ProjectTypeId   = @ProjectTypeId,
                   Status          = COALESCE(@Status, Status),
                   SourceType      = COALESCE(@SourceType, SourceType),
                   OrganizationName = @OrganizationName,
                   ContactPerson   = @ContactPerson,
                   ContactRole     = @ContactRole,
                   Goals           = @Goals,
                   TargetAudience  = @TargetAudience,
                   InternalNotes   = @InternalNotes,
                   Priority        = @Priority,
                   UpdatedAt       = datetime('now')
            WHERE  Id = @Id";

        var p = BuildParams(req);
        int affected = await _db.SaveDataAsync(sql, new
        {
            p.ProjectNumber, p.Title, p.Description, p.AcademicYearId, p.ProjectTypeId,
            p.Status, p.SourceType, p.OrganizationName, p.ContactPerson, p.ContactRole,
            p.Goals, p.TargetAudience, p.InternalNotes, p.Priority,
            Id = id,
        });

        if (affected == 0) return NotFound("הפרויקט לא נמצא");
        return Ok();
    }

    // ── PATCH /api/catalog/{id}/toggle-available ────────────────────────────
    [HttpPatch("{id:int}/toggle-available")]
    public async Task<IActionResult> ToggleAvailability(int id, int authUserId)
    {
        const string sql = @"
            UPDATE Projects
            SET    Status    = CASE WHEN Status = 'Available' THEN 'Unavailable' ELSE 'Available' END,
                   UpdatedAt = datetime('now')
            WHERE  Id = @Id";

        int affected = await _db.SaveDataAsync(sql, new { Id = id });
        if (affected == 0) return NotFound("הפרויקט לא נמצא");
        return Ok();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string? Validate(SaveCatalogProjectRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))  return "שם הפרויקט הוא שדה חובה";
        if (req.ProjectNumber <= 0)                return "מספר פרויקט חייב להיות חיובי";
        if (req.ProjectTypeId <= 0)                return "יש לבחור סוג פרויקט";
        if (req.AcademicYearId <= 0)               return "יש לבחור מחזור אקדמי";
        return null;
    }

    // Typed record used as the Dapper parameter bag for INSERT and UPDATE.
    // Must be a named type so callers receive a concrete type, not object.
    private sealed record CatalogParams(
        int     ProjectNumber,
        string  Title,
        string? Description,
        int     AcademicYearId,
        int     ProjectTypeId,
        string? Status,
        string  SourceType,
        string? OrganizationName,
        string? ContactPerson,
        string? ContactRole,
        string? Goals,
        string? TargetAudience,
        string? InternalNotes,
        string? Priority
    );

    private static CatalogParams BuildParams(SaveCatalogProjectRequest req) => new(
        ProjectNumber   : req.ProjectNumber,
        Title           : req.Title.Trim(),
        Description     : Trim(req.Description),
        AcademicYearId  : req.AcademicYearId,
        ProjectTypeId   : req.ProjectTypeId,
        Status          : req.Status,
        SourceType      : string.IsNullOrWhiteSpace(req.SourceType) ? "Manual" : req.SourceType,
        OrganizationName: Trim(req.OrganizationName),
        ContactPerson   : Trim(req.ContactPerson),
        ContactRole     : Trim(req.ContactRole),
        Goals           : Trim(req.Goals),
        TargetAudience  : Trim(req.TargetAudience),
        InternalNotes   : Trim(req.InternalNotes),
        Priority        : string.IsNullOrWhiteSpace(req.Priority) ? null : req.Priority
    );

    private static string? Trim(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task<bool> AcademicYearExistsAsync(int id) =>
        (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM AcademicYears WHERE Id = @Id", new { Id = id }))
        .FirstOrDefault() > 0;

    private async Task<bool> ProjectTypeExistsAsync(int id) =>
        (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM ProjectTypes WHERE Id = @Id", new { Id = id }))
        .FirstOrDefault() > 0;
}
