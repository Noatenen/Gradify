using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.AuthHelpers
{
    public class UserAuthResult
    {
        public User User { get; set; }
        public string Result { get; set; }

    }
}
