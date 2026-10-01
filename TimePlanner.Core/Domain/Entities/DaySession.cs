namespace TimePlanner.Core.Domain.Entities
{
    //-----------------------------
    //this class represents one tracked working day which is opened by start tracking btn and closed by end day btn
    public class DaySession
    {
        public int DaySessionId { get; set; }

        public int UserId { get; set; }
        public AppUser? User { get; set; }

        public DateTime StartedAt { get; set; }

        //null while the day is still being tracked
        public DateTime? EndedAt { get; set; }

        //the breaks taken during this day where break time never gets logged as an entry
        public ICollection<SessionPause> Pauses { get; set; } = new List<SessionPause>();
    }
}
//------------------------------EOF-----------------------------\\
