using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Data;

public class UserFromDB
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public List<string> Roles { get; set; } = new List<string>();

    // Extended profile fields
    public string Phone { get; set; } = string.Empty;
    public string AcademicYear { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public UserForAdmin MapUserToAdmin()
    {
        return new UserForAdmin()
        {
            Id = Id,
            Email = Email,
            FirstName = FirstName,
            LastName = LastName,
            RegisterDate = DateTime.UtcNow,
            Roles = Roles,
            Phone = Phone,
            AcademicYear = AcademicYear,
            IsActive = IsActive
        };
    }

    public User MapUser()
    {
        return new User() { Id = Id, Email = Email, FirstName = FirstName, LastName = LastName, Roles = Roles, IsVerified = IsVerified };
    }
}