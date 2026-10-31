using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Services.Models;

namespace TimePlanner.Core.Services
{
    //-----------------------------
    //the rules for moving from one day to the next
    //a day belongs to the date it started on, only that date can resume it, and a day left open overnight closes at its last activity
    public static class DayRules
    {
        //-----------------------------
        //the date a day belongs to
        public static DateOnly DateOf(DaySession day) => DateOnly.FromDateTime(day.StartedAt);

        //-----------------------------
        //what today looks like from the open day and the last ended one
        public static DayState StateOn(DateTime now, DaySession? open, DaySession? lastEnded)
        {
            var today = DateOnly.FromDateTime(now);

            //an open day from today is tracking or on a break
            if (open != null && DateOf(open) == today)
            {
                return open.Pauses.Any(p => p.EndedAt == null) ? DayState.Paused : DayState.Tracking;
            }

            //a day from today that has ended
            if (lastEnded?.EndedAt != null && DateOf(lastEnded) == today)
            {
                return DayState.Ended;
            }

            return DayState.NotStarted;
        }

        //-----------------------------
        //only a day from today can be resumed, an earlier one stays ended and today starts fresh
        public static bool CanResume(DaySession? ended, DateTime now) =>
            ended?.EndedAt != null && DateOf(ended) == DateOnly.FromDateTime(now);

        //-----------------------------
        //a day still open from an earlier date was left open overnight
        public static bool IsLeftOpen(DaySession? open, DateTime now) =>
            open != null && open.EndedAt == null && DateOf(open) < DateOnly.FromDateTime(now);

        //-----------------------------
        //the last moment anything happened in a day: its start, its last entry or a break
        public static DateTime LastActivity(DaySession day, DateTime? lastEntryEnd)
        {
            var last = day.StartedAt;

            //a later entry moves the end forward
            if (lastEntryEnd > last)
            {
                last = lastEntryEnd.Value;
            }

            //break times count as activity too
            foreach (var pause in day.Pauses)
            {
                var pauseTime = pause.EndedAt ?? pause.StartedAt;

                //keep the latest time seen
                if (pauseTime > last)
                {
                    last = pauseTime;
                }
            }

            return last;
        }

        //-----------------------------
        //the latest earlier day with logged time, when it has never been sent
        //older days are left alone: once the latest one is sent the reminder stops
        public static DateOnly? UnsentEarlierDay(IEnumerable<DateOnly> daysWithTime, IEnumerable<DateOnly> sentDays, DateOnly today)
        {
            var earlier = daysWithTime.Where(d => d < today).ToList();

            //nothing logged before today
            if (earlier.Count == 0)
            {
                return null;
            }

            var latest = earlier.Max();
            return sentDays.Contains(latest) ? null : latest;
        }
    }
}
//------------------------------EOF-----------------------------\\
