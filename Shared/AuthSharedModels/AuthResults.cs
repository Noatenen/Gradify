using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

public class AuthResults
{
    public const string Success = "Success";
    public const string Failed = "Failed";
    public const string UserNotFound = "User not found";
    public const string Exists = "User allready exists";
    public const string EmailNotVerified = "Email not verified";
    public const string WrongPassword = "Wrong password";
    public const string ValidUserFailed = "Fail to validate user";
    public const string ChangeRoleFailed = "Failed to change role";
    public const string TokenExpired = "Token has expired";
    public const string InvalidToken = "Invalid token";
    public const string NotAllowed = "User is not allowed";
    public const string CreateUserFailed = "Failed to create user";
    public const string TokenInBlackList = "Token is in black list";
    public const string EmailFailed = "Failed to send Email";


}