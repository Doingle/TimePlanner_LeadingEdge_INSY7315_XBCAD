namespace TimePlanner.Core.Services.Models
{
    //-----------------------------
    //rename hide or show result with a message for the user
    public sealed record ListChangeOutcome(bool Ok, string Message)
    {
        public static ListChangeOutcome Done(string message) => new(true, message);

        public static ListChangeOutcome Refused(string message) => new(false, message);
    }
}
//------------------------------EOF-----------------------------\\
