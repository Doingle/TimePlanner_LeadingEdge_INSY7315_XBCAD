using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Repositories.SQLite
{
    //-----------------------------
    //ef core implementation of ICheckInSkipRepository
    public class SQLiteCheckInSkipRepository : ICheckInSkipRepository
    {
        private readonly AppDbContext _db;

        public SQLiteCheckInSkipRepository(AppDbContext db) => _db = db;

        //-----------------------------
        //adds a new skip
        public async Task AddAsync(CheckInSkip skip)
        {
            _db.CheckInSkips.Add(skip);
            await _db.SaveChangesAsync();
        }

        //-----------------------------
        //counts skips for user on one calendar day
        public Task<int> CountForUserOnDayAsync(int userId, DateOnly day)
        {
            var start = day.ToDateTime(TimeOnly.MinValue);
            var end = day.AddDays(1).ToDateTime(TimeOnly.MinValue);

            return _db.CheckInSkips.AsNoTracking()
                .CountAsync(s => s.DaySession!.UserId == userId && s.SkippedAt >= start && s.SkippedAt < end);
        }

        //-----------------------------
        //newest skip time for the session or null
        public Task<DateTime?> GetLatestForSessionAsync(int daySessionId) =>
            _db.CheckInSkips.AsNoTracking()
                .Where(s => s.DaySessionId == daySessionId)
                .Select(s => (DateTime?)s.SkippedAt)
                .MaxAsync();
    }
}
//------------------------------EOF-----------------------------\\
