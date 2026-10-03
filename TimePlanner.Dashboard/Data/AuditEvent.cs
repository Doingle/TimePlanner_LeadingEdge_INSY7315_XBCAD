namespace TimePlanner.Dashboard.Data
{
    //-----------------------------
    //one security relevant thing that happened: who did what, when and from where. Rows are only ever added.
    //passwords and tokens are never written here
    public class AuditEvent
    {
        public int Id { get; set; }
        public DateTime TimestampUtc { get; set; }

        //the identity id and email of the person, for a failed sign in the email is whatever was typed
        public string? UserId { get; set; }
        public string? Email { get; set; }

        public string Action { get; set; } = string.Empty;
        public string? Detail { get; set; }
        public string? IpAddress { get; set; }
    }

    //-----------------------------
    //the actions that get recorded
    public static class AuditActions
    {
        public const string LoginSucceeded = "LoginSucceeded";
        public const string LoginFailed = "LoginFailed";
        public const string LoginLockedOut = "LoginLockedOut";
        public const string LoginBlocked = "LoginBlocked";
        public const string Logout = "Logout";
        public const string RefreshFailed = "RefreshFailed";
        public const string RefreshTokenReuse = "RefreshTokenReuse";
        public const string UserCreated = "UserCreated";
        public const string PasswordReset = "PasswordReset";
        public const string UserDeactivated = "UserDeactivated";
        public const string UserReactivated = "UserReactivated";
        public const string PasswordChanged = "PasswordChanged";
        public const string ProfileUpdated = "ProfileUpdated";
        public const string TimesheetImported = "TimesheetImported";
        public const string TimesheetImportRejected = "TimesheetImportRejected";
        public const string TimesheetExported = "TimesheetExported";
    }
}
//------------------------------EOF-----------------------------\\
