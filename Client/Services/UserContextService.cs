using AuthWithAdmin.Client.Models;

namespace AuthWithAdmin.Client.Services;

public class UserContextService
{
    public UserContext CurrentUser { get; private set; } = new UserContext
    {
        FullName = "שם משתמש",
        Role = "Admin"
    };

    private static readonly Dictionary<string, string> _roleSubtitles = new()
    {
        { "Admin",    "צוות הקורס" },
        { "Lecturer", "מרצה"       },
        { "Mentor",   "מנחה"       },
        { "Student",  "סטודנט"     }
    };

    public string GetRoleSubtitle() =>
        _roleSubtitles.TryGetValue(CurrentUser.Role, out var subtitle) ? subtitle : "";
}
