namespace AuthWithAdmin.Server.AuthHelpers;
using Microsoft.AspNetCore.Identity;

public class PasswordService
{
    private readonly IPasswordHasher<MinimalUser> _passwordHasher;

    public PasswordService()
    {
        _passwordHasher = new PasswordHasher<MinimalUser>();
    }

    public string HashPassword(MinimalUser user, string password)
    {
        return _passwordHasher.HashPassword(user, password);
    }

    public PasswordVerificationResult VerifyPassword(MinimalUser user, string password)
    {
        return _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
    }

}