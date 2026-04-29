using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;
using System.ComponentModel.DataAnnotations;

public class UserResetPassword
{

    public int Id {get; set;}
    
    [Required(ErrorMessage = "יש להזין סיסמה")]
    [MinLength(6, ErrorMessage = "הסיסמה חייבת להכיל לפחות 6 תווים")]
    public string NewPassword { get; set; }
    
    [Required(ErrorMessage = "אימות הסיסמה נדרש")]
    [Compare("NewPassword", ErrorMessage = "הסיסמאות אינן תואמות")]
    public string ConfirmPassword { get; set; }
}