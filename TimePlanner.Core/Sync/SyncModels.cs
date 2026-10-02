namespace TimePlanner.Core.Sync
{
    //-----------------------------
    //one entry as the dashboard import expects it
    public sealed record DayEntryPayload(
        string Company,
        string Project,
        string Activity,
        DateTime Start,
        DateTime End,
        string? Note,
        string Method);

    //-----------------------------
    //a saved access token for the dashboard api
    public sealed record StoredToken(string AccessToken, DateTime ExpiresAtUtc, string Email);

    //-----------------------------
    //one day the user has sent
    public sealed record SentDay(DateOnly Day, DateTime SentAt, int Entries, double Hours);

    //-----------------------------
    //exactly what a send would upload for one day
    public sealed record DayPreview(DateOnly Day, IReadOnlyList<DayEntryPayload> Rows, double Hours, bool CanSend);

    //-----------------------------
    //how a sign in attempt ended
    public enum SignInStatus
    {
        SignedIn,
        InvalidDetails,
        PasswordChangeRequired,
        Offline,
        Failed
    }

    //-----------------------------
    //result of a sign in with an optional token and message
    public sealed record SignInOutcome(SignInStatus Status, StoredToken? Token, string? Message);

    //-----------------------------
    //how a send attempt ended
    public enum SendStatus
    {
        Sent,
        NeedsSignIn,
        NotEnded,
        NothingToSend,
        Rejected,
        Offline,
        Failed
    }

    //-----------------------------
    //result of a send with stored count and any row errors
    public sealed record SendOutcome(SendStatus Status, int Created, IReadOnlyList<string> Errors, string? Message)
    {
        //-----------------------------
        //an outcome with only a status and message
        public static SendOutcome Of(SendStatus status, string? message) => new(status, 0, Array.Empty<string>(), message);
    }
}
//------------------------------EOF-----------------------------\\
