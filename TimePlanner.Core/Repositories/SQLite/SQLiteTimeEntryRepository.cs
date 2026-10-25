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

        //-----------------------------
        //saves a changed entry
        public async Task UpdateAsync(TimeEntry entry)
        {
            _db.ChangeTracker.Clear();
            _db.Entry(entry).State = EntityState.Modified;
            await _db.SaveChangesAsync();
        }

        //-----------------------------
        //one save for all of it, so an edit that moves several entries either happens completely or not at all
        public async Task ApplyAsync(IEnumerable<TimeEntry> changed, IEnumerable<int> removedIds, IEnumerable<TimeEntry> added)
        {
            _db.ChangeTracker.Clear();

            foreach (var entry in changed)
            {
                _db.Entry(entry).State = EntityState.Modified;
            }

            foreach (var id in removedIds)
            {
                _db.Entry(new TimeEntry { TimeEntryId = id }).State = EntityState.Deleted;
            }

            _db.TimeEntries.AddRange(added);
            await _db.SaveChangesAsync();
        }

        //-----------------------------
        //deletes an entry by id
        public async Task DeleteAsync(int id)
        {
            var entry = await _db.TimeEntries.FindAsync(id);

            //removes entry if present in database
            if (entry != null)
            {
                _db.TimeEntries.Remove(entry);
                await _db.SaveChangesAsync();
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
