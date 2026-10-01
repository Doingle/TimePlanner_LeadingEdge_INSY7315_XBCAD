using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Services
{
    //-----------------------------
    //reads and saves check in settings
    public class SettingsService
    {
        private readonly IAppUserRepository _users;

        public SettingsService(IAppUserRepository users)
        {
            _users = users;
        }

        //-----------------------------
        //loads saved user settings or default values
        public async Task<UserSettings> GetAsync(int userId)
        {
            var user = await _users.GetByIdAsync(userId);

            //a missing user cannot load settings
            if (user == null)
            {
                throw new ArgumentException("User does not exist.");
            }

            return user.Settings ?? new UserSettings { UserId = userId };
        }

        //-----------------------------
        //checks settings and saves valid ones
        public async Task SaveAsync(UserSettings settings)
        {
            Validate(settings);
            await _users.SaveSettingsAsync(settings);
        }

        //-----------------------------
        //checks range rules for all setting values
        public static void Validate(UserSettings s)
        {
            //if makes sure interval must stay between 5 and 480 minutes
            if (s.CheckInIntervalMinutes < 5 || s.CheckInIntervalMinutes > 480)
            {
                throw new ArgumentException("Check in interval must be between 5 and 480 minutes.", nameof(s));
            }

            //if makes sure snooze must stay between 1 and 60 minutes
            if (s.SnoozeMinutes < 1 || s.SnoozeMinutes > 60)
            {
                throw new ArgumentException("Snooze time must be between 1 and 60 minutes.", nameof(s));
            }

            //if makes sure max snoozes must be 0 to 10
            if (s.MaxSnoozes < 0 || s.MaxSnoozes > 10)
            {
                throw new ArgumentException("Max snoozes must be between 0 and 10.", nameof(s));
            }

            //if makes sure max skips per day must be 0 to 10
            if (s.MaxSkipsPerDay < 0 || s.MaxSkipsPerDay > 10)
            {
                throw new ArgumentException("Max skips per day must be between 0 and 10.", nameof(s));
            }

            //if makes sure daily goal must be 0.5 to 24 hours
            if (s.DailyGoalHours < 0.5 || s.DailyGoalHours > 24)
            {
                throw new ArgumentException("Daily goal must be between 0.5 and 24 hours.", nameof(s));
            }

            //if makes sure ignored check in minutes must be 1 to 60
            if (s.IgnoredCheckInMinutes < 1 || s.IgnoredCheckInMinutes > 60)
            {
                throw new ArgumentException("Ignored check in time must be between 1 and 60 minutes.", nameof(s));
            }

            //if makes sure action for ignored check in must be a known option
            if (!Enum.IsDefined(s.IgnoredCheckInAction))
            {
                throw new ArgumentException("Invalid ignored check in action.", nameof(s));
            }

            //if makes sure lunch start must be strictly before lunch end
            if (s.LunchStart >= s.LunchEnd)
            {
                throw new ArgumentException("Lunch start time must be before lunch end time.", nameof(s));
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
