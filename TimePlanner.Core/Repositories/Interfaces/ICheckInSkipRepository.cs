using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Repositories.Interfaces
{
    //-----------------------------
    //data access for skipped check ins
    public interface ICheckInSkipRepository
    {
        Task AddAsync(CheckInSkip skip);

        //skips the user made on one calendar day
        Task<int> CountForUserOnDayAsync(int userId, DateOnly day);

        //time of the newest skip in a day or null
        Task<DateTime?> GetLatestForSessionAsync(int daySessionId);
    }
}
//------------------------------EOF-----------------------------\\
