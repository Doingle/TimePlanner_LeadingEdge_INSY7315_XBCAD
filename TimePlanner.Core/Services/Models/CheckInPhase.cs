namespace TimePlanner.Core.Services.Models
{
    //-----------------------------
    //what the widget should show right now
    public enum CheckInPhase
    {
        NotTracking,
        Paused,
        Waiting,
        Due,
        Snoozed,

        //a day started by hand on a day that isn't a working day: tracked, but no check ins
        DayOff
    }
}
//------------------------------EOF-----------------------------\\
