namespace TimePlanner.Core.Services.Models
{
    //-----------------------------
    //time since the last entry still to be logged
    public sealed record UnloggedSpan(DateTime Start, DateTime End, TimeSpan PausedTime)
    {
        //working time with breaks removed
        public TimeSpan WorkedTime => End - Start - PausedTime;
    }
}
//------------------------------EOF-----------------------------\\
