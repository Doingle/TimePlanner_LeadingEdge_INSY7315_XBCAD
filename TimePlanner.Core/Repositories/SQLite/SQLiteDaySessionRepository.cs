using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Repositories.SQLite
{
    //-----------------------------
    //ef core implementation of IDaySessionRepository
    public class SQLiteDaySessionRepository : IDaySessionRepository
    {
        private readonly AppDbContext _db;

        public SQLiteDaySessionRepository(AppDbContext db) => _db = db;

        //-----------------------------
        //open day for the user with its pauses
        public Task<DaySession?> GetOpenAsync(int userId) =>
            _db.DaySessions.AsNoTracking()
                .Include(s => s.Pauses)
                .FirstOrDefaultAsync(s => s.UserId == userId && s.EndedAt == null);

        //-----------------------------
        //days that overlap with pauses
        public Task<List<DaySession>> GetForUserBetweenAsync(int userId, DateTime from, DateTime to) =>
            _db.DaySessions.AsNoTracking()
                .Include(s => s.Pauses)
                .Where(s => s.UserId == userId && s.StartedAt < to && (s.EndedAt == null || s.EndedAt > from))
                .OrderBy(s => s.StartedAt)
                .ToListAsync();

        //-----------------------------
        //adds a new day
        public async Task AddAsync(DaySession session)
        {
            _db.DaySessions.Add(session);
            await _db.SaveChangesAsync();
        }

        //-----------------------------
        //new pauses inserted, known pauses updated
        public async Task UpdateAsync(DaySession session)
        {
            //instant save to not clear 
            _db.ChangeTracker.Clear();
            _db.DaySessions.Update(session);
            await _db.SaveChangesAsync();
        }
    }
}
//------------------------------EOF-----------------------------\\
