using TimePlanner.Dashboard.Services.Users;

namespace TimePlanner.Dashboard.Models
{
    //-----------------------------
    //the admin users page: the people, the outcome of the last action, and a temporary password to show once
    public class UsersPageModel
    {
        public List<UserSummary> Users { get; set; } = new();
        public string? Message { get; set; }
        public string? Error { get; set; }

        //only set on the response to creating an account or resetting a password, it is not kept anywhere
        public string? TemporaryPassword { get; set; }
        public string? TemporaryFor { get; set; }
    }

    //-----------------------------
    //the settings page: who the person is, and the outcome of the last change
    public class SettingsPageModel
    {
        public AccountInfo Account { get; set; } = new("", "", "", false);
        public string? Message { get; set; }
        public string? Error { get; set; }
    }
}
//------------------------------EOF-----------------------------\\
