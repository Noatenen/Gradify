using AuthWithAdmin.Client.ClientHelpers;
using AuthWithAdmin.Shared;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client
{
    public interface IAuthenticationService
    {

        event Action OnAuthenticationStateChanged;

        Task<bool> Login(LoginForm user);
        Task<bool> Signup(SignupForm user);

        Task<User> GetUserFromClaimAsync();
        Task Logout();
        Task TokenLogin(string token);

        Task<string> ForgetPassword(ForgetPasswordDTO email);

        Task<string> ResetPassword(UserResetPassword passwordDTO);
        Task resetPasswordAuth(string token);

        Task<AdminResults> ToggleUserRole(UserForAdmin user, string role);
        Task<string> ResendVerafication(User user);
        Task<List<UserForAdmin>> GetUsersByRoles(string role);
        Task<AdminResults> AdminAddUser(UserAddedByAdmin user);
        Task<string?> UpdateUserAsync(int userId, AdminUpdateUserRequest req);
    }
}
