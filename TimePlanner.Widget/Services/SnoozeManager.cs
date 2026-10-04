using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Services
{
    /// <summary>
    /// Snoozing puts a check-in off for a few minutes (SnoozeMinutes in the user's settings), up to
    /// MaxSnoozes times for each check-in. Core's check-in engine keeps the count; this is what the
    /// screens read from it.
    /// </summary>
    public sealed class SnoozeManager(CheckInScheduler scheduler, WidgetSession session)
    {
        /// <summary>When the snoozed check-in shows again.</summary>
        public DateTime? SnoozedUntil => scheduler.Status.SnoozedUntil;

        public int Left => scheduler.Status.SnoozesLeft;

        public int Minutes => session.Settings.SnoozeMinutes;

        /// <summary>Snoozes the check-in that is due. False when it cannot be snoozed, such as with none left.</summary>
        public async Task<bool> SnoozeAsync()
        {
            var before = scheduler.Status.SnoozesLeft;
            await scheduler.SnoozeAsync();
            return scheduler.IsSnoozed && scheduler.Status.SnoozesLeft < before;
        }
    }
}
