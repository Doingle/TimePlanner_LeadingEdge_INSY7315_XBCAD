using TimePlanner.Core.Domain.Enums;

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
        public int CheckInIntervalMinutes { get; set; } = 90;

        //minutes a snoozed check in is delayed by
        public int SnoozeMinutes { get; set; } = 10;

        //the snooze limit enforced by the check in engine
        public int MaxSnoozes { get; set; } = 3;

        //skips allowed per day before check ins become required, separate from snoozes
        public int MaxSkipsPerDay { get; set; } = 3;

        //hours of logged work the user aims for each day
        public double DailyGoalHours { get; set; } = 8;

        //minutes an unanswered check in waits before IgnoredCheckInAction is applied
        public int IgnoredCheckInMinutes { get; set; } = 5;
        public IgnoredCheckInAction IgnoredCheckInAction { get; set; } = IgnoredCheckInAction.KeepAsking;

        //no check ins are prompted between LunchStart and LunchEnd
        public TimeOnly LunchStart { get; set; } = new TimeOnly(12, 0);
        public TimeOnly LunchEnd { get; set; } = new TimeOnly(13, 0);
    }
}
//------------------------------EOF-----------------------------\\
