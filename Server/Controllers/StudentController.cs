using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static AuthWithAdmin.Server.Controllers.ResourceFilesController;
using System.Collections.Generic;

namespace AuthWithAdmin.Server.Controllers;

/// <summary>
/// Endpoints for the student assignment flow.
/// All routes require authentication (any role).
/// </summary>
[Route("api/student")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize]
public class StudentController : ControllerBase
{
    private readonly DbRepository _db;

    public StudentController(DbRepository db) => _db = db;

    // GET /api/student/catalog
    // Returns projects that are open for assignment (Status = 'Available').
    // Internal notes are excluded — this is a student-facing view.
    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog(int authUserId)
    {
        const string sql = @"
            SELECT  p.Id,
                    p.ProjectNumber,
                    p.Title,
                    p.Description,
                    pt.Name  AS ProjectType,
                    COALESCE(GROUP_CONCAT(u.FirstName || ' ' || u.LastName, ', '), '') AS Mentors,
                    CASE
                        WHEN (SELECT COUNT(1) FROM Teams t2
                              WHERE t2.Id = p.TeamId) > 0
                        THEN 'Taken'
                        ELSE 'Available'
                    END AS Availability
            FROM    Projects p
            JOIN    ProjectTypes pt ON p.ProjectTypeId = pt.Id
            LEFT JOIN ProjectMentors pm ON pm.ProjectId = p.Id
            LEFT JOIN Users u ON u.Id = pm.UserId
            WHERE   p.Status = 'Available'
            GROUP BY p.Id, p.ProjectNumber, p.Title, p.Description, pt.Name
            ORDER   BY p.ProjectNumber";

        var rows = await _db.GetRecordsAsync<StudentCatalogProjectDto>(sql, new { });

        return Ok(rows?.ToList() ?? new List<StudentCatalogProjectDto>());
    }

    // GET /api/student/resources
    // Returns all resource items (files and videos) accessible to any authenticated user.
    // Reuses the same SQL and mapping logic as the admin ResourceFilesController.
    [HttpGet("resources")]
    public async Task<IActionResult> GetResources(int authUserId)
    {
        var rows = await FetchAllRowsAsync(_db);
        return Ok(ResourceFilesController.MapRows(rows));
    }

    // GET /api/student/learning-materials
    // Returns learning materials relevant to the requesting student:
    //   1. Materials targeted to the student's specific project (ProjectId match)
    //   2. Materials targeted to the student's project type (ProjectType match, no ProjectId)
    //   3. Global materials (no ProjectId, no ProjectType)
    // Ordered newest first.
    [HttpGet("learning-materials")]
    public async Task<IActionResult> GetLearningMaterials(int authUserId)
    {
        // Resolve the student's current project context
        const string contextSql = @"
            SELECT  p.Id   AS ProjectId,
                    pt.Name AS ProjectType
            FROM    Projects p
            JOIN    Teams       t  ON p.TeamId        = t.Id
            JOIN    TeamMembers tm ON t.Id             = tm.TeamId
            JOIN    ProjectTypes pt ON p.ProjectTypeId = pt.Id
            WHERE   tm.UserId = @UserId
              AND   tm.IsActive = 1
            LIMIT 1";

        var context = (await _db.GetRecordsAsync<StudentProjectContext>(
            contextSql, new { UserId = authUserId }))?.FirstOrDefault();

        // A student who is not yet assigned to a project sees only global materials.
        int    projectId   = context?.ProjectId   ?? 0;
        string projectType = context?.ProjectType ?? "";

        const string materialsSql = @"
            SELECT  Id, Title, Description, MaterialType, Url, FileName,
                    ProjectId, ProjectType, CreatedAt
            FROM    LearningMaterials
            WHERE   ProjectId = @ProjectId
               OR   (ProjectId IS NULL AND ProjectType = @ProjectType AND @ProjectType != '')
               OR   (ProjectId IS NULL AND ProjectType IS NULL)
            ORDER BY CreatedAt DESC";

        var rows = await _db.GetRecordsAsync<LearningMaterialDto>(
            materialsSql, new { ProjectId = projectId, ProjectType = projectType });

        return Ok(rows?.ToList() ?? new List<LearningMaterialDto>());
    }

    // ── Private row type for project-context query ────────────────────────────

    private sealed class StudentProjectContext
    {
        public int    ProjectId   { get; set; }
        public string ProjectType { get; set; } = "";
    }
}
