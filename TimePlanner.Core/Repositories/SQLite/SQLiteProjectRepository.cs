using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Repositories.SQLite
{
    //-----------------------------
    //ef core implementation of IProjectRepository
    public class SQLiteProjectRepository : IProjectRepository
    {
        private readonly AppDbContext _db;

        public SQLiteProjectRepository(AppDbContext db) => _db = db;

        public Task<Project?> GetByIdAsync(int id) =>
            _db.Projects.AsNoTracking().Include(p => p.Company).FirstOrDefaultAsync(p => p.ProjectID == id);

        public Task<List<Project>> GetByCompanyAsync(int companyId) =>
            _db.Projects.AsNoTracking().Where(p => p.CompanyId == companyId).OrderBy(p => p.Name).ToListAsync();

        public Task<List<Project>> GetAllAsync() =>
            _db.Projects.AsNoTracking().Include(p => p.Company).OrderBy(p => p.Name).ToListAsync();

        public async Task AddAsync(Project project)
        {
            _db.Projects.Add(project);
            await _db.SaveChangesAsync();
        }

        //-----------------------------
        //active projects sorted by company then name
        public Task<List<Project>> GetActiveAsync() =>
            _db.Projects.AsNoTracking()
                .Include(p => p.Company)
                .Where(p => p.Status == ProjectStatus.Active)
                .OrderBy(p => p.Company!.Name).ThenBy(p => p.Name)
                .ToListAsync();

        //-----------------------------
        //saves a changed project
        public async Task UpdateAsync(Project project)
        {
            _db.ChangeTracker.Clear();
            _db.Entry(project).State = EntityState.Modified;
            await _db.SaveChangesAsync();
        }

        //-----------------------------
        //deletes a project by id
        public async Task DeleteAsync(int id)
        {
            var item = await _db.Projects.FindAsync(id);

            //removes the project when found
            if (item != null)
            {
                _db.Projects.Remove(item);
                await _db.SaveChangesAsync();
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
