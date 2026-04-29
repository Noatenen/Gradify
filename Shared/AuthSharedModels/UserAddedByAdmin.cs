using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;
using System.ComponentModel.DataAnnotations;

public class UserAddedByAdmin
{
    //משתמש שאדמין הוסיף
    public int Id { get; set; }

    [Required(ErrorMessage = "יש להזין שם פרטי")]
    public string FirstName { get; set; }

    [Required(ErrorMessage = "יש להזין שם משפחה")]
    public string LastName { get; set; }

    [Required(ErrorMessage = "יש להזין כתובת מייל")]
    [EmailAddress(ErrorMessage = "כתובת מייל לא תקינה")]
    public string Email { get; set; }

    public string Phone        { get; set; } = "";
    public string AcademicYear { get; set; } = "2025-2026";

    [Required(ErrorMessage = "יש לבחור תפקיד")]
    public string Role         { get; set; } = "Student";
}