
namespace AuthWithAdmin.Server.Data;
using Shared.AuthSharedModels;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using AuthWithAdmin.Server.AuthHelpers;

public class AuthRepository
{
    //ניהול תהליכי התחברות
    private readonly TokenService _tokenService;
    private readonly DbRepository _db;
    private readonly PasswordService _passwordService;
    private readonly IHttpContextAccessor _context;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly bool _needApproval;
    private readonly bool _VerifyEmail;

    public AuthRepository(TokenService tokenService, DbRepository db, PasswordService passwordService,
        IHttpContextAccessor contextAccessor, ITokenBlacklistService tokenBlacklistService, IConfiguration config)
    {
        _db = db;
        _tokenService = tokenService;
        _passwordService = passwordService;
        _context = contextAccessor;
        _tokenBlacklistService = tokenBlacklistService;
        _needApproval = config.GetValue<bool>("UserManagement:NeedApproval");
        _VerifyEmail = config.GetValue<bool>("UserManagement:VerifyEmail");
    }


    /// <summary>
    /// //התחברות 
    /// </summary>
    /// <param name="user">פרטי משתמש</param>
    /// <returns>Token</returns>

    public async Task<UserAuthResult> Login(LoginForm user)
    {
        user.Email = cleanEmail(user.Email);

        var userFromDB = await GetUserByEmail<UserFromDB>(user.Email);
        if (userFromDB == null)
            return new UserAuthResult() { Result= AuthResults.UserNotFound };

        if (!VerifyPassword(userFromDB, user.Password))
            return new UserAuthResult() { Result = AuthResults.WrongPassword };

        userFromDB.Roles = await GetUserRoles(userFromDB.Id);
        string token = CreateToken(userFromDB);

        return new UserAuthResult() { User=userFromDB.MapUser(),Result=token };
    }

    // Verifies a user's password
    private bool VerifyPassword(UserFromDB user, string password)
    {
        user.Email = cleanEmail(user.Email);
        return _passwordService.VerifyPassword(new MinimalUser { Email = user.Email, PasswordHash = user.PasswordHash }, password) != PasswordVerificationResult.Failed;
    }

    /// <summary>
    /// //הרשמה
    /// </summary>
    /// <param name="newUser">פרטי משתמש</param>
    /// <returns>Token</returns>

    public async Task<UserAuthResult> Signup(SignupForm newUser)
    {
        newUser.Email = cleanEmail(newUser.Email);
        if (await GetUserByEmail<UserFromDB>(newUser.Email) != null)
            return new UserAuthResult { Result = AuthResults.Exists };

        var signupUser = CreateNewUser(newUser);
        signupUser.Id = await InsertUser(signupUser);
        if (signupUser.Id == 0)
            return new UserAuthResult { Result = AuthResults.CreateUserFailed };

        if (!_needApproval)
        {
            await ApproveUser(signupUser.Id);
            signupUser.Roles.Add(Roles.User);
        }
            
        return new UserAuthResult { User = signupUser.MapUser(), Result = CreateToken(signupUser) };
    }

    private async Task<int> InsertUser(UserFromDB user)
    {
        user.Email  = cleanEmail(user.Email);
        string query = "INSERT INTO Users (Email, PasswordHash, FirstName, LastName, IsVerified) VALUES (@Email, @PasswordHash, @FirstName, @LastName, @IsVerified)";
        return await _db.InsertReturnIdAsync(query, user);
    }

    // Creates a new user instance
    private UserFromDB CreateNewUser(SignupForm newUser)
    {
        newUser.Email = cleanEmail(newUser.Email);
        return new UserFromDB
        {
            Email = newUser.Email,
            PasswordHash = _passwordService.HashPassword(new MinimalUser { Email = newUser.Email }, newUser.Password),
            FirstName = newUser.FirstName,
            LastName = newUser.LastName,
            IsVerified = !_VerifyEmail
        };
    }




    /// <summary>
    /// //התחברות או הרשמה על ידי גוגל
    /// </summary>
    /// <param name="userFromGoogle">פרטי משתמש</param>
    /// <returns>Token</returns>


    public async Task<string> Google(UserFromDB userFromGoogle)
    {
        userFromGoogle.Email = cleanEmail(userFromGoogle.Email);
        var user = await GetUserByEmail<UserFromDB>(userFromGoogle.Email);
        if (user == null)
            user = await RegisterGoogleUser(userFromGoogle);
      
        else if (!user.IsVerified && !(await VerifyUser(user.Id)))
            return AuthResults.EmailNotVerified;
       
        user.Roles = await GetUserRoles(user.Id);

        return CreateToken(user);
    }

    // Registers a Google user
    private async Task<UserFromDB> RegisterGoogleUser(UserFromDB userFromGoogle)
    {
        userFromGoogle.IsVerified = true;
        userFromGoogle.PasswordHash = string.Empty;
        userFromGoogle.Id = await InsertUser(userFromGoogle);

        if (!_needApproval)
        {
            await ApproveUser(userFromGoogle.Id);
            userFromGoogle.Roles.Add(Roles.User);
        }
        
        return userFromGoogle;
    }


    /// <summary>
    /// //הוספת יוזר על ידי אדמין
    /// </summary>
    /// <param name="newUser">פרטי משתמש</param>
    /// <returns>משתמש חדש</returns>

    // Adds a user by an admin
    public async Task<AdminResults> AddUserByAdmin(UserAddedByAdmin newUser)
    {
        newUser.Email = cleanEmail(newUser.Email);
        if (await GetUserByEmail<UserFromDB>(newUser.Email) != null) return new AdminResults { Result = AuthResults.Exists };

        UserFromDB newUserForDB = new UserFromDB
        {
            Email = newUser.Email,
            FirstName = newUser.FirstName,
            LastName = newUser.LastName,
            PasswordHash = string.Empty,
            IsVerified = false
        };
        var newUserId = await InsertUser(newUserForDB);

        if (newUserId == 0) return new AdminResults { Result = AuthResults.CreateUserFailed };

        if (!await ApproveUser(newUserId)) return new AdminResults { Result = AuthResults.ChangeRoleFailed };

        newUserForDB.Id = newUserId;
        return new AdminResults { Result = _tokenService.GenerateToken(CreateClaimsIDEmail(newUserId, newUser.Email), 7), User = newUserForDB.MapUserToAdmin() };
    }

    // Creates claims for a user
    private List<Claim> CreateClaimsIDEmail(int userId, string email)
    {
        email = cleanEmail(email);
        return new List<Claim> { new Claim(JwtRegisteredClaimNames.NameId, userId.ToString()), new Claim(JwtRegisteredClaimNames.Email, email) };
    }

    /// <summary>
    /// //שכחתי סיסמה
    /// </summary>
    /// <param name="email">מייל</param>
    /// <returns>Token איפוס סיסמה</returns>

    public async Task<UserAuthResult> ForgotPassword(string email)
    {
        email = cleanEmail(email);
        var user = await GetUserByEmail<User>(email);
        if (user == null) return new UserAuthResult { Result = AuthResults.UserNotFound };

        var claims = CreateClaimsIDEmail(user.Id, email);
        return new UserAuthResult { User = user, Result = _tokenService.GenerateToken(claims, 0) };
    }


    /// <summary>
    /// //איפוס סיסמה - לאחר שכחתי סיסמה או התחברות ראשונה של משתמש שהאדמין רשם
    /// </summary>
    /// <param name="userPassword">פרטי משתמש - מזהה, סיסמה חדשה</param>
    /// <returns>האם הצליח</returns>


    public string CheckToken(string token)
    {

        if (token == null)
            return AuthResults.InvalidToken;

        var principal = _tokenService.ValidateToken(token);

        if (principal == null)
        {
            return AuthResults.InvalidToken;
        }

        if (_tokenBlacklistService.IsBlacklisted(token))
        {
            return AuthResults.TokenInBlackList;
        }
        return AuthResults.Success;
    }

    //איפוס סיסמה
    public async Task<string> ResetPassword(UserResetPassword userPassword)
    {
        var token = GetTokenFromHeader();
        var principal = _tokenService.ValidateToken(token);

        if (principal == null || _tokenBlacklistService.IsBlacklisted(token)) return AuthResults.InvalidToken;

        var email = await GetEmailFromID(userPassword.Id);
        if (string.IsNullOrEmpty(email)) return AuthResults.UserNotFound;

        string newPasswordHash = _passwordService.HashPassword(new MinimalUser { Email = email }, userPassword.NewPassword);
        return await _db.SaveDataAsync("UPDATE Users SET PasswordHash=@PasswordHash, IsVerified=1 WHERE Email=@Email", new { PasswordHash = newPasswordHash, Email = email }) > 0
            ? AuthResults.Success : AuthResults.Failed;
    }


    /// <summary>
    /// התנתקות
    /// </summary>

    public void Logout()
    {
        //קבלת מזהה משתמש

        var token = GetTokenFromHeader();
        if (!string.IsNullOrEmpty(token))
        {
            _tokenBlacklistService.AddToBlacklist(token);
        }
    }

    /// <summary>
    /// //אישור משתמש לשימוש בתוצר
    /// </summary>
    /// <param name="userId">מזהה משתמש</param>
    /// <returns>האם הצליח</returns>

    public async Task<bool> ApproveUser(int userId)
    {
        ///בדיקה שיש יוזר
       
        UserRole role = new UserRole() { UserId = userId, Role = Roles.User };
        string query = "INSERT INTO UserRoles (UserId,Role) VALUES (@UserId,@Role)";
        int ok = await _db.InsertReturnIdAsync(query, role);
        return ok > 0;
    }


    public async Task<List<string>> GetUserRoles(int UserId)
    {
        string rolesQuery = "SELECT Role from userRoles WHERE UserId = @UserId";

        List<string> roles = (await _db.GetRecordsAsync<string>(rolesQuery, new { UserId }))?.ToList();
        return roles ?? new List<string>();

    }


    public async Task<string> VerifyFromEmail(string token)
    {
        var principal = _tokenService.ValidateToken(token);
        if (principal == null || _tokenBlacklistService.IsBlacklisted(token))
            return AuthResults.InvalidToken;

        var email = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(email)) return AuthResults.UserNotFound;

        var user = await GetUserByEmail<UserFromDB>(email);
        if (user == null || !await VerifyUser(user.Id)) return AuthResults.ValidUserFailed;

        user.IsVerified = true;
        user.Roles = await GetUserRoles(user.Id);
        Logout();
        string newToken = CreateToken(user);
        return newToken;
    }

    /// <summary>
    /// אימות מייל
    /// </summary>
    /// <param name="userId">מזהה משתמש</param>
    /// <returns>האם הצליח</returns>

    public async Task<bool> VerifyUser(int Id)
    {
        ///בדיקה שיש יוזרררר
        string query = "UPDATE Users SET IsVerified = 1 WHERE Id = @Id";
        int ok = await _db.SaveDataAsync(query, new { Id });
        return ok > 0;
    }

    /// <summary>
    /// קבלת פרטי המשתמש המחובר
    /// </summary>
    /// <returns>המשתמש המחובר</returns>

    public async Task<User>? GetUser(string token = "")
    {
        if (string.IsNullOrEmpty(token))
        {
            token = GetTokenFromHeader();
        }

        if (CheckToken(token) != AuthResults.Success)
        {
            Logout();
            return null;
        }

        var principal = _tokenService.ValidateToken(token);

        return ExtractUserFromClaims(principal.Claims);
    }
    private User ExtractUserFromClaims(IEnumerable<Claim> claims)
    {
        return new User
        {
            Id = Convert.ToInt16(claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value),
            Email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value,
            FirstName = claims.FirstOrDefault(c => c.Type == ClaimTypes.GivenName)?.Value,
            LastName = claims.FirstOrDefault(c => c.Type == ClaimTypes.Surname)?.Value,
            Roles = claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList(),
            IsVerified = Convert.ToBoolean(claims.FirstOrDefault(c => c.Type == "IsVerified")?.Value),
        };
    }

    string GetTokenFromHeader()
    {
        var authorizationHeader = _context.HttpContext.Request.Headers["Authorization"].ToString();
        var token = authorizationHeader.StartsWith("Bearer ") ? authorizationHeader.Substring("Bearer ".Length).Trim() : authorizationHeader;
        return token;
    }

    /// <summary>
    /// //שינוי הרשאות משתמש
    /// </summary>
    /// <param name="roles">רשימת תפקידים של משתמש</param>
    /// <returns>האם הצליח</returns>

    public async Task<AdminResults> ChangeRoles(List<UserRole> roles)
    {
        AdminResults auth = new AdminResults();

        // Get the user first
        var user = await GetUserById<UserForAdmin>(roles[0].UserId);
        if (user == null) return new AdminResults { Result = AuthResults.UserNotFound };

        int rowsAffected = 0;

        // Process each role individually with correct parameters
        foreach (var role in roles)
        {
            string query = role.Enable
                ? "INSERT INTO UserRoles (UserId, Role) SELECT @UserId, @Role WHERE NOT EXISTS (SELECT 1 FROM UserRoles WHERE UserId = @UserId AND Role = @Role)"
                : "DELETE FROM UserRoles WHERE UserId = @UserId AND Role = @Role";

            // Execute SQL with the correct parameters for each role
            rowsAffected += await _db.SaveDataAsync(query, new { role.UserId, role.Role });
        }
        user.Roles = roles.Where(r => r.Enable).Select(a => a.Role).ToList();

        return new AdminResults { Result = rowsAffected == 0 ? AuthResults.ChangeRoleFailed : AuthResults.Success, User = user };
    }


    private async Task<T?> GetUserById<T>(int userId) where T : class
    {
        return (await _db.GetRecordsAsync<T>("SELECT FirstName,Email, Id FROM Users WHERE Id = @Id", new { Id = userId })).FirstOrDefault();
    }


    public async Task<T?> GetUserByEmail<T>(string Email) where T : class
    {
        Email = cleanEmail(Email);
        string query = "SELECT * FROM Users WHERE Email = @Email";

        T? user = (await _db.GetRecordsAsync<T>(query, new { Email })).FirstOrDefault();


        if (user == null) return null;
        return user;
    }

    public async Task<string> GetEmailFromID(int Id)
    {
        string query = "SELECT Email FROM Users WHERE Id = @Id";
        string? email = (await _db.GetRecordsAsync<string>(query, new { Id })).FirstOrDefault();
        return email;
    }

    public async Task<int> GetIdFromEmail(string Email)
    {
        Email = cleanEmail(Email);
        string query = "SELECT Id FROM Users WHERE Email = @Email";
        int Id = (await _db.GetRecordsAsync<int>(query, new { Email })).FirstOrDefault();
        return Id;
    }


    /// <summary>
    /// //יצירת Token
    /// </summary>
    /// <param name="user">משתמש</param>
    /// <returns>Token</returns>

    private string CreateToken(UserFromDB user)
    {
        user.Email = cleanEmail(user.Email);
        var claims = new List<Claim> // יצירת מזהה משתמש
        {
            new Claim(JwtRegisteredClaimNames.NameId, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.GivenName, user.FirstName),
            new Claim(JwtRegisteredClaimNames.FamilyName, user.LastName),
            new Claim("IsVerified", user.IsVerified.ToString().ToLower()) // Adding email verification claim
        };
        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var token = _tokenService.GenerateToken(claims); //יצירת TOKEN
        return token;
    }
    /// <summary>
    /// //עדכון Token שעומד לפוג
    /// </summary>
    /// <param></param>
    /// <returns>Token</returns>

    public async Task<string> RefreshToken()
    {
        var token = GetTokenFromHeader();

        //האם הטוקן תקין
        var principal = _tokenService.ValidateToken(token);
        if (principal == null)
        {
            Logout();
            return AuthResults.InvalidToken;
        }

        string newToken = _tokenService.RefreshToken(token);
        return newToken;
    }

    string cleanEmail(string email) {
        return email.ToLower().Trim();
    
    }

}


