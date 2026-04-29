using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

public class AdminUpdateUserRequest
{
    public string FirstName    { get; set; } = "";
    public string LastName     { get; set; } = "";
    public string Phone        { get; set; } = "";
    public string AcademicYear { get; set; } = "";
    public string Role         { get; set; } = "";
}
