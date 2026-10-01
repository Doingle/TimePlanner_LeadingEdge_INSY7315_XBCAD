namespace TimePlanner.Core.Services
{
    //-----------------------------
    //reads the windows system clock
    public sealed class SystemClock : IClock
    {
        public DateTime Now => DateTime.Now;
    }
}
//------------------------------EOF-----------------------------\\
