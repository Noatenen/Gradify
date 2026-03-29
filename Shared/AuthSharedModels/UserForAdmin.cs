
namespace AuthWithAdmin.Shared.AuthSharedModels
{
    public class UserForAdmin
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public DateTime RegisterDate { get; set; }
        public List<string> Roles { get; set; }

        // Extended profile fields — added via DatabaseMigrator
        public string Phone { get; set; } = string.Empty;
        public string AcademicYear { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
