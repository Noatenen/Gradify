using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static AuthWithAdmin.Server.Controllers.ResourceFilesController;

namespace AuthWithAdmin.Server.Controllers;

[Route("api/student")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Student)]
public class StudentController : ControllerBase
{
    private readonly DbRepository _db;
    private readonly FilesManage  _files;

    private static readonly HashSet<string> AllowedImageExts =
        new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp" };

    public StudentController(DbRepository db, FilesManage files)
    {
        _db    = db;
        _files = files;
    }

    // GET /api/student/catalog
    // Includes IsFavorite flag for the requesting student so the UI can render
    // the star without a second round-trip.
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
                    CASE WHEN EXISTS (
                        SELECT 1 FROM TeamMembers tm
                        WHERE  tm.TeamId = p.TeamId AND tm.IsActive = 1
                    ) THEN 'Taken' ELSE 'Available' END AS Availability,
                    CASE WHEN EXISTS (
                        SELECT 1 FROM StudentProjectFavorites f
                        WHERE  f.UserId = @UserId AND f.ProjectId = p.Id
                    ) THEN 1 ELSE 0 END AS IsFavorite,
                    p.OrganizationName,
                    p.OrganizationType,
                    p.ContactPerson,
                    p.ContactRole,
                    p.ContactEmail,
                    p.ContactPhone,
                    p.Goals,
                    p.TargetAudience,
                    p.ProjectTopic,
                    p.Contents
            FROM    Projects p
            JOIN    ProjectTypes pt ON p.ProjectTypeId = pt.Id
            LEFT JOIN ProjectMentors pm ON pm.ProjectId = p.Id
            LEFT JOIN Users u ON u.Id = pm.UserId
            WHERE   p.Status = 'Available'
            GROUP BY p.Id, p.ProjectNumber, p.Title, p.Description, pt.Name
            ORDER   BY p.ProjectNumber";

        var rows = await _db.GetRecordsAsync<StudentCatalogProjectDto>(sql, new { UserId = authUserId });
        return Ok(rows?.ToList() ?? new List<StudentCatalogProjectDto>());
    }

    // GET /api/student/favorites — list of favorited project IDs for the current student
    [HttpGet("favorites")]
    public async Task<IActionResult> GetFavorites(int authUserId)
    {
        var ids = await _db.GetRecordsAsync<int>(
            "SELECT ProjectId FROM StudentProjectFavorites WHERE UserId = @UserId ORDER BY CreatedAt DESC",
            new { UserId = authUserId });
        return Ok(ids?.ToList() ?? new List<int>());
    }

    // POST /api/student/favorites/{projectId} — bookmark a project
    [HttpPost("favorites/{projectId:int}")]
    public async Task<IActionResult> AddFavorite(int projectId, int authUserId)
    {
        var exists = (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM Projects WHERE Id = @Id", new { Id = projectId })).FirstOrDefault();
        if (exists == 0) return NotFound("הפרויקט לא נמצא");

        await _db.SaveDataAsync(
            "INSERT OR IGNORE INTO StudentProjectFavorites (UserId, ProjectId) VALUES (@UserId, @ProjectId)",
            new { UserId = authUserId, ProjectId = projectId });
        return Ok();
    }

    // DELETE /api/student/favorites/{projectId} — remove bookmark
    [HttpDelete("favorites/{projectId:int}")]
    public async Task<IActionResult> RemoveFavorite(int projectId, int authUserId)
    {
        await _db.SaveDataAsync(
            "DELETE FROM StudentProjectFavorites WHERE UserId = @UserId AND ProjectId = @ProjectId",
            new { UserId = authUserId, ProjectId = projectId });
        return Ok();
    }

    // GET /api/student/resources
    [HttpGet("resources")]
    public async Task<IActionResult> GetResources(int authUserId)
    {
        var rows = await FetchAllRowsAsync(_db);
        return Ok(ResourceFilesController.MapRows(rows));
    }

    // GET /api/student/learning-materials
    [HttpGet("learning-materials")]
    public async Task<IActionResult> GetLearningMaterials(int authUserId)
    {
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

    // ── Profile ───────────────────────────────────────────────────────────────

    // GET /api/student/me
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile(int authUserId)
    {
        const string profileSql = @"
            SELECT  u.Id,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    u.Phone,
                    u.AcademicYear,
                    u.IdNumber,
                    u.ProfileImagePath,
                    p.Title        AS ProjectTitle,
                    p.ProjectNumber,
                    t.TeamName
            FROM    users u
            LEFT JOIN TeamMembers tm ON tm.UserId = u.Id AND tm.IsActive = 1
            LEFT JOIN Teams       t  ON t.Id      = tm.TeamId
            LEFT JOIN Projects    p  ON p.TeamId  = t.Id
            WHERE   u.Id = @UserId
            LIMIT   1";

        var row = (await _db.GetRecordsAsync<StudentProfileRow>(
            profileSql, new { UserId = authUserId }))?.FirstOrDefault();

        if (row is null) return NotFound();

        const string prefsSql = @"
            SELECT  NotifyOnTasks, NotifyOnDeadlines, NotifyOnFeedback,
                    NotifyOnSubmissions, NotifyOnMentorUpdates,
                    GoogleCalendarConnected, ThemePreference
            FROM    UserPreferences
            WHERE   UserId = @UserId";

        var prefsRow = (await _db.GetRecordsAsync<UserPreferencesRow>(
            prefsSql, new { UserId = authUserId }))?.FirstOrDefault();

        const string slackSql = @"
            SELECT  SlackTeamName, ConnectedAt
            FROM    SlackIntegrations
            WHERE   UserId = @UserId AND IsActive = 1
            LIMIT   1";

        var slackRow = (await _db.GetRecordsAsync<SlackRow>(
            slackSql, new { UserId = authUserId }))?.FirstOrDefault();

        var dto = new StudentProfileDto
        {
            Id              = row.Id,
            FirstName       = row.FirstName     ?? "",
            LastName        = row.LastName      ?? "",
            Email           = row.Email         ?? "",
            Phone           = row.Phone         ?? "",
            AcademicYear    = row.AcademicYear  ?? "",
            IdNumber        = row.IdNumber      ?? "",
            ProfileImageUrl = string.IsNullOrEmpty(row.ProfileImagePath)
                                ? null
                                : $"/profile-images/{row.ProfileImagePath}",
            ProjectTitle    = row.ProjectTitle,
            ProjectNumber   = row.ProjectNumber,
            TeamName        = row.TeamName,
            Preferences     = prefsRow is null ? new StudentPreferencesDto() : new StudentPreferencesDto
            {
                NotifyOnTasks           = prefsRow.NotifyOnTasks,
                NotifyOnDeadlines       = prefsRow.NotifyOnDeadlines,
                NotifyOnFeedback        = prefsRow.NotifyOnFeedback,
                NotifyOnSubmissions     = prefsRow.NotifyOnSubmissions,
                NotifyOnMentorUpdates   = prefsRow.NotifyOnMentorUpdates,
                GoogleCalendarConnected = prefsRow.GoogleCalendarConnected,
                ThemePreference         = prefsRow.ThemePreference ?? "system",
            },
            SlackConnection = slackRow is null ? new SlackConnectionDto() : new SlackConnectionDto
            {
                IsConnected = true,
                TeamName    = slackRow.SlackTeamName ?? "",
                ConnectedAt = slackRow.ConnectedAt   ?? "",
            }
        };

        return Ok(dto);
    }

    // PUT /api/student/me
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMyProfile(
        int authUserId,
        [FromBody] UpdateStudentProfileRequest req)
    {
        if (req is null) return BadRequest();
        var phone = (req.Phone ?? "").Trim();
        if (phone.Length > 20) return BadRequest("Phone number too long.");

        await _db.SaveDataAsync(
            "UPDATE users SET Phone = @Phone WHERE Id = @UserId",
            new { Phone = phone, UserId = authUserId });

        return NoContent();
    }

    // PUT /api/student/me/preferences
    [HttpPut("me/preferences")]
    public async Task<IActionResult> UpdateMyPreferences(
        int authUserId,
        [FromBody] StudentPreferencesDto req)
    {
        if (req is null) return BadRequest();

        var theme = req.ThemePreference is "dark" or "light" or "system"
            ? req.ThemePreference : "system";

        await _db.SaveDataAsync(@"
            INSERT OR REPLACE INTO UserPreferences
                (UserId, NotifyOnTasks, NotifyOnDeadlines, NotifyOnFeedback,
                 NotifyOnSubmissions, NotifyOnMentorUpdates,
                 GoogleCalendarConnected, ThemePreference, UpdatedAt)
            VALUES
                (@UserId, @NotifyOnTasks, @NotifyOnDeadlines, @NotifyOnFeedback,
                 @NotifyOnSubmissions, @NotifyOnMentorUpdates,
                 @GoogleCalendarConnected, @ThemePreference, datetime('now'))",
            new
            {
                UserId                  = authUserId,
                NotifyOnTasks           = req.NotifyOnTasks           ? 1 : 0,
                NotifyOnDeadlines       = req.NotifyOnDeadlines       ? 1 : 0,
                NotifyOnFeedback        = req.NotifyOnFeedback        ? 1 : 0,
                NotifyOnSubmissions     = req.NotifyOnSubmissions     ? 1 : 0,
                NotifyOnMentorUpdates   = req.NotifyOnMentorUpdates   ? 1 : 0,
                GoogleCalendarConnected = req.GoogleCalendarConnected ? 1 : 0,
                ThemePreference         = theme,
            });

        return NoContent();
    }

    // PUT /api/student/me/avatar
    [HttpPut("me/avatar")]
    public async Task<IActionResult> UpdateMyAvatar(
        int authUserId,
        [FromBody] UploadAvatarRequest req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.ImageBase64))
            return BadRequest();

        var ext = (req.Extension ?? "").ToLower().TrimStart('.');
        if (!AllowedImageExts.Contains(ext))
            return BadRequest("Unsupported image type.");

        // Get existing image path so we can delete it after replacing
        var existing = (await _db.GetRecordsAsync<AvatarPathRow>(
            "SELECT ProfileImagePath FROM users WHERE Id = @UserId",
            new { UserId = authUserId }))?.FirstOrDefault();

        string newFileName;
        try
        {
            newFileName = await _files.SaveFile(req.ImageBase64, ext, "profile-images");
        }
        catch
        {
            return StatusCode(500, "Image save failed.");
        }

        await _db.SaveDataAsync(
            "UPDATE users SET ProfileImagePath = @Path WHERE Id = @UserId",
            new { Path = newFileName, UserId = authUserId });

        if (!string.IsNullOrEmpty(existing?.ProfileImagePath))
            _files.DeleteFile(existing.ProfileImagePath, "profile-images");

        return Ok(new { url = $"/profile-images/{newFileName}" });
    }

    // DELETE /api/student/me/avatar
    [HttpDelete("me/avatar")]
    public async Task<IActionResult> RemoveMyAvatar(int authUserId)
    {
        var existing = (await _db.GetRecordsAsync<AvatarPathRow>(
            "SELECT ProfileImagePath FROM users WHERE Id = @UserId",
            new { UserId = authUserId }))?.FirstOrDefault();

        if (!string.IsNullOrEmpty(existing?.ProfileImagePath))
        {
            await _db.SaveDataAsync(
                "UPDATE users SET ProfileImagePath = NULL WHERE Id = @UserId",
                new { UserId = authUserId });
            _files.DeleteFile(existing.ProfileImagePath, "profile-images");
        }

        return NoContent();
    }

    // ── Private row types ─────────────────────────────────────────────────────

    private sealed class StudentProjectContext
    {
        public int    ProjectId   { get; set; }
        public string ProjectType { get; set; } = "";
    }

    private sealed class StudentProfileRow
    {
        public int     Id               { get; set; }
        public string? FirstName        { get; set; }
        public string? LastName         { get; set; }
        public string? Email            { get; set; }
        public string? Phone            { get; set; }
        public string? AcademicYear     { get; set; }
        public string? IdNumber         { get; set; }
        public string? ProfileImagePath { get; set; }
        public string? ProjectTitle     { get; set; }
        public int?    ProjectNumber    { get; set; }
        public string? TeamName         { get; set; }
    }

    private sealed class UserPreferencesRow
    {
        public bool   NotifyOnTasks           { get; set; }
        public bool   NotifyOnDeadlines       { get; set; }
        public bool   NotifyOnFeedback        { get; set; }
        public bool   NotifyOnSubmissions     { get; set; }
        public bool   NotifyOnMentorUpdates   { get; set; }
        public bool   GoogleCalendarConnected { get; set; }
        public string ThemePreference         { get; set; } = "system";
    }

    private sealed class SlackRow
    {
        public string? SlackTeamName { get; set; }
        public string? ConnectedAt   { get; set; }
    }

    private sealed class AvatarPathRow
    {
        public string? ProfileImagePath { get; set; }
    }
}
