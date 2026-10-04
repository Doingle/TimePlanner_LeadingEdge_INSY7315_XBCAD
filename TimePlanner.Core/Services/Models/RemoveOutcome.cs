namespace TimePlanner.Core.Services.Models
{
    //-----------------------------
    //what removing an item did
    public enum RemoveResult
    {
        Deleted,
        Hidden,
        NotAllowed
    }

    //-----------------------------
    //removal result with a message for the user
    public sealed record RemoveOutcome(RemoveResult Result, string Message);
}
//------------------------------EOF-----------------------------\\
