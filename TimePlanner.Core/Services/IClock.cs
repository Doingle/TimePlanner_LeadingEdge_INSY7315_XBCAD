namespace TimePlanner.Core.Services
{
    //-----------------------------
    //current local time so tests can control it
    public interface IClock
    {
        DateTime Now { get; }
    }
}
//------------------------------EOF-----------------------------\\
