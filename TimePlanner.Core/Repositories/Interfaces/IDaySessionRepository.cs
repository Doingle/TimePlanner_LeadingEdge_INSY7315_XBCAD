using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Repositories.Interfaces
{
    //-----------------------------
    //data access for tracked days and their pauses
    public interface IDaySessionRepository
    {
        //the open day with pauses or null
        Task<DaySession?> GetOpenAsync(int userId);

        //days overlapping the window with pauses
        Task<List<DaySession>> GetForUserBetweenAsync(int userId, DateTime from, DateTime to);
        Task AddAsync(DaySession session);

        //saves the day and any added or changed pauses
        Task UpdateAsync(DaySession session);
    }
}
//------------------------------EOF-----------------------------\\
