namespace AuthWithAdmin.Shared.AuthSharedModels;
using System.ComponentModel.DataAnnotations;

public class UserAddedByAdmin
{
    //משתמש שאדמין הוסיף
    public int Id { get; set; }

    [Required(ErrorMessage = "יש להזין שם")]
    public string FirstName { get; set; }

    [Required(ErrorMessage = "יש להזין שם")]
    public string LastName { get; set; }


    [Required(ErrorMessage = "יש להזין כתובת מייל")]
    [EmailAddress(ErrorMessage = "כתובת מייל לא תקינה")]
    public string Email { get; set; }
 
}