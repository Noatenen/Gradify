namespace AuthWithAdmin.Server.AuthHelpers;
public class MinimalUser
{
    private string _email = string.Empty;
    public string Email 
    { 
        get => _email;
        set => _email = (value ?? string.Empty).Trim().ToLower();
    }   
    public string PasswordHash { get; set; }

}