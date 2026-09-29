using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Repositories.SQLite
{
    //-----------------------------
    //ef core implementation of IAppUserRepository
    public class SQLiteAppUserRepository : IAppUserRepository
    {
        private readonly AppDbContext _db;

        public SQLiteAppUserRepository(AppDbContext db) => _db = db;

        public Task<AppUser?> GetByIdAsync(int id) =>
            _db.Users.AsNoTracking().Include(u => u.Settings).FirstOrDefaultAsync(u => u.UserId == id);

        public Task<AppUser?> GetByEmailAsync(string email) =>
            _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);

        public Task<AppUser?> GetByLocalAccountNameAsync(string localAccountName) =>
            _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.LocalAccountName == localAccountName);

        public async Task AddAsync(AppUser user)
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
        }
    }
}
//------------------------------EOF-----------------------------\\
