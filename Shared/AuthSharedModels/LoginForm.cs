using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;
using System.ComponentModel.DataAnnotations;

public class LoginForm
{
    [Required(ErrorMessage = "יש להזין כתובת מייל")]
    [EmailAddress(ErrorMessage = "כתובת מייל לא תקינה")]
    public string Email { get; set; }

    [Required(ErrorMessage = "יש להזין סיסמה")]
    public string Password { get; set; }

}