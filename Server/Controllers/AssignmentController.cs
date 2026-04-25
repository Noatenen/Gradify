using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

[Route("api/assignment")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize]
public class AssignmentController : ControllerBase
{
    private readonly DbRepository _db;

    public AssignmentController(DbRepository db) => _db = db;

    // GET /api/assignment/context
    [HttpGet("context")]
    public async Task<IActionResult> GetContext(int authUserId)
    {
        // Current student info
        var meRow = (await _db.GetRecordsAsync<StudentRow>(
            "SELECT Id, FirstName || ' ' || LastName AS FullName FROM users WHERE Id = @Id",
            new { Id = authUserId }))?.FirstOrDefault();

        // Existing team membership
        const string teamSql = @"
            SELECT t.Id AS TeamId, tm2.UserId,
                   u.FirstName || ' ' || u.LastName AS FullName
            FROM   Teams t
            JOIN   TeamMembers tm  ON tm.TeamId = t.Id AND tm.UserId = @UserId AND tm.IsActive = 1
            JOIN   TeamMembers tm2 ON tm2.TeamId = t.Id AND tm2.IsActive = 1
            JOIN   users u ON u.Id = tm2.UserId";

        var teamRows = (await _db.GetRecordsAsync<TeamRow>(teamSql, new { UserId = authUserId }))?.ToList() ?? new();
        bool hasTeam = teamRows.Count > 0;
        int  teamId  = teamRows.FirstOrDefault()?.TeamId ?? 0;

        // Team members with strengths
        List<TeamMemberBasicDto> teamMembers = new();
        if (hasTeam)
        {
            var strengthRows = (await _db.GetRecordsAsync<StrengthRow>(@"
                SELECT ss.UserId, ss.Strength
                FROM   StudentStrengths ss
                WHERE  ss.UserId IN (
                    SELECT UserId FROM TeamMembers WHERE TeamId = @TeamId AND IsActive = 1
                )", new { TeamId = teamId }))?.ToList() ?? new();

            foreach (var member in teamRows.DistinctBy(r => r.UserId))
            {
                teamMembers.Add(new TeamMemberBasicDto
                {
                    UserId    = member.UserId,
                    FullName  = member.FullName,
                    Strengths = strengthRows
                        .Where(s => s.UserId == member.UserId)
                        .Select(s => s.Strength)
                        .ToList()
                });
            }
        }

        // Students who are not yet in any team (available as partners)
        const string availSql = @"
            SELECT u.Id, u.FirstName || ' ' || u.LastName AS FullName
            FROM   users u
            JOIN   UserRoles ur ON ur.UserId = u.Id AND ur.Role = 'Student'
            WHERE  u.IsActive = 1
              AND  u.Id != @UserId
              AND  NOT EXISTS (
                       SELECT 1 FROM TeamMembers tm
                       WHERE  tm.UserId = u.Id AND tm.IsActive = 1
                   )
            ORDER BY u.FirstName, u.LastName";

        var availRows = (await _db.GetRecordsAsync<StudentRow>(availSql, new { UserId = authUserId }))?.ToList() ?? new();

        // Project catalog — only truly unassigned projects
        const string catalogSql = @"
            SELECT p.Id, p.ProjectNumber, p.Title, pt.Name AS ProjectType,
                   p.Description,
                   'Available' AS Availability
            FROM   Projects p
            JOIN   ProjectTypes pt ON p.ProjectTypeId = pt.Id
            WHERE  p.Status = 'Available'
              AND  NOT EXISTS (
                       SELECT 1 FROM TeamMembers tm
                       WHERE  tm.TeamId = p.TeamId AND tm.IsActive = 1
                   )
            ORDER  BY p.ProjectNumber";

        var catalogRows = (await _db.GetRecordsAsync<CatalogRow>(catalogSql, null))?.ToList() ?? new();

        // Existing submission for the team (if any)
        ExistingAssignmentDto? existing = null;
        if (hasTeam)
        {
            var submRow = (await _db.GetRecordsAsync<SubmissionRow>(@"
                SELECT HasOwnProject, OwnProjectDescription, Notes, SubmittedAt
                FROM   AssignmentFormSubmissions
                WHERE  TeamId = @TeamId LIMIT 1",
                new { TeamId = teamId }))?.FirstOrDefault();

            if (submRow is not null)
            {
                var prefs = (await _db.GetRecordsAsync<PrefRow>(@"
                    SELECT Priority, ProjectId FROM TeamProjectPreferences
                    WHERE  TeamId = @TeamId ORDER BY Priority",
                    new { TeamId = teamId }))?.ToList() ?? new();

                existing = new ExistingAssignmentDto
                {
                    HasOwnProject         = submRow.HasOwnProject,
                    OwnProjectDescription = submRow.OwnProjectDescription ?? "",
                    Notes                 = submRow.Notes ?? "",
                    SubmittedAt           = submRow.SubmittedAt,
                    Preferences           = prefs.Select(p => new ProjectPreferenceDto
                    {
                        Priority  = p.Priority,
                        ProjectId = p.ProjectId
                    }).ToList()
                };
            }
        }

        return Ok(new AssignmentContextDto
        {
            Me                = new StudentBasicDto { Id = meRow?.Id ?? authUserId, FullName = meRow?.FullName ?? "" },
            HasTeam           = hasTeam,
            TeamMembers       = teamMembers,
            AvailableStudents = availRows.Select(r => new StudentBasicDto { Id = r.Id, FullName = r.FullName }).ToList(),
            Catalog           = catalogRows.Select(r => new AssignmentCatalogItemDto
            {
                Id            = r.Id,
                ProjectNumber = r.ProjectNumber,
                Title         = r.Title,
                ProjectType   = r.ProjectType,
                Availability  = r.Availability,
                Description   = r.Description
            }).ToList(),
            ExistingSubmission = existing
        });
    }

    // POST /api/assignment/submit
    [HttpPost("submit")]
    public async Task<IActionResult> Submit(int authUserId, [FromBody] SubmitAssignmentRequest req)
    {
        if (req is null) return BadRequest();

        // Validate max 2 partners
        var partnerIds = req.PartnerIds.Distinct().Where(p => p != authUserId).Take(2).ToList();

        // Find or create team
        var existingTeamRow = (await _db.GetRecordsAsync<TeamIdRow>(
            "SELECT TeamId FROM TeamMembers WHERE UserId = @UserId AND IsActive = 1 LIMIT 1",
            new { UserId = authUserId }))?.FirstOrDefault();

        int teamId;
        List<int> memberIds;

        if (existingTeamRow is not null)
        {
            teamId = existingTeamRow.TeamId;
            memberIds = (await _db.GetRecordsAsync<UserIdRow>(
                "SELECT UserId FROM TeamMembers WHERE TeamId = @TeamId AND IsActive = 1",
                new { TeamId = teamId }))
                ?.Select(r => r.UserId).ToList() ?? new();
        }
        else
        {
            int academicYearId = (await _db.GetRecordsAsync<int>(
                "SELECT Id FROM AcademicYears WHERE IsCurrent = 1 LIMIT 1", null))
                .FirstOrDefault();
            if (academicYearId == 0) academicYearId = 1;

            teamId = await _db.InsertReturnIdAsync(
                "INSERT INTO Teams (TeamName, AcademicYearId) VALUES (@Name, @AcademicYearId)",
                new { Name = $"Team_{authUserId}", AcademicYearId = academicYearId });

            memberIds = new List<int> { authUserId };
            memberIds.AddRange(partnerIds);

            foreach (var uid in memberIds)
                await _db.SaveDataAsync(
                    "INSERT OR IGNORE INTO TeamMembers (TeamId, UserId, IsActive) VALUES (@TeamId, @UserId, 1)",
                    new { TeamId = teamId, UserId = uid });
        }

        // Strengths — replace all for team members
        foreach (var uid in memberIds)
            await _db.SaveDataAsync("DELETE FROM StudentStrengths WHERE UserId = @UserId", new { UserId = uid });

        foreach (var s in req.Strengths.Where(s => !string.IsNullOrWhiteSpace(s.Strength) && memberIds.Contains(s.UserId)))
            await _db.SaveDataAsync(
                "INSERT OR IGNORE INTO StudentStrengths (UserId, Strength) VALUES (@UserId, @Strength)",
                new { s.UserId, s.Strength });

        // Preferences — replace
        await _db.SaveDataAsync("DELETE FROM TeamProjectPreferences WHERE TeamId = @TeamId", new { TeamId = teamId });
        foreach (var pref in req.Preferences.Where(p => p.ProjectId > 0).OrderBy(p => p.Priority))
            await _db.SaveDataAsync(
                "INSERT OR IGNORE INTO TeamProjectPreferences (TeamId, Priority, ProjectId) VALUES (@TeamId, @Priority, @ProjectId)",
                new { TeamId = teamId, pref.Priority, pref.ProjectId });

        // Form submission upsert
        await _db.SaveDataAsync(@"
            INSERT INTO AssignmentFormSubmissions (TeamId, HasOwnProject, OwnProjectDescription, Notes, SubmittedAt)
            VALUES (@TeamId, @HasOwnProject, @OwnProjectDescription, @Notes, datetime('now'))
            ON CONFLICT(TeamId) DO UPDATE SET
                HasOwnProject         = excluded.HasOwnProject,
                OwnProjectDescription = excluded.OwnProjectDescription,
                Notes                 = excluded.Notes,
                SubmittedAt           = excluded.SubmittedAt",
            new
            {
                TeamId                = teamId,
                HasOwnProject         = req.HasOwnProject ? 1 : 0,
                OwnProjectDescription = req.OwnProjectDescription ?? "",
                Notes                 = req.Notes ?? ""
            });

        return Ok(new { teamId });
    }

    // ── Private row types ────────────────────────────────────────────────────

    private sealed class TeamRow       { public int TeamId { get; set; } public int UserId { get; set; } public string FullName { get; set; } = ""; }
    private sealed class StrengthRow   { public int UserId { get; set; } public string Strength { get; set; } = ""; }
    private sealed class StudentRow    { public int Id { get; set; } public string FullName { get; set; } = ""; }
    private sealed class CatalogRow    { public int Id { get; set; } public int ProjectNumber { get; set; } public string Title { get; set; } = ""; public string ProjectType { get; set; } = ""; public string Availability { get; set; } = ""; public string? Description { get; set; } }
    private sealed class SubmissionRow { public bool HasOwnProject { get; set; } public string? OwnProjectDescription { get; set; } public string? Notes { get; set; } public string SubmittedAt { get; set; } = ""; }
    private sealed class PrefRow       { public int Priority { get; set; } public int ProjectId { get; set; } }
    private sealed class TeamIdRow     { public int TeamId { get; set; } }
    private sealed class UserIdRow     { public int UserId { get; set; } }
}
