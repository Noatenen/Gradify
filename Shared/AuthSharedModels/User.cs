using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

public class User
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public List<string> Roles { get; set; }
    public bool IsVerified { get; set; }
}