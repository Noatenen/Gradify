using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  AssignmentManagementController — /api/assignment-management
//
//  Lecturer/Admin side of the assignment-form workflow.
//
//  Invariants (enforced server-side):
//    · A team can hold at most ONE project assignment (Projects.TeamId UNIQUE).
//      Reassigning requires Force=true ("move" action).
//    · A project can hold at most MaxMentorsPerProject mentors (default 2).
//    · A mentor cannot be added twice to the same project.
//    · Draft assignments (Projects.AssignmentIsDraft = 1) stay invisible to
//      students until the lecturer publishes the academic year, which:
//        – sets AssignmentSettings.AssignmentsPublished = 1 for that year
//        – flips AssignmentIsDraft → 0 on every project of the year
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/assignment-management")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin)]
public class AssignmentManagementController : ControllerBase
{
    private const int MaxMentorsPerProject = 2;

    private readonly DbRepository _db;

    public AssignmentManagementController(DbRepository db) => _db = db;

    // ── GET /api/assignment-management/submissions ──────────────────────────
    [HttpGet("submissions")]
    public async Task<IActionResult> GetSubmissions(int authUserId)
    {
        const string headerSql = @"
            SELECT  s.TeamId,
                    COALESCE(NULLIF(t.TeamName, ''), 'צוות ' || t.Id) AS TeamName,
                    t.AcademicYearId,
                    COALESCE(ay.Name, '')                              AS AcademicYear,
                    s.SubmittedAt,
                    s.HasOwnProject,
                    s.OwnProjectDescription,
                    s.Notes
            FROM    AssignmentFormSubmissions s
            JOIN    Teams         t  ON t.Id = s.TeamId
            LEFT JOIN AcademicYears ay ON ay.Id = t.AcademicYearId
            ORDER   BY s.SubmittedAt DESC";

        var headers = (await _db.GetRecordsAsync<HeaderRow>(headerSql))?.ToList()
                      ?? new List<HeaderRow>();

        if (headers.Count == 0)
            return Ok(Array.Empty<AssignmentSubmissionListItemDto>());

        var memberRows   = await LoadMembersAsync();
        var strengthRows = await LoadStrengthsAsync();
        var prefRows     = await LoadPreferencesAsync();

        var strengthsByUser = GroupStrengthsByUser(strengthRows);
        var membersByTeam   = memberRows
            .GroupBy(r => r.TeamId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new AssignmentSubmissionMemberDto
                {
                    UserId    = r.UserId,
                    FullName  = r.FullName,
                    Email     = r.Email,
                    Strengths = strengthsByUser.TryGetValue(r.UserId, out var s) ? s : new()
                }).ToList());

        var prefsByTeam = prefRows
            .GroupBy(r => r.TeamId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(ToSubmissionPreference).ToList());

        var result = headers.Select(h => new AssignmentSubmissionListItemDto
        {
            TeamId                = h.TeamId,
            TeamName              = h.TeamName,
            AcademicYearId        = h.AcademicYearId,
            AcademicYear          = h.AcademicYear,
            SubmittedAt           = h.SubmittedAt,
            HasOwnProject         = h.HasOwnProject,
            OwnProjectDescription = h.OwnProjectDescription,
            Notes                 = h.Notes,
            Members     = membersByTeam.TryGetValue(h.TeamId, out var m) ? m : new(),
            Preferences = prefsByTeam.TryGetValue(h.TeamId, out var p)   ? p : new()
        }).ToList();

        return Ok(result);
    }

    // ── GET /api/assignment-management/matching ─────────────────────────────
    [HttpGet("matching")]
    public async Task<IActionResult> GetMatching(int authUserId)
    {
        var teams    = await LoadSubmittedTeamsAsync();
        var projects = await LoadOpenProjectsAsync();
        if (teams.Count == 0 || projects.Count == 0)
            return Ok(Array.Empty<TeamProjectMatchDto>());

        var memberRows      = await LoadMembersAsync();
        var strengthRows    = await LoadStrengthsAsync();
        var prefRows        = await LoadPreferencesAsync();
        var membersByTeam   = memberRows.GroupBy(r => r.TeamId)
                                        .ToDictionary(g => g.Key, g => g.Select(r => r.UserId).ToList());
        var strengthsByUser = GroupStrengthsByUser(strengthRows);

        var prefIndex = prefRows.ToDictionary(
            r => (r.TeamId, r.ProjectId),
            r => r.Priority);

        var matches = BuildMatches(teams, projects, membersByTeam, strengthsByUser, prefIndex);
        return Ok(matches);
    }

    // ── GET /api/assignment-management/assignment-board ─────────────────────
    [HttpGet("assignment-board")]
    public async Task<IActionResult> GetAssignmentBoard(int authUserId)
    {
        var teams    = await LoadSubmittedTeamsAsync();
        var projects = await LoadOpenProjectsAsync();
        var mentors  = await LoadMentorsAsync();

        var memberRows     = await LoadMembersAsync();
        var strengthRows   = await LoadStrengthsAsync();
        var prefRows       = await LoadPreferencesAsync();
        var demandRows     = await LoadDemandAsync();
        var projectMentors = await LoadProjectMentorsAsync();

        var strengthsByUser  = GroupStrengthsByUser(strengthRows);
        var memberDtosByTeam = memberRows
            .GroupBy(r => r.TeamId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new AssignmentBoardTeamMemberDto
                {
                    UserId    = r.UserId,
                    FullName  = r.FullName,
                    Strengths = strengthsByUser.TryGetValue(r.UserId, out var s) ? s : new()
                }).ToList());

        var memberIdsByTeam = memberRows.GroupBy(r => r.TeamId)
                                        .ToDictionary(g => g.Key, g => g.Select(r => r.UserId).ToList());

        var prefIndex = prefRows.ToDictionary(
            r => (r.TeamId, r.ProjectId),
            r => r.Priority);

        var prefsByTeam = prefRows
            .GroupBy(r => r.TeamId)
            .ToDictionary(g => g.Key,
                          g => g.OrderBy(p => p.Priority).Select(ToSubmissionPreference).ToList());

        var matches = BuildMatches(teams, projects, memberIdsByTeam, strengthsByUser, prefIndex);

        var matchesByProject = matches
            .GroupBy(m => m.ProjectId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.TotalMatchScore).ToList());

        var matchesByTeam = matches
            .GroupBy(m => m.TeamId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.TotalMatchScore).ToList());

        var demandByProject = demandRows.ToDictionary(d => d.ProjectId, d => d.DemandScore);

        var mentorsByProject = projectMentors
            .GroupBy(m => m.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(m => new AssignmentBoardMentorDto { UserId = m.UserId, FullName = m.FullName }).ToList());

        var teamLookup = teams.ToDictionary(t => t.TeamId);

        var assignedTeamIds = projects.Where(p => p.AssignedTeamId.HasValue)
                                      .Select(p => p.AssignedTeamId!.Value)
                                      .ToHashSet();

        var projectDtos = projects.Select(p =>
        {
            var assignedMembers = p.AssignedTeamId.HasValue && memberDtosByTeam.TryGetValue(p.AssignedTeamId.Value, out var am)
                ? am
                : new List<AssignmentBoardTeamMemberDto>();

            mentorsByProject.TryGetValue(p.ProjectId, out var ms);
            var recs = matchesByProject.TryGetValue(p.ProjectId, out var rs)
                ? rs.Take(5).ToList()
                : new List<TeamProjectMatchDto>();

            return new AssignmentBoardProjectDto
            {
                ProjectId        = p.ProjectId,
                ProjectNumber    = p.ProjectNumber,
                ProjectName      = p.Title,
                ProjectType      = p.ProjectType,
                AssignedTeamId   = p.AssignedTeamId,
                AssignedTeamName = p.AssignedTeamId.HasValue && teamLookup.TryGetValue(p.AssignedTeamId.Value, out var t)
                                   ? t.TeamName
                                   : null,
                AssignedMembers  = assignedMembers,
                Mentors          = ms ?? new List<AssignmentBoardMentorDto>(),
                DemandScore      = demandByProject.TryGetValue(p.ProjectId, out var d) ? d : 0,
                IsDraft          = p.AssignmentIsDraft,
                Recommendations  = recs
            };
        }).ToList();

        var unassignedTeamDtos = teams
            .Where(t => !assignedTeamIds.Contains(t.TeamId))
            .Select(t => new AssignmentBoardTeamDto
            {
                TeamId             = t.TeamId,
                TeamName           = t.TeamName,
                AcademicYearId     = t.AcademicYearId,
                Members            = memberDtosByTeam.TryGetValue(t.TeamId, out var m) ? m : new(),
                Preferences        = prefsByTeam.TryGetValue(t.TeamId, out var p) ? p : new(),
                TopRecommendations = matchesByTeam.TryGetValue(t.TeamId, out var rs)
                                     ? rs.Take(3).ToList()
                                     : new()
            })
            .ToList();

        var mentorDtos = mentors
            .Select(m => new AssignmentBoardMentorDto { UserId = m.UserId, FullName = m.FullName })
            .ToList();

        var year         = await GetCurrentAcademicYearAsync();
        var publishState = year is null
            ? null
            : await GetPublishStateAsync(year.Id);

        bool hasDrafts = projectDtos.Any(p => p.IsDraft);

        return Ok(new AssignmentBoardDto
        {
            AcademicYearId       = year?.Id ?? 0,
            AcademicYearName     = year?.Name ?? "",
            AssignmentsPublished = publishState?.AssignmentsPublished ?? false,
            PublishedAt          = publishState?.PublishedAt,
            HasUnpublishedDrafts = hasDrafts,
            MaxMentorsPerProject = MaxMentorsPerProject,
            Projects             = projectDtos,
            UnassignedTeams      = unassignedTeamDtos,
            Mentors              = mentorDtos
        });
    }

    // ── POST /api/assignment-management/assign-team ─────────────────────────
    // Without Force: refuses with 409 if the team is already assigned to a
    // different project. The UI uses the conflict body to confirm a "move".
    // With Force = true: clears the prior assignment and writes the new one.
    [HttpPost("assign-team")]
    public async Task<IActionResult> AssignTeam(int authUserId, [FromBody] AssignTeamRequest req)
    {
        if (req is null || req.TeamId <= 0 || req.ProjectId <= 0)
            return BadRequest("נתונים חסרים");

        if (!await ExistsAsync("SELECT 1 FROM Projects WHERE Id = @Id", new { Id = req.ProjectId }))
            return NotFound("הפרויקט לא נמצא");

        if (!await ExistsAsync("SELECT 1 FROM Teams WHERE Id = @Id", new { Id = req.TeamId }))
            return NotFound("הצוות לא נמצא");

        var existingProject = (await _db.GetRecordsAsync<ConflictRow>(@"
            SELECT  Id, Title
            FROM    Projects
            WHERE   TeamId = @TeamId AND Id != @ProjectId
            LIMIT   1", new { req.TeamId, req.ProjectId }))?.FirstOrDefault();

        if (existingProject is not null && !req.Force)
        {
            return Conflict(new AssignTeamConflictDto
            {
                Message     = "team-already-assigned",
                ProjectId   = existingProject.Id,
                ProjectName = existingProject.Title
            });
        }

        // Clear team's prior project (covers move case).
        await _db.SaveDataAsync(@"
            UPDATE Projects
            SET    TeamId            = NULL,
                   AssignmentIsDraft = 0,
                   UpdatedAt         = datetime('now')
            WHERE  TeamId = @TeamId AND Id != @ProjectId",
            new { req.TeamId, req.ProjectId });

        // Clear the target project's prior team (only if it's a different team).
        await _db.SaveDataAsync(@"
            UPDATE Projects
            SET    TeamId            = NULL,
                   AssignmentIsDraft = 0,
                   UpdatedAt         = datetime('now')
            WHERE  Id = @ProjectId AND TeamId IS NOT NULL AND TeamId != @TeamId",
            new { req.TeamId, req.ProjectId });

        // Set new draft assignment.
        await _db.SaveDataAsync(@"
            UPDATE Projects
            SET    TeamId            = @TeamId,
                   AssignmentIsDraft = 1,
                   UpdatedAt         = datetime('now')
            WHERE  Id = @ProjectId",
            new { req.TeamId, req.ProjectId });

        return Ok();
    }

    // ── POST /api/assignment-management/unassign-team ───────────────────────
    [HttpPost("unassign-team")]
    public async Task<IActionResult> UnassignTeam(int authUserId, [FromBody] UnassignTeamRequest req)
    {
        if (req is null || req.ProjectId <= 0)
            return BadRequest("נתונים חסרים");

        int affected = await _db.SaveDataAsync(@"
            UPDATE Projects
            SET    TeamId            = NULL,
                   AssignmentIsDraft = 0,
                   UpdatedAt         = datetime('now')
            WHERE  Id = @ProjectId",
            new { req.ProjectId });

        if (affected == 0) return NotFound("הפרויקט לא נמצא");
        return Ok();
    }

    // ── POST /api/assignment-management/assign-mentor ───────────────────────
    // Adds a mentor to the project. Validates: project exists, user is an
    // active Mentor, not already assigned, count < MaxMentorsPerProject.
    [HttpPost("assign-mentor")]
    public async Task<IActionResult> AssignMentor(int authUserId, [FromBody] AssignMentorRequest req)
    {
        if (req is null || req.ProjectId <= 0 || req.MentorId <= 0)
            return BadRequest("נתונים חסרים");

        if (!await ExistsAsync("SELECT 1 FROM Projects WHERE Id = @Id", new { Id = req.ProjectId }))
            return NotFound("הפרויקט לא נמצא");

        bool isMentor = await ExistsAsync(@"
            SELECT 1 FROM users u
            JOIN   UserRoles ur ON ur.UserId = u.Id
            WHERE  u.Id = @Id AND ur.Role = 'Mentor' AND u.IsActive = 1",
            new { Id = req.MentorId });

        if (!isMentor) return BadRequest("המשתמש שנבחר אינו מנטור פעיל");

        bool already = await ExistsAsync(
            "SELECT 1 FROM ProjectMentors WHERE ProjectId = @ProjectId AND UserId = @UserId",
            new { req.ProjectId, UserId = req.MentorId });

        if (already) return Conflict("המנטור כבר משוייך לפרויקט");

        int currentCount = (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM ProjectMentors WHERE ProjectId = @ProjectId",
            new { req.ProjectId })).FirstOrDefault();

        if (currentCount >= MaxMentorsPerProject)
            return Conflict($"מספר המנטורים המקסימלי לפרויקט הוא {MaxMentorsPerProject}");

        await _db.SaveDataAsync(
            "INSERT INTO ProjectMentors (ProjectId, UserId) VALUES (@ProjectId, @UserId)",
            new { req.ProjectId, UserId = req.MentorId });

        await _db.SaveDataAsync(
            "UPDATE Projects SET UpdatedAt = datetime('now') WHERE Id = @Id",
            new { Id = req.ProjectId });

        return Ok();
    }

    // ── POST /api/assignment-management/remove-mentor ───────────────────────
    [HttpPost("remove-mentor")]
    public async Task<IActionResult> RemoveMentor(int authUserId, [FromBody] RemoveMentorRequest req)
    {
        if (req is null || req.ProjectId <= 0 || req.MentorId <= 0)
            return BadRequest("נתונים חסרים");

        int affected = await _db.SaveDataAsync(
            "DELETE FROM ProjectMentors WHERE ProjectId = @ProjectId AND UserId = @UserId",
            new { req.ProjectId, UserId = req.MentorId });

        if (affected == 0) return NotFound("המנטור לא נמצא בפרויקט");

        await _db.SaveDataAsync(
            "UPDATE Projects SET UpdatedAt = datetime('now') WHERE Id = @Id",
            new { Id = req.ProjectId });

        return Ok();
    }

    // ── POST /api/assignment-management/publish ─────────────────────────────
    // Publishes all draft assignments for an academic year (default = current).
    // Idempotent — re-publishing flips any new drafts that were created since.
    [HttpPost("publish")]
    public async Task<IActionResult> Publish(int authUserId, [FromBody] PublishAssignmentsRequest? req)
    {
        int yearId = req?.AcademicYearId ?? 0;
        if (yearId <= 0)
        {
            var current = await GetCurrentAcademicYearAsync();
            if (current is null) return BadRequest("לא נמצא מחזור אקדמי פעיל");
            yearId = current.Id;
        }

        if (!await ExistsAsync("SELECT 1 FROM AcademicYears WHERE Id = @Id", new { Id = yearId }))
            return NotFound("המחזור האקדמי לא נמצא");

        await _db.SaveDataAsync(@"
            UPDATE Projects
            SET    AssignmentIsDraft = 0,
                   UpdatedAt         = datetime('now')
            WHERE  AcademicYearId    = @YearId
              AND  AssignmentIsDraft = 1",
            new { YearId = yearId });

        await _db.SaveDataAsync(@"
            INSERT INTO AssignmentSettings (AcademicYearId, AssignmentsPublished, PublishedAt, UpdatedAt)
            VALUES (@YearId, 1, datetime('now'), datetime('now'))
            ON CONFLICT(AcademicYearId) DO UPDATE SET
                AssignmentsPublished = 1,
                PublishedAt          = datetime('now'),
                UpdatedAt            = datetime('now')",
            new { YearId = yearId });

        return Ok();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Internal data-loading helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<List<TeamRow>> LoadSubmittedTeamsAsync()
    {
        const string sql = @"
            SELECT  t.Id              AS TeamId,
                    COALESCE(NULLIF(t.TeamName, ''), 'צוות ' || t.Id) AS TeamName,
                    t.AcademicYearId
            FROM    Teams t
            WHERE   EXISTS (SELECT 1 FROM AssignmentFormSubmissions s WHERE s.TeamId = t.Id)
            ORDER   BY t.Id";

        return (await _db.GetRecordsAsync<TeamRow>(sql))?.ToList() ?? new();
    }

    private async Task<List<ProjectRow>> LoadOpenProjectsAsync()
    {
        const string sql = @"
            SELECT  p.Id           AS ProjectId,
                    p.ProjectNumber,
                    p.Title,
                    pt.Name        AS ProjectType,
                    p.TeamId       AS AssignedTeamId,
                    p.AssignmentIsDraft
            FROM    Projects     p
            JOIN    ProjectTypes pt ON pt.Id = p.ProjectTypeId
            JOIN    AcademicYears ay ON ay.Id = p.AcademicYearId
            WHERE   (ay.IsCurrent = 1 OR ay.IsActive = 1)
              AND   (p.Status = 'Available' OR p.TeamId IS NOT NULL)
            ORDER   BY p.ProjectNumber";

        return (await _db.GetRecordsAsync<ProjectRow>(sql))?.ToList() ?? new();
    }

    private async Task<List<MemberRow>> LoadMembersAsync()
    {
        const string sql = @"
            SELECT  tm.TeamId,
                    u.Id           AS UserId,
                    u.FirstName || ' ' || u.LastName AS FullName,
                    COALESCE(u.Email, '')            AS Email
            FROM    TeamMembers tm
            JOIN    users       u  ON u.Id = tm.UserId
            WHERE   tm.IsActive = 1
              AND   tm.TeamId IN (SELECT TeamId FROM AssignmentFormSubmissions)
            ORDER   BY tm.TeamId, u.FirstName, u.LastName";

        return (await _db.GetRecordsAsync<MemberRow>(sql))?.ToList() ?? new();
    }

    private async Task<List<StrengthRow>> LoadStrengthsAsync()
    {
        const string sql = @"
            SELECT  ss.UserId, ss.Strength
            FROM    StudentStrengths ss
            WHERE   ss.UserId IN (
                        SELECT tm.UserId
                        FROM   TeamMembers tm
                        WHERE  tm.IsActive = 1
                          AND  tm.TeamId IN (SELECT TeamId FROM AssignmentFormSubmissions)
                    )";
        return (await _db.GetRecordsAsync<StrengthRow>(sql))?.ToList() ?? new();
    }

    private async Task<List<PrefRow>> LoadPreferencesAsync()
    {
        const string sql = @"
            SELECT  pp.TeamId,
                    pp.Priority,
                    pp.ProjectId,
                    p.ProjectNumber,
                    p.Title    AS ProjectTitle,
                    COALESCE(pt.Name, '') AS ProjectType
            FROM    TeamProjectPreferences pp
            JOIN    Projects     p  ON p.Id = pp.ProjectId
            LEFT JOIN ProjectTypes pt ON pt.Id = p.ProjectTypeId
            WHERE   pp.TeamId IN (SELECT TeamId FROM AssignmentFormSubmissions)
            ORDER   BY pp.TeamId, pp.Priority";

        return (await _db.GetRecordsAsync<PrefRow>(sql))?.ToList() ?? new();
    }

    private async Task<List<MentorRow>> LoadMentorsAsync()
    {
        const string sql = @"
            SELECT  u.Id           AS UserId,
                    u.FirstName || ' ' || u.LastName AS FullName
            FROM    users u
            JOIN    UserRoles ur ON ur.UserId = u.Id
            WHERE   ur.Role = 'Mentor' AND u.IsActive = 1
            ORDER   BY u.FirstName, u.LastName";

        return (await _db.GetRecordsAsync<MentorRow>(sql))?.ToList() ?? new();
    }

    private async Task<List<DemandRow>> LoadDemandAsync()
    {
        const string sql = @"
            SELECT  pp.ProjectId,
                    SUM(CASE pp.Priority
                            WHEN 1 THEN 30
                            WHEN 2 THEN 20
                            WHEN 3 THEN 10
                            ELSE 0
                        END) AS DemandScore
            FROM    TeamProjectPreferences pp
            GROUP   BY pp.ProjectId";

        return (await _db.GetRecordsAsync<DemandRow>(sql))?.ToList() ?? new();
    }

    private async Task<List<ProjectMentorRow>> LoadProjectMentorsAsync()
    {
        const string sql = @"
            SELECT  pm.ProjectId,
                    u.Id           AS UserId,
                    u.FirstName || ' ' || u.LastName AS FullName
            FROM    ProjectMentors pm
            JOIN    users u ON u.Id = pm.UserId
            ORDER   BY pm.ProjectId, u.FirstName, u.LastName";

        return (await _db.GetRecordsAsync<ProjectMentorRow>(sql))?.ToList() ?? new();
    }

    private async Task<AcademicYearRow?> GetCurrentAcademicYearAsync()
    {
        const string sql = @"
            SELECT  Id, Name
            FROM    AcademicYears
            WHERE   IsCurrent = 1
            ORDER   BY Id DESC
            LIMIT   1";
        return (await _db.GetRecordsAsync<AcademicYearRow>(sql))?.FirstOrDefault();
    }

    private async Task<PublishStateRow?> GetPublishStateAsync(int academicYearId)
    {
        const string sql = @"
            SELECT  AssignmentsPublished, PublishedAt
            FROM    AssignmentSettings
            WHERE   AcademicYearId = @Id
            LIMIT   1";
        return (await _db.GetRecordsAsync<PublishStateRow>(sql, new { Id = academicYearId }))?.FirstOrDefault();
    }

    private async Task<bool> ExistsAsync(string sql, object parameters)
    {
        var rows = await _db.GetRecordsAsync<int>(sql, parameters);
        return rows is not null && rows.Any();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Matching algorithm
    // ─────────────────────────────────────────────────────────────────────────

    private static List<TeamProjectMatchDto> BuildMatches(
        List<TeamRow>                                teams,
        List<ProjectRow>                             projects,
        Dictionary<int, List<int>>                   memberIdsByTeam,
        Dictionary<int, List<string>>                strengthsByUser,
        Dictionary<(int teamId, int projectId), int> prefIndex)
    {
        var result = new List<TeamProjectMatchDto>(teams.Count * projects.Count);

        foreach (var team in teams)
        {
            var teamStrengths = (memberIdsByTeam.TryGetValue(team.TeamId, out var members)
                ? members
                : new List<int>())
                .SelectMany(uid => strengthsByUser.TryGetValue(uid, out var s) ? s : new())
                .ToList();

            foreach (var project in projects)
            {
                int? rank = prefIndex.TryGetValue((team.TeamId, project.ProjectId), out var r) ? r : (int?)null;

                int prefScore = rank switch
                {
                    1 => 30,
                    2 => 20,
                    3 => 10,
                    _ => 0
                };

                int skillScore = teamStrengths.Sum(s => SkillWeight(project.ProjectType, s));
                int total      = prefScore + skillScore;

                result.Add(new TeamProjectMatchDto
                {
                    TeamId              = team.TeamId,
                    TeamName            = team.TeamName,
                    ProjectId           = project.ProjectId,
                    ProjectName         = project.Title,
                    ProjectType         = project.ProjectType,
                    PreferenceRank      = rank,
                    PreferenceScore     = prefScore,
                    SkillScore          = skillScore,
                    TotalMatchScore     = total,
                    RecommendationLabel = LabelFor(total)
                });
            }
        }

        return result;
    }

    private static int SkillWeight(string projectType, string strength)
    {
        if (string.Equals(projectType, "Technological", StringComparison.OrdinalIgnoreCase))
        {
            return strength switch
            {
                "Technology"        => 15,
                "Design"            => 8,
                "ProjectManagement" => 5,
                "Content"           => 3,
                _                   => 0
            };
        }

        if (string.Equals(projectType, "Methodological", StringComparison.OrdinalIgnoreCase))
        {
            return strength switch
            {
                "Content"           => 15,
                "Design"            => 8,
                "ProjectManagement" => 5,
                "Technology"        => 3,
                _                   => 0
            };
        }

        return 0;
    }

    private static string LabelFor(int total) =>
        total >= 40 ? "התאמה גבוהה" :
        total >= 25 ? "התאמה בינונית" :
                      "התאמה נמוכה";

    private static Dictionary<int, List<string>> GroupStrengthsByUser(IEnumerable<StrengthRow> rows) =>
        rows.GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Strength).ToList());

    private static AssignmentSubmissionPreferenceDto ToSubmissionPreference(PrefRow r) => new()
    {
        Priority      = r.Priority,
        ProjectId     = r.ProjectId,
        ProjectNumber = r.ProjectNumber,
        ProjectTitle  = r.ProjectTitle,
        ProjectType   = r.ProjectType
    };

    // ── Private row types ────────────────────────────────────────────────────

    private sealed class HeaderRow
    {
        public int     TeamId                { get; set; }
        public string  TeamName              { get; set; } = "";
        public int     AcademicYearId        { get; set; }
        public string  AcademicYear          { get; set; } = "";
        public string  SubmittedAt           { get; set; } = "";
        public bool    HasOwnProject         { get; set; }
        public string? OwnProjectDescription { get; set; }
        public string? Notes                 { get; set; }
    }

    private sealed class MemberRow
    {
        public int    TeamId   { get; set; }
        public int    UserId   { get; set; }
        public string FullName { get; set; } = "";
        public string Email    { get; set; } = "";
    }

    private sealed class StrengthRow
    {
        public int    UserId   { get; set; }
        public string Strength { get; set; } = "";
    }

    private sealed class PrefRow
    {
        public int    TeamId        { get; set; }
        public int    Priority      { get; set; }
        public int    ProjectId     { get; set; }
        public int    ProjectNumber { get; set; }
        public string ProjectTitle  { get; set; } = "";
        public string ProjectType   { get; set; } = "";
    }

    private sealed class TeamRow
    {
        public int    TeamId         { get; set; }
        public string TeamName       { get; set; } = "";
        public int    AcademicYearId { get; set; }
    }

    private sealed class ProjectRow
    {
        public int    ProjectId         { get; set; }
        public int    ProjectNumber     { get; set; }
        public string Title             { get; set; } = "";
        public string ProjectType       { get; set; } = "";
        public int?   AssignedTeamId    { get; set; }
        public bool   AssignmentIsDraft { get; set; }
    }

    private sealed class MentorRow
    {
        public int    UserId   { get; set; }
        public string FullName { get; set; } = "";
    }

    private sealed class DemandRow
    {
        public int ProjectId   { get; set; }
        public int DemandScore { get; set; }
    }

    private sealed class ProjectMentorRow
    {
        public int    ProjectId { get; set; }
        public int    UserId    { get; set; }
        public string FullName  { get; set; } = "";
    }

    private sealed class ConflictRow
    {
        public int    Id    { get; set; }
        public string Title { get; set; } = "";
    }

    private sealed class AcademicYearRow
    {
        public int    Id   { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class PublishStateRow
    {
        public bool    AssignmentsPublished { get; set; }
        public string? PublishedAt          { get; set; }
    }
}
