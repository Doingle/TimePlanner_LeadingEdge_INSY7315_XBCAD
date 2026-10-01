using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Repositories.SQLite
{
    //-----------------------------
    //ef core implementation of ITimeEntryRepository
    public class SQLiteTimeEntryRepository : ITimeEntryRepository
    {
        private readonly AppDbContext _db;

        public SQLiteTimeEntryRepository(AppDbContext db) => _db = db;

        public Task<TimeEntry?> GetByIdAsync(int id) =>
            _db.TimeEntries.AsNoTracking().FirstOrDefaultAsync(e => e.TimeEntryId == id);

        public Task<List<TimeEntry>> GetForUserAsync(int userId, DateTime from, DateTime to) =>
            _db.TimeEntries.AsNoTracking()
                .Include(e => e.Task).ThenInclude(t => t!.Project)
                .Where(e => e.UserId == userId && e.StartTime >= from && e.EndTime <= to)
                .OrderBy(e => e.StartTime)
                .ToListAsync();

        public async Task AddAsync(TimeEntry entry)
        {
            _db.TimeEntries.Add(entry);
            await _db.SaveChangesAsync();
        }

        //ef calls in single transaction
        public async Task AddRangeAsync(IEnumerable<TimeEntry> entries)
        {
            _db.TimeEntries.AddRange(entries);
            await _db.SaveChangesAsync();
        }

        //-----------------------------
        //newest entry by end time with respective task
        public Task<TimeEntry?> GetLatestForUserAsync(int userId) =>
            _db.TimeEntries.AsNoTracking()
                .Include(e => e.Task)
                .Where(e => e.UserId == userId)
                .OrderByDescending(e => e.EndTime)
                .FirstOrDefaultAsync();

        //-----------------------------
        //newest activity ids for the user
        public async Task<List<int>> GetRecentCategoryIdsAsync(int userId, int count)
        {
            var ids = await _db.TimeEntries.AsNoTracking()
                .Where(e => e.UserId == userId)
                .OrderByDescending(e => e.EndTime)
                .Select(e => e.Task!.CategoryId)
                .Take(200)
                .ToListAsync();

            return ids.Distinct().Take(count).ToList();
        }
    }
}
//------------------------------EOF-----------------------------\\
