using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Repositories.SQLite
{
    //-----------------------------
    //ef core implementation of ICompanyRepository, every query is parameterised by ef
    public class SQLiteCompanyRepository : ICompanyRepository
    {
        private readonly AppDbContext _db;

        public SQLiteCompanyRepository(AppDbContext db) => _db = db;

        public Task<Company?> GetByIdAsync(int id) =>
            _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == id);

        public Task<List<Company>> GetAllAsync() =>
            _db.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync();

        public async Task AddAsync(Company company)
        {
            _db.Companies.Add(company);
            await _db.SaveChangesAsync();
        }
    }
}
//------------------------------EOF-----------------------------\\
