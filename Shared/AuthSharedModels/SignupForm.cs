namespace AuthWithAdmin.Shared.AuthSharedModels;
using System.ComponentModel.DataAnnotations;

public class SignupForm
{
    //משתמש שנרשם לבד
    [Required(ErrorMessage = "יש להזין שם")]
    public string FirstName { get; set; }

    [Required(ErrorMessage = "יש להזין שם משפחה")]
    public string LastName { get; set; }


    [Required(ErrorMessage = "יש להזין כתובת מייל")]
    [EmailAddress(ErrorMessage = "כתובת מייל לא תקינה")]
    public string Email { get; set; }

    [Required(ErrorMessage = "יש להזין סיסמה")]
    [MinLength(6, ErrorMessage = "הסיסמה חייבת להכיל לפחות 6 תווים")]
    public string Password { get; set; }

    [Required(ErrorMessage = "אימות הסיסמה נדרש")]
    [Compare("Password", ErrorMessage = "הסיסמאות אינן תואמות")]
    public string ConfirmPassword { get; set; }

}