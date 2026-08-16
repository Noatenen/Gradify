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

    /// <summary>
    /// "זכור אותי" — asks the server for a longer-lived token instead of the
    /// one-day default.
    ///
    /// <para>It carries no credential and changes nothing about how the session
    /// is stored: the token still goes to the same place, and logout still
    /// revokes it. Defaults to false, so an unchecked login is byte-for-byte the
    /// request that was sent before this option existed.</para>
    /// </summary>
    public bool RememberMe { get; set; }

}