using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

/// <summary>
/// Public endpoint for student team self-registration.
/// No authentication required — this is the entry point before students have accounts.
/// </summary>
[Route("api/team-registration")]
[ApiController]
[AllowAnonymous]
public class TeamRegistrationController : ControllerBase
{
    private readonly DbRepository    _db;
    private readonly PasswordService _passwordService;
    private readonly FilesManage     _files;
    private readonly EmailHelper     _emailHelper;
    private readonly string          _appName;

    public TeamRegistrationController(
        DbRepository    db,
        PasswordService passwordService,
        FilesManage     files,
        EmailHelper     emailHelper,
        IConfiguration  config)
    {
        _db              = db;
        _passwordService = passwordService;
        _files           = files;
        _emailHelper     = emailHelper;
        _appName         = config.GetValue<string>("Email:AppName") ?? "Gradify";
    }

    // POST /api/team-registration
    [HttpPost]
    public async Task<IActionResult> CreateTeam([FromBody] CreateTeamRequest req)
    {
        // ── Basic validation ─────────────────────────────────────────────────
        var s1Err = ValidateStudent(req.Student1, "סטודנט 1");
        if (s1Err != null) return BadRequest(s1Err);

        var s2Err = ValidateStudent(req.Student2, "סטודנט 2");
        if (s2Err != null) return BadRequest(s2Err);

        // ── Cross-student uniqueness ─────────────────────────────────────────
        var email1 = req.Student1.Email.Trim().ToLower();
        var email2 = req.Student2.Email.Trim().ToLower();
        var id1    = req.Student1.IdNumber.Trim();
        var id2    = req.Student2.IdNumber.Trim();

        if (email1 == email2)
            return BadRequest("כתובות האימייל של שני הסטודנטים חייבות להיות שונות");

        if (id1 == id2)
            return BadRequest("מספרי תעודת הזהות של שני הסטודנטים חייבים להיות שונים");

        // ── DB uniqueness checks ─────────────────────────────────────────────
        if (await EmailExistsAsync(email1))
            return BadRequest($"האימייל {req.Student1.Email} כבר רשום במערכת");

        if (await EmailExistsAsync(email2))
            return BadRequest($"האימייל {req.Student2.Email} כבר רשום במערכת");

        if (await IdNumberExistsAsync(id1))
            return BadRequest($"מספר תעודת הזהות {id1} כבר רשום במערכת");

        if (await IdNumberExistsAsync(id2))
            return BadRequest($"מספר תעודת הזהות {id2} כבר רשום במערכת");

        // ── Auto-generate team name from student first names ─────────────────
        var name1 = req.Student1.FirstName.Trim();
        var name2 = req.Student2.FirstName.Trim();
        string teamName = (!string.IsNullOrEmpty(name1) && !string.IsNullOrEmpty(name2))
            ? $"{name1} + {name2}"
            : "צוות חדש";

        // ── Generate temporary passwords ─────────────────────────────────────
        string pass1 = GenerateTempPassword();
        string pass2 = GenerateTempPassword();
        while (pass2 == pass1) pass2 = GenerateTempPassword();

        // ── Create the team ──────────────────────────────────────────────────
        int teamId;
        try
        {
            teamId = await _db.InsertReturnIdAsync(
                "INSERT INTO Teams (TeamName, AcademicYearId) VALUES (@TeamName, @AcademicYearId)",
                new { TeamName = teamName, AcademicYearId = 1 });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"שגיאה ביצירת הצוות: {ex.Message}");
        }

        if (teamId == 0)
            return StatusCode(500, "שגיאה ביצירת הצוות — לא התקבל מזהה");

        // ── Create both students ─────────────────────────────────────────────
        int userId1, userId2;
        try
        {
            userId1 = await CreateStudentAsync(req.Student1, pass1, teamId, email1);
            userId2 = await CreateStudentAsync(req.Student2, pass2, teamId, email2);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup: delete the team if user creation failed
            await _db.SaveDataAsync("DELETE FROM Teams WHERE Id = @Id", new { Id = teamId });
            return StatusCode(500, $"שגיאה ביצירת חשבון הסטודנט: {ex.Message}");
        }

        // ── Send welcome emails ──────────────────────────────────────────────
        string? warning = null;
        string loginUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/login";

        bool sent1 = await SendWelcomeEmailAsync(req.Student1, email1, pass1, teamName, loginUrl);
        bool sent2 = await SendWelcomeEmailAsync(req.Student2, email2, pass2, teamName, loginUrl);

        if (!sent1 || !sent2)
            warning = "הצוות נוצר אך שליחת האימייל לאחד הסטודנטים נכשלה. ניתן להתחבר עם הפרטים שנוצרו.";

        return Ok(new TeamRegistrationResultDto
        {
            Success = true,
            TeamId  = teamId,
            Warning = warning,
        });
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<int> CreateStudentAsync(
        StudentRegistrationForm form,
        string                  tempPassword,
        int                     teamId,
        string                  normalizedEmail)
    {
        // Hash password
        string hash = _passwordService.HashPassword(
            new MinimalUser { Email = normalizedEmail },
            tempPassword);

        // Insert core user record (IsVerified = 1 so they can log in immediately)
        int userId = await _db.InsertReturnIdAsync(
            @"INSERT INTO Users (Email, PasswordHash, FirstName, LastName, IsVerified)
              VALUES (@Email, @PasswordHash, @FirstName, @LastName, 1)",
            new
            {
                Email        = normalizedEmail,
                PasswordHash = hash,
                FirstName    = form.FirstName.Trim(),
                LastName     = form.LastName.Trim(),
            });

        if (userId == 0)
            throw new InvalidOperationException($"Failed to create user for {normalizedEmail}");

        // Save profile image if provided
        string? imagePath = null;
        if (!string.IsNullOrWhiteSpace(form.ProfileImageBase64) &&
            !string.IsNullOrWhiteSpace(form.ProfileImageExtension))
        {
            try
            {
                imagePath = await _files.SaveFile(
                    form.ProfileImageBase64,
                    form.ProfileImageExtension.TrimStart('.').ToLower(),
                    "profile-images");
            }
            catch
            {
                // Image save is non-fatal — user is created without an image
            }
        }

        // Store extended fields
        await _db.SaveDataAsync(
            @"UPDATE Users
              SET Phone            = @Phone,
                  IdNumber         = @IdNumber,
                  ProfileImagePath = @ProfileImagePath
              WHERE Id = @Id",
            new
            {
                Phone            = form.Phone?.Trim() ?? "",
                IdNumber         = form.IdNumber.Trim(),
                ProfileImagePath = imagePath,
                Id               = userId,
            });

        // Assign roles: "User" (approval) + "Student"
        await _db.SaveDataAsync(
            "INSERT INTO UserRoles (UserId, Role) VALUES (@UserId, @Role)",
            new { UserId = userId, Role = Roles.User });

        await _db.SaveDataAsync(
            "INSERT INTO UserRoles (UserId, Role) VALUES (@UserId, @Role)",
            new { UserId = userId, Role = Roles.Student });

        // Add to team
        await _db.SaveDataAsync(
            "INSERT INTO TeamMembers (TeamId, UserId, IsActive) VALUES (@TeamId, @UserId, 1)",
            new { TeamId = teamId, UserId = userId });

        return userId;
    }

    private async Task<bool> SendWelcomeEmailAsync(
        StudentRegistrationForm form,
        string                  email,
        string                  tempPassword,
        string                  teamName,
        string                  loginUrl)
    {
        try
        {
            var placeholders = new Dictionary<string, string>
            {
                { "USERNAME", form.FirstName.Trim() },
                { "EMAIL",    email                 },
                { "PASSWORD", tempPassword           },
                { "TEAMNAME", teamName               },
                { "APPNAME",  _appName               },
                { "URL",      loginUrl               },
            };

            string? body = await _emailHelper.GetEmailTemplateAsync(
                "TeamStudentCreated", placeholders);

            if (body is null) return false;

            return await _emailHelper.SendEmail(new MailModel
            {
                Subject    = $"ברוכים הבאים ל{_appName} — פרטי כניסה",
                Body       = body,
                Recipients = new List<string> { email },
            });
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> EmailExistsAsync(string email) =>
        (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM Users WHERE Email = @Email",
            new { Email = email })).FirstOrDefault() > 0;

    private async Task<bool> IdNumberExistsAsync(string idNumber) =>
        (await _db.GetRecordsAsync<int>(
            "SELECT COUNT(1) FROM Users WHERE IdNumber = @IdNumber",
            new { IdNumber = idNumber })).FirstOrDefault() > 0;

    private static string? ValidateStudent(StudentRegistrationForm s, string label)
    {
        if (string.IsNullOrWhiteSpace(s.FirstName)) return $"שם פרטי של {label} הוא שדה חובה";
        if (string.IsNullOrWhiteSpace(s.LastName))  return $"שם משפחה של {label} הוא שדה חובה";
        if (string.IsNullOrWhiteSpace(s.IdNumber))  return $"מספר תעודת זהות של {label} הוא שדה חובה";
        if (string.IsNullOrWhiteSpace(s.Email))     return $"אימייל של {label} הוא שדה חובה";
        if (!s.Email.Contains('@'))                 return $"כתובת האימייל של {label} אינה תקינה";
        return null;
    }

    private static string GenerateTempPassword() =>
        Random.Shared.Next(100000, 999999).ToString("D6");
}
