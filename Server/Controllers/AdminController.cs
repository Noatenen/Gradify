using AuthWithAdmin.Server.AuthHelpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Controllers
{
    [ServiceFilter(typeof(AuthCheck))]
    [Authorize(Roles = Roles.Admin)]
    [Route("api/[controller]")]
    [ApiController]

    public class AdminController : ControllerBase
    {
        private readonly AuthRepository _authRepository;
        private readonly EmailHelper _emailHelper;
        private readonly DbRepository _db;
        private readonly string _appName;

        public AdminController(AuthRepository authRepository, EmailHelper emailHelper, DbRepository db, IConfiguration config)
        {
            _authRepository = authRepository;
            _emailHelper = emailHelper;
            _db = db;
            _appName = config.GetValue<string>("Email:AppName");

        }

        /*
         אדמין רוצה לדעת מי היוזרים שלו
         הוא רוצה להוסיף יוזרים
         הוא רוצה לשנות הרשאות
         */
        //שינוי הרשאות למשתמש
        [HttpPost("roles")]
        public async Task<IActionResult> ChangeRoles(List<UserRole> roles)
        {

            if (roles == null || roles.Count == 0)
                return BadRequest("Invalid request");

            AdminResults auth = await _authRepository.ChangeRoles(roles);
            if (auth.Result == AuthResults.UserNotFound)
            {
                auth.User = null;
                return BadRequest(auth);
            }

            if (auth.Result == AuthResults.ChangeRoleFailed)
            {
                auth.User = null;
                return BadRequest(auth);
            }


            UserRole approve = roles.Where(r => r.Role == "User" && r.Enable).FirstOrDefault();
            if (approve == null)
            {
                auth.Result = AuthResults.Success;
                return Ok(auth);
            }


            //מייל
            string redirectURL = $"{getPath()}/{PageRoutes.Login}";

            var placeholders = new Dictionary<string, string>
            {
                { "USERNAME", auth.User.FirstName },
                { "APPNAME", _appName },
                { "URL", redirectURL }
            };
            string emailBody = await _emailHelper.GetEmailTemplateAsync("GotApproved", placeholders);

            if (emailBody == null)
            {
                auth.Result = AuthResults.EmailFailed;
                return BadRequest(auth);
            }

            MailModel mail = new MailModel()
            {
                Body = emailBody,
                Recipients = new List<string>() { auth.User.Email },
                Subject = $"ברוכים הבאים ל{_appName}"
            };

            bool ok = await _emailHelper.SendEmail(mail);
            if (!ok)
            {
                auth.Result = AuthResults.EmailFailed;
                return BadRequest(auth);
            }


            auth.Result = AuthResults.Success;
            return Ok(auth);



        }

        //הוספת משתמש על ידי אדמין - אוטומטי מאושר
        [HttpPost("AddUser")]
        public async Task<IActionResult> AddAdmin(UserAddedByAdmin newUser)
        {
            AdminResults auth = await _authRepository.AddUserByAdmin(newUser);

            if (auth.Result == AuthResults.ChangeRoleFailed)
                return BadRequest(auth);

            if (auth.Result == AuthResults.Exists)
                return BadRequest(auth);

            if (auth.Result == AuthResults.CreateUserFailed)
                return BadRequest(auth);

            int newUserId = auth.User.Id;

            // Persist Phone and AcademicYear (not stored by AddUserByAdmin)
            await _db.SaveDataAsync(
                "UPDATE users SET Phone = @Phone, AcademicYear = @AcademicYear WHERE Id = @Id",
                new
                {
                    Phone        = newUser.Phone?.Trim()        ?? "",
                    AcademicYear = newUser.AcademicYear?.Trim() ?? "2025-2026",
                    Id           = newUserId,
                });

            // Insert the selected role (AddUserByAdmin only inserts the "User" approval role)
            var validRoles = new[] { Roles.Student, Roles.Mentor, Roles.Staff, Roles.Admin };
            var primaryRole = newUser.Role?.Trim() ?? Roles.Student;
            if (validRoles.Contains(primaryRole))
            {
                await _db.SaveDataAsync(
                    "INSERT INTO UserRoles (UserId, Role) VALUES (@UserId, @Role)",
                    new { UserId = newUserId, Role = primaryRole });
            }

            // Reflect updated fields in the returned user object
            auth.User.Phone        = newUser.Phone?.Trim()        ?? "";
            auth.User.AcademicYear = newUser.AcademicYear?.Trim() ?? "2025-2026";
            auth.User.Roles.Add(primaryRole);

            // שליחת מייל עם קישור לאיפוס סיסמה
            string redirectURL = $"{getPath()}/api/users/ResetPassword?token={Uri.EscapeDataString(auth.Result)}";

            var placeholders = new Dictionary<string, string>
            {
                { "USERNAME", auth.User.FirstName },
                { "APPNAME", _appName },
                { "URL", redirectURL }
            };

            string emailBody = await _emailHelper.GetEmailTemplateAsync("AddedByAdmin", placeholders);

            if (emailBody == null)
            {
                auth.Result = AuthResults.EmailFailed;
                return Ok(auth);
            }

            MailModel mail = new MailModel()
            {
                Body = emailBody,
                Recipients = new List<string>() { auth.User.Email },
                Subject = $"ברוכים הבאים ל{_appName}"
            };

            bool ok = await _emailHelper.SendEmail(mail);
            if (!ok)
            {
                auth.Result = AuthResults.EmailFailed;
                return Ok(auth);
            }

            auth.Result = AuthResults.Success;
            return Ok(auth);
        }


        // עדכון פרטי משתמש + תפקיד
        [HttpPut("users/{id:int}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] AdminUpdateUserRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.FirstName) || string.IsNullOrWhiteSpace(req.LastName))
                return BadRequest("שם פרטי ושם משפחה הם שדות חובה");

            var validRoles = new[] { Roles.Student, Roles.Mentor, Roles.Staff, Roles.Admin };
            if (string.IsNullOrWhiteSpace(req.Role) || !validRoles.Contains(req.Role))
                return BadRequest("תפקיד לא תקין");

            // Update personal details
            const string updateSql = @"
                UPDATE users
                SET    FirstName    = @FirstName,
                       LastName     = @LastName,
                       Phone        = @Phone,
                       AcademicYear = @AcademicYear
                WHERE  Id = @Id";

            int affected = await _db.SaveDataAsync(updateSql, new
            {
                FirstName    = req.FirstName.Trim(),
                LastName     = req.LastName.Trim(),
                Phone        = req.Phone?.Trim() ?? "",
                AcademicYear = req.AcademicYear?.Trim() ?? "",
                Id           = id,
            });

            if (affected == 0) return NotFound("המשתמש לא נמצא");

            // Replace primary role (keep "User" approval role if present)
            await _db.SaveDataAsync(
                "DELETE FROM UserRoles WHERE UserId = @Id AND Role != 'User'",
                new { Id = id });

            await _db.SaveDataAsync(
                "INSERT INTO UserRoles (UserId, Role) VALUES (@UserId, @Role)",
                new { UserId = id, Role = req.Role });

            return Ok();
        }

        //קבלת כל המשתמשים לפי הרשאה מסויימת
        [HttpGet]
        [HttpGet("{roles?}")]
        public async Task<IActionResult> GetAllUsers(string? roles = null)
        {
            string roleQuery = "'%' || @roles";
            if (string.IsNullOrEmpty(roles))
                roleQuery = "'%'";

            string query = $@"SELECT
    users.Id,
    users.FirstName,
    users.LastName,
    users.RegisterDate,
    users.Email,
    users.Phone,
    users.AcademicYear,
    users.IsActive,
    COALESCE(GROUP_CONCAT(userRoles.role, ','), '') AS userRoles
FROM users
LEFT JOIN userRoles ON users.id = userRoles.UserId
GROUP BY users.Id, users.FirstName, users.LastName, users.RegisterDate
HAVING userRoles LIKE {roleQuery};";
            List<userAdminFromDb> users = (await _db.GetRecordsAsync<userAdminFromDb>(query, new { roles })).ToList();

            List<UserForAdmin> usersAdmin = users.Select(u => u.MapUser()).ToList();
            return Ok(usersAdmin);
        }

        string getPath()
        {
            var request = HttpContext.Request;
            return $"{request.Scheme}://{request.Host}{request.PathBase}";
        }
    }


}

class userAdminFromDb
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public DateTime RegisterDate { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string AcademicYear { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string userRoles { get; set; }

    public UserForAdmin MapUser()
    {
        return new UserForAdmin()
        {
            Id = Id,
            FirstName = FirstName,
            LastName = LastName,
            Email = Email,
            RegisterDate = RegisterDate,
            Phone = Phone,
            AcademicYear = AcademicYear,
            IsActive = IsActive,
            Roles = string.IsNullOrEmpty(userRoles) ? new List<string>() : userRoles.Split(',').ToList()
        };
    }
}