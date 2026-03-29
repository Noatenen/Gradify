namespace AuthWithAdmin.Shared.AuthSharedModels;

public class UserRole
{
    public int UserId { get; set; }
    public string Role { get; set; }
    public bool Enable {get;set;}
}