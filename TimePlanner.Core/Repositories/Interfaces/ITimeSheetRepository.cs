using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Repositories.Interfaces
{
    //-----------------------------
    //data access for timesheets
    public interface ITimeSheetRepository
    {
        Task<TimeSheet?> GetByIdAsync(int id);
        Task<List<TimeSheet>> GetForUserAsync(int userId);
        Task AddAsync(TimeSheet sheet);
    }
}
//------------------------------EOF-----------------------------\\
