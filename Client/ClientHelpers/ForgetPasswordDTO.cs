using System.ComponentModel.DataAnnotations;


namespace AuthWithAdmin.Client.ClientHelpers
{
    public class ForgetPasswordDTO
    {
        [Required(ErrorMessage = "יש להזין כתובת מייל")]
        [EmailAddress(ErrorMessage = "כתובת מייל לא תקינה")]
        public string Email { get; set; }
    }
}
