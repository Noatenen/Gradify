using Microsoft.AspNetCore.Components.Authorization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Net.Http.Json;
using System.Net.Http;
using System.Security.Claims;
using AuthWithAdmin.Shared;
using AuthWithAdmin.Shared.AuthSharedModels;
using AuthWithAdmin.Client.ClientHelpers;
using AuthWithAdmin.Client.Pages;


namespace AuthWithAdmin.Client
{
    public class AuthenticationService : IAuthenticationService
    {

        private readonly HttpClient _httpClient;
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly AuthenticationState _anonymous;

        public event Action OnAuthenticationStateChanged;

        public AuthenticationService(HttpClient client, AuthenticationStateProvider authStateProvider)
        {
            _httpClient = client;
            _authStateProvider = authStateProvider;
        }

        //התנתקות 
        public async Task Logout()
        {

            var get = await _httpClient.GetAsync("api/users/logout");
            await ((AuthStateProvider)_authStateProvider).NotifyUserLogout();
            OnAuthenticationStateChanged?.Invoke();

        }

        //התחברות
        public async Task<bool> Login(LoginForm user)
        {
            //נרמול מייל - ללא רווחים ואותיות גדולות
            user.Email = user.Email.Trim().ToLower();
            var postUser = await _httpClient.PostAsJsonAsync("api/users/login", user);

            //האם הצליח
            if (postUser.IsSuccessStatusCode)
            {
                string token = await postUser.Content.ReadAsStringAsync();
                await ((AuthStateProvider)_authStateProvider).NotifyAuthenticationStateChanged(token);
                OnAuthenticationStateChanged?.Invoke();
                return true;
            }
            return false;
        }

        //הרשמה
        public async Task<bool> Signup(SignupForm user)
        {
            //נרמול מייל - ללא רווחים ואותיות גדולות
            user.Email = user.Email.Trim().ToLower();
            var postUser = await _httpClient.PostAsJsonAsync("api/users/signup", user);

            //האם הצליח
            if (postUser.IsSuccessStatusCode)
            {
                string token = await postUser.Content.ReadAsStringAsync();
                await ((AuthStateProvider)_authStateProvider).NotifyAuthenticationStateChanged(token);
                OnAuthenticationStateChanged?.Invoke();

                return true;
            }
            return false;
        }

        //התחברות דרך גוגל
        public async Task TokenLogin(string token)
        {
            await ((AuthStateProvider)_authStateProvider).NotifyAuthenticationStateChanged(token);
            OnAuthenticationStateChanged?.Invoke();
        }

        public async Task resetPasswordAuth(string token)
        {
            await ((AuthStateProvider)_authStateProvider).NotifyAuthenticationStateChanged(token);
            OnAuthenticationStateChanged?.Invoke();

        }



        //קבלת פרטי המשתמש
        public async Task<User> GetUserFromClaimAsync()
        {
            var authenticationState = await _authStateProvider.GetAuthenticationStateAsync();
            var user = authenticationState.User;

            //אם המשתמש מחובר
            if (user.Identity.IsAuthenticated)
            {
                var userDto = new User
                {
                    FirstName = user.FindFirst(c => c.Type == "given_name")?.Value,
                    LastName = user.FindFirst(c => c.Type == "family_name")?.Value,
                    Email = user.FindFirst(c => c.Type == "email")?.Value,
                    Id = Convert.ToInt16(user.FindFirst(c => c.Type == "nameid")?.Value),
                    IsVerified = Convert.ToBoolean(user.FindFirst(c => c.Type == "IsVerified")?.Value),
                    Roles = user.FindAll(c => c.Type == ClaimTypes.Role).Select(r => r.Value).ToList(),

                    // Map other claims to UserDTO properties as needed
                };

                return userDto;
            }
            //אם לא 
            else
            {
                return null;
            }
        }

        public async Task<string> ResetPassword(UserResetPassword passwordDTO)
        {
            var resetResponse = await _httpClient.PostAsJsonAsync("api/users/ResetPassword", passwordDTO);
            await Logout();
            return resetResponse.Content.ReadAsStringAsync().Result;

        }

        public async Task<string> ForgetPassword(ForgetPasswordDTO email)
        {
            var forgetResponse = await _httpClient.GetAsync($"api/users/ForgotPassword?email={email.Email}");
            return forgetResponse.Content.ReadAsStringAsync().Result;
        }

        public async Task<List<UserForAdmin>> GetUsersByRoles(string role)
        {
            var getUsers = await _httpClient.GetAsync($"api/Admin/{role}");
            if (getUsers.IsSuccessStatusCode)
            {
                return getUsers.Content.ReadFromJsonAsync<List<UserForAdmin>>().Result;
            }
            return null;

        }

        public async Task<string> ResendVerafication(User user)
        {
            var res = await _httpClient.PostAsJsonAsync("api/users/SendVerificationEmail", user);

            return res.Content.ReadAsStringAsync().Result;
        }
        public async Task<AdminResults> AdminAddUser(UserAddedByAdmin user)
        {
            var addResponse = await _httpClient.PostAsJsonAsync("api/admin/AddUser", user);
           
            AdminResults newUser = addResponse.Content.ReadFromJsonAsync<AdminResults>().Result;
            return newUser;
        }
        public async Task<AdminResults> ToggleUserRole(UserForAdmin user, string role)
        {
            var rolesToChange = new List<UserRole>();

            void UpdateRole(string roleName, bool enable)
            {
                if (enable)
                    user.Roles.Add(roleName);
                else
                    user.Roles.Remove(roleName);

                rolesToChange.Add(new UserRole { Role = roleName, UserId = user.Id, Enable = enable });
            }

            bool hasRole = user.Roles.Contains(role);

            if (role == Roles.Admin)
            {
                UpdateRole(Roles.Admin, !hasRole);

                if (!hasRole && !user.Roles.Contains(Roles.User))
                    UpdateRole(Roles.User, true);
            }
            else if (role == Roles.User)
            {
                UpdateRole(Roles.User, !hasRole);

                if (hasRole && user.Roles.Contains(Roles.Admin))
                    UpdateRole(Roles.Admin, false);
            }

            var rolesResponse = await _httpClient.PostAsJsonAsync("api/admin/roles", rolesToChange);
            AdminResults auth = rolesResponse.Content.ReadFromJsonAsync<AdminResults>().Result;
            return auth;
           
        }

    }
}
