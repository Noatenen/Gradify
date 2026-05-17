using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  ProjectMentorsController — /api/projects/{projectId}/mentors
//
//  Admin/Staff surface for ADDING additional mentors to an already-existing
//  project, including users who hold only the Admin or Staff role.
//
//  Why a separate controller (and not the existing
//  /api/assignment-management/assign-mentor)?
//    • That endpoint is part of the structured assignment workflow:
//        - Admin role only
//        - Rejects non-Mentor candidates
//        - Caps at 2 mentors per project
//      Changing it would alter behaviour for its existing callers. The new
//      surface here is intentionally more permissive (Admin OR Staff caller,
//      Admin/Staff/Mentor target, idempotent on duplicates, no hard cap),
//      and the existing endpoint stays untouched.
//
//  Idempotency:
//    PK on ProjectMentors is UNIQUE(ProjectId, UserId). We pre-check before
//    inserting and treat "already assigned" as 200 OK with a `wasAlreadyAssigned`
//    flag — the UI just refreshes without showing an error.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/projects/{projectId:int}/mentors")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = $"{Roles.Admin},{Roles.Staff}")]
public class ProjectMentorsController : ControllerBase
{
    private readonly DbRepository _db;

    public ProjectMentorsController(DbRepository db) => _db = db;

    // ── GET /api/projects/{projectId}/mentors ───────────────────────────────
    [HttpGet]
    public async Task<IActionResult> List(int projectId, int authUserId)
    {
        if (!await ProjectExistsAsync(projectId))
            return NotFound("הפרויקט לא נמצא");

        const string sql = @"
            SELECT  u.Id                                              AS UserId,
                    TRIM(COALESCE(u.FirstName,'') || ' ' || COALESCE(u.LastName,'')) AS FullName,
                    COALESCE(u.Email, '')                             AS Email,
                    COALESCE(u.Phone, '')                             AS Phone,
                    COALESCE(
                        (SELECT GROUP_CONCAT(ur.Role, ', ')
                         FROM   UserRoles ur
                         WHERE  ur.UserId = u.Id),
                        '')                                           AS Roles,
                    pm.AssignedAt                                     AS AssignedAt
            FROM    ProjectMentors pm
            JOIN    users u ON u.Id = pm.UserId
            WHERE   pm.ProjectId = @ProjectId
            ORDER   BY pm.AssignedAt ASC, u.LastName, u.FirstName";

        var rows = (await _db.GetRecordsAsync<ProjectMentorDto>(
            sql, new { ProjectId = projectId }))?.ToList() ?? new();

        return Ok(rows);
    }

    // ── GET /api/projects/{projectId}/mentors/candidates?q=&limit= ──────────
    // Returns active users who can act as mentor (Mentor / Admin / Staff
    // roles) and aren't already assigned to this project. The search filter
    // matches name OR email (case-insensitive, contains). Capped at 30 rows.
    [HttpGet("candidates")]
    public async Task<IActionResult> Candidates(int projectId, int authUserId,
        [FromQuery] string? q = null,
        [FromQuery] int limit = 30)
    {
        if (!await ProjectExistsAsync(projectId))
            return NotFound("הפרויקט לא נמצא");

        if (limit <= 0 || limit > 100) limit = 30;
        var qTrim = (q ?? "").Trim();
        bool hasQuery = qTrim.Length > 0;

        const string sql = @"
            SELECT  u.Id                                              AS UserId,
                    TRIM(COALESCE(u.FirstName,'') || ' ' || COALESCE(u.LastName,'')) AS FullName,
                    COALESCE(u.Email, '')                             AS Email,
                    (SELECT GROUP_CONCAT(ur2.Role, ', ')
                     FROM   UserRoles ur2
                     WHERE  ur2.UserId = u.Id)                        AS Roles
            FROM    users u
            WHERE   u.IsActive = 1
              AND   EXISTS (
                    SELECT 1 FROM UserRoles ur
                    WHERE  ur.UserId = u.Id
                      AND  ur.Role IN ('Mentor','Admin','Staff')
              )
              AND   NOT EXISTS (
                    SELECT 1 FROM ProjectMentors pm
                    WHERE  pm.ProjectId = @ProjectId AND pm.UserId = u.Id
              )
              AND   (@HasQ = 0
                    OR LOWER(u.FirstName || ' ' || u.LastName) LIKE @Like
                    OR LOWER(COALESCE(u.Email, ''))            LIKE @Like)
            ORDER BY u.LastName, u.FirstName
            LIMIT  @Limit";

        var rows = (await _db.GetRecordsAsync<MentorCandidateDto>(sql, new
        {
            ProjectId = projectId,
            HasQ      = hasQuery ? 1 : 0,
            Like      = "%" + qTrim.ToLowerInvariant() + "%",
            Limit     = limit,
        }))?.ToList() ?? new();

        return Ok(rows);
    }

    // ── POST /api/projects/{projectId}/mentors ──────────────────────────────
    // Adds a user as an additional mentor for the project. Allowed candidates
    // are active users with at least one of: Mentor, Admin, Staff. Idempotent.
    [HttpPost]
    public async Task<IActionResult> Add(int projectId, int authUserId,
        [FromBody] AddProjectMentorRequest req)
    {
        if (req is null || req.UserId <= 0) return BadRequest("נתונים חסרים");

        if (!await ProjectExistsAsync(projectId))
            return NotFound("הפרויקט לא נמצא");

        // Candidate must be an active user with at least one allowed role.
        bool canMentor = (await _db.GetRecordsAsync<int>(@"
            SELECT 1
            FROM   users u
            WHERE  u.Id = @Id
              AND  u.IsActive = 1
              AND  EXISTS (
                    SELECT 1 FROM UserRoles ur
                    WHERE  ur.UserId = u.Id
                      AND  ur.Role IN ('Mentor','Admin','Staff')
              )
            LIMIT 1",
            new { Id = req.UserId }))?.Any() ?? false;

        if (!canMentor) return BadRequest("המשתמש שנבחר אינו יכול לשמש כמנחה");

        bool already = (await _db.GetRecordsAsync<int>(
            "SELECT 1 FROM ProjectMentors WHERE ProjectId = @P AND UserId = @U",
            new { P = projectId, U = req.UserId }))?.Any() ?? false;

        if (!already)
        {
            // Defence in depth — also covered by the UNIQUE(ProjectId, UserId)
            // constraint, but pre-checking lets us return a clean payload
            // shape without catching a SQLite exception.
            await _db.SaveDataAsync(
                "INSERT INTO ProjectMentors (ProjectId, UserId) VALUES (@P, @U)",
                new { P = projectId, U = req.UserId });

            await _db.SaveDataAsync(
                "UPDATE Projects SET UpdatedAt = datetime('now') WHERE Id = @Id",
                new { Id = projectId });
        }

        return Ok(new { wasAlreadyAssigned = already });
    }

    // ── DELETE /api/projects/{projectId}/mentors/{userId} ───────────────────
    // Bonus: lets admins remove an additional mentor. Not required by the
    // current task but mirrors the assignment-management remove flow and is
    // a natural pair for the "add" surface.
    [HttpDelete("{userId:int}")]
    public async Task<IActionResult> Remove(int projectId, int userId, int authUserId)
    {
        if (!await ProjectExistsAsync(projectId))
            return NotFound("הפרויקט לא נמצא");

        int affected = await _db.SaveDataAsync(
            "DELETE FROM ProjectMentors WHERE ProjectId = @P AND UserId = @U",
            new { P = projectId, U = userId });

        if (affected == 0) return NotFound("המנחה אינו משוייך לפרויקט");

        await _db.SaveDataAsync(
            "UPDATE Projects SET UpdatedAt = datetime('now') WHERE Id = @Id",
            new { Id = projectId });

        return Ok();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<bool> ProjectExistsAsync(int projectId) =>
        (await _db.GetRecordsAsync<int>(
            "SELECT 1 FROM Projects WHERE Id = @Id LIMIT 1",
            new { Id = projectId }))?.Any() ?? false;
}