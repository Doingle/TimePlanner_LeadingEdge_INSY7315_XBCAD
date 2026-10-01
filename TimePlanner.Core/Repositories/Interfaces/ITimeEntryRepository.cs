using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Repositories.Interfaces
{
    //-----------------------------
    //data access for time entries
    public interface ITimeEntryRepository
    {
        Task<TimeEntry?> GetByIdAsync(int id);

        //entries for one user that fall completely inside from and to, oldest first, includes the task and project
        Task<List<TimeEntry>> GetForUserAsync(int userId, DateTime from, DateTime to);
        Task AddAsync(TimeEntry entry);

        //saves every entry in a single transaction, nothing is stored if one fails
        Task AddRangeAsync(IEnumerable<TimeEntry> entries);

        //newest entry for the user with their task
        Task<TimeEntry?> GetLatestForUserAsync(int userId);

        //activity ids from newest entries with no duplicates
        Task<List<int>> GetRecentCategoryIdsAsync(int userId, int count);
    }
}
//------------------------------EOF-----------------------------\\
