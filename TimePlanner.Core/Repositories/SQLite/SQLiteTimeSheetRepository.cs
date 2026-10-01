using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Repositories.SQLite
{
    //-----------------------------
    //ef core implementation of ITimeSheetRepository
    public class SQLiteTimeSheetRepository : ITimeSheetRepository
    {
        private readonly AppDbContext _db;

        public SQLiteTimeSheetRepository(AppDbContext db) => _db = db;

        public Task<TimeSheet?> GetByIdAsync(int id) =>
            _db.TimeSheets.AsNoTracking().FirstOrDefaultAsync(s => s.TimeSheetId == id);

        public Task<List<TimeSheet>> GetForUserAsync(int userId) =>
            _db.TimeSheets.AsNoTracking().Where(s => s.UserId == userId).OrderByDescending(s => s.PeriodStart).ToListAsync();

        public async Task AddAsync(TimeSheet sheet)
        {
            _db.TimeSheets.Add(sheet);
            await _db.SaveChangesAsync();
        }
    }
}
//------------------------------EOF-----------------------------\\
