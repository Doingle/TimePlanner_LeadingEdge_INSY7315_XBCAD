namespace TimePlanner.Dashboard.Services.Users
{
    //-----------------------------
    //one person as the admin list shows them. Id is the login's id, AppUserId the time tracking profile the hours belong to
    public record UserSummary(string Id, int? AppUserId, string Name, string Email, string Role, bool IsActive,
        bool MustChangePassword, bool LockedOut, DateTime? LastLoginUtc);

    //-----------------------------
    //the outcome of an account operation: Success, or an Error that is safe to show to the person who asked
    public record OpResult(bool Success, string? Error = null)
    {
        public static OpResult Ok() => new(true);
        public static OpResult Fail(string error) => new(false, error);
    }

    //-----------------------------
    //the outcome of creating an account or resetting a password. The temporary password is only ever returned here, once, and is never stored in readable form or logged
    public record PasswordResult(bool Success, string? Error, UserSummary? User, string? TemporaryPassword)
    {
        public static PasswordResult Fail(string error) => new(false, error, null, null);
    }

    //-----------------------------
    //what a person sees about their own account
    public record AccountInfo(string Name, string Email, string Role, bool MustChangePassword);
}
//------------------------------EOF-----------------------------\\
