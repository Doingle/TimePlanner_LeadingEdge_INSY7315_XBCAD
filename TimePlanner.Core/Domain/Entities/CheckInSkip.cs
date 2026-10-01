namespace TimePlanner.Core.Domain.Entities
{
    //-----------------------------
    //one skipped check in inside a day
    public class CheckInSkip
    {
        public int CheckInSkipId { get; set; }

        public int DaySessionId { get; set; }
        public DaySession? DaySession { get; set; }

        public DateTime SkippedAt { get; set; }
    }
}
//------------------------------EOF-----------------------------\\
