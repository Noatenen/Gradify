using Microsoft.AspNetCore.Mvc;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;

namespace AuthWithAdmin.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]

    public class UsersController : ControllerBase
    {
        private readonly AuthRepository _authRepository;
        private readonly EmailHelper _emailHelper;
        private readonly TokenService _tokenService;
        private readonly string _appName;
        public UsersController(AuthRepository authRepository, EmailHelper emailHelper, TokenService tokenService, IConfiguration config)
        {
            _authRepository = authRepository;
            _emailHelper = emailHelper;
            _tokenService = tokenService;
            _appName = config.GetValue<string>("Email:AppName");
        }

        /*
         יוזר נרשם - לא מאומת
         יוזר מתחבר - נכנס רק אם הוא מאומת
         יוזר מתחבר דרך גוגל
         יוזר שוכח סיסמה --> מקבל מייל עם טוקן
         יוזר לוחץ על מייל עם טוקן --> מקבל אישור ומעבר לדף שכחתי סיסמה
         */


        //קבלת פרטי משתמש
        [HttpGet("user")]
        public async Task<ActionResult> GetUser()
        {
            User? user = await _authRepository.GetUser();
            if (user == null)
                return Unauthorized();
            return Ok(user);
        }


        //התחברות
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginForm user)
        {

            UserAuthResult auth = await _authRepository.Login(user);
            if (auth.User == null)
                return Unauthorized("Invalid username or password");
            return Ok(auth.Result);
        }


        //הרשמה
        [HttpPost("signup")]
        public async Task<IActionResult> Signup(SignupForm user)
        {

            UserAuthResult auth = await _authRepository.Signup(user);
            switch (auth.Result)
            {
                case AuthResults.Exists:
                    return Unauthorized("User already exists");
                case AuthResults.CreateUserFailed:
                    return BadRequest("Error creating user");
                default:
                    return Ok(auth.Result);
            }
        }

        //שליחת מייל אימות חדש
        [HttpPost("SendVerificationEmail")]
        public async Task<IActionResult> SendVerificationEmail(User user)
        {

            if (user == null) return Unauthorized();


            List<Claim> claims = new List<Claim>()
            {
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
            };
            string emailToken = _tokenService.GenerateToken(claims);


            string redirectURL = $"{getPath()}/api/users/VerifyEmail?token={Uri.EscapeDataString(emailToken)}";

            var placeholders = new Dictionary<string, string>
            {
                { "USERNAME", user.FirstName },
                { "APPNAME", _appName },
                { "URL", redirectURL }
            };

            string emailBody = await _emailHelper.GetEmailTemplateAsync("VerifyEmail", placeholders);

            if (emailBody == null)
                return BadRequest(AuthResults.EmailFailed);

            MailModel mail = new MailModel()
            {
                Body = emailBody,
                Recipients = new List<string>() { user.Email },
                Subject = "אימות כתובת מייל"
            };

            bool ok = await _emailHelper.SendEmail(mail);
            if (!ok)
                return BadRequest(AuthResults.EmailFailed);
            return Ok(AuthResults.Success);

        }


        //התחברות דרך גוגל
        [HttpGet("google/{page}")]
        public IActionResult Google(string page = "./")
        {
            var props = new AuthenticationProperties { RedirectUri = $"api/users/signin-google/{page}" };
            props.Items["page"] = page;
            return Challenge(props, GoogleDefaults.AuthenticationScheme);
        }

        //לכאן גוגל מגיע לאחר ההתחברות
        [HttpGet("signin-google/{page}")]
        public async Task<IActionResult> GoogleLogin(string page = "./")
        {
            var result = await HttpContext.AuthenticateAsync(GoogleDefaults.AuthenticationScheme);
            
            if (result?.Principal == null)
                return BadRequest("Invalid token");

            //יצירת משתמש
            UserFromDB googleUser = new UserFromDB()
            {
                Email = result.Principal.FindFirstValue(ClaimTypes.Email),
                FirstName = result.Principal.FindFirstValue(ClaimTypes.GivenName),
                LastName = result.Principal.FindFirstValue(ClaimTypes.Surname),
                PasswordHash = string.Empty
            };

            //יצירת Token וקידוד שלו
            string token = await _authRepository.Google(googleUser);
            switch (token)
            {
                case AuthResults.CreateUserFailed:
                    return Unauthorized("Failed to create user");
                case AuthResults.EmailNotVerified:
                    return BadRequest(googleUser.Email);
                default:
                    //מעבר לדף שיקבל את היוזר
                    string redirectUrl = $"{getPath()}/{PageRoutes.Redirect}?token={token}";
                    return Redirect(redirectUrl);
            }

        }

        //שכחתי סיסמה
        [HttpGet("ForgotPassword")]
        public async Task<IActionResult> ForgotPassword(string email)
        {

            if (string.IsNullOrEmpty(email))
                return Unauthorized(AuthResults.UserNotFound);

            email = email.Trim().ToLower();

            UserAuthResult auth = await _authRepository.ForgotPassword(email);
            if (auth.Result == AuthResults.UserNotFound)
                return BadRequest(AuthResults.UserNotFound);



            string redirectURL = $"{getPath()}/api/users/ResetPassword?token={Uri.EscapeDataString(auth.Result)}";

            var placeholders = new Dictionary<string, string>
            {
                { "USERNAME", auth.User.FirstName },
                { "APPNAME", _appName },
                { "URL", redirectURL }
            };

            string emailBody = await _emailHelper.GetEmailTemplateAsync("ForgetPassword", placeholders);

            if (emailBody == null)
                return BadRequest(AuthResults.Failed);

            MailModel mail = new MailModel()
            {
                Body = emailBody,
                Recipients = new List<string>() { email },
                Subject = "איפוס סיסמה"
            };

            bool ok = await _emailHelper.SendEmail(mail);
            if (!ok)
                return BadRequest(AuthResults.Failed);
            return Ok(AuthResults.Success);


        }
        [HttpGet("ResetPassword")]
        public async Task<IActionResult> ResetPasswordAPI(string token)
        {

            string results = _authRepository.CheckToken(token);
            if (results != AuthResults.Success)
                return Redirect($"{getPath()}/{PageRoutes.Login}");

            return Redirect($"{getPath()}/{PageRoutes.ResetPassword}?token={Uri.EscapeDataString(token)}");
        }



        // איפוס סיסמה
        [Authorize]
        [HttpPost("ResetPassword")]
        public async Task<IActionResult> ResetPassword(UserResetPassword userPassword)
        {
            string token = await _authRepository.ResetPassword(userPassword);
            switch (token)
            {
                case AuthResults.UserNotFound:
                    return BadRequest("Invalid username");
                case AuthResults.Failed:
                    return BadRequest("Failed to reset password");
                default:
                    return Ok(AuthResults.Success);
            }
        }


        [HttpGet("VerifyEmail")]
        public async Task<IActionResult> VerifyEmail(string token)
        {
            string results = await _authRepository.VerifyFromEmail(token);
            switch (results)
            {
                case AuthResults.InvalidToken:
                    return Redirect($"{getPath()}/{PageRoutes.VerifyEmail}");

                case AuthResults.UserNotFound:
                    return Redirect($"{getPath()}/{PageRoutes.VerifyEmail}");

                case AuthResults.ValidUserFailed:
                    return Redirect($"{getPath()}/{PageRoutes.VerifyEmail}");

                default:
                    string redirectUrl = $"{getPath()}/{PageRoutes.Redirect}?token={results}";
                    return Redirect(redirectUrl);
            }
        }



        //מרענן את הטוקן שעומד לפוג
        [HttpGet("refresh")]
        public async Task<IActionResult> refreshToken()
        {

            string token = await _authRepository.RefreshToken();
            if (string.IsNullOrEmpty(token) || token == AuthResults.InvalidToken)
            {
                return Unauthorized();
            }

            return Ok(token);
        }


        //התנתקות
        [HttpGet("logout")]
        public async Task<IActionResult> Logout()
        {
            _authRepository.Logout();

            return Unauthorized();
        }


        string getPath()
        {
            var request = HttpContext.Request;
            return $"{request.Scheme}://{request.Host}{request.PathBase}";
        }

    }
}
