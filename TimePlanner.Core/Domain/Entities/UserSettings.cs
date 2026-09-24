namespace TimePlanner.Core.Domain.Entities
{
    //-----------------------------
    //this class holds one users check in preferences new instances will carry the agreed default values
    public class UserSettings
    {
        public int UserSettingsId { get; set; }

        public int UserId { get; set; }
        public AppUser? User { get; set; }

        //minutes between scheduled check in prompts
        public int CheckInIntervalMinutes { get; set; } = 30;

        //minutes a snoozed check in is delayed by
        public int SnoozeMinutes { get; set; } = 10;

        //the snooze limit enforced by the check in engine
        public int MaxSnoozes { get; set; } = 3;

        //no check ins are prompted between LunchStart and LunchEnd
        public TimeOnly LunchStart { get; set; } = new TimeOnly(12, 0);
        public TimeOnly LunchEnd { get; set; } = new TimeOnly(13, 0);

        //check ins are only prompted between WorkdayStart and WorkdayEnd
        public TimeOnly WorkdayStart { get; set; } = new TimeOnly(8, 0);
        public TimeOnly WorkdayEnd { get; set; } = new TimeOnly(17, 0);
    }
}
//------------------------------EOF-----------------------------\\
