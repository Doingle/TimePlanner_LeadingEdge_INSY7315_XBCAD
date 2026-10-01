namespace TimePlanner.Core.Domain.Entities
{
    //-----------------------------
    //this class represents one pause inside a day session, the time between StartedAt and EndedAt is break time
    public class SessionPause
    {
        public int SessionPauseId { get; set; }

        public int DaySessionId { get; set; }
        public DaySession? DaySession { get; set; }

        public DateTime StartedAt { get; set; }

        //null while the user is still paused
        public DateTime? EndedAt { get; set; }
    }
}
//------------------------------EOF-----------------------------\\
