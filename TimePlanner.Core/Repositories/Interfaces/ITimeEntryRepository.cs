using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Repositories.Interfaces
{
    public interface ITimeEntryRepository
    {
        Task AddRangeAsync(IEnumerable<TimeEntry> entries, CancellationToken cancellationToken = default);
        Task<List<TimeEntry>> GetForUserAsync(int userId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
        Task<List<TimeEntry>> GetLatestForUserAsync(int userId, int count, CancellationToken cancellationToken = default);
    }
}
