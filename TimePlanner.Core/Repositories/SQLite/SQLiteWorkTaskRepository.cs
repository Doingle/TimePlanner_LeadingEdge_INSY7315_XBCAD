using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Repositories.SQLite
{
    //-----------------------------
    //ef core implementation of IWorkTaskRepository
    public class SQLiteWorkTaskRepository : IWorkTaskRepository
    {
        private readonly AppDbContext _db;

        public SQLiteWorkTaskRepository(AppDbContext db) => _db = db;

        public Task<WorkTask?> GetByIdAsync(int id) =>
            _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.TaskID == id);

        public Task<List<WorkTask>> GetByProjectAsync(int projectId) =>
            _db.Tasks.AsNoTracking().Where(t => t.ProjectID == projectId).OrderBy(t => t.Name).ToListAsync();

        public Task<List<WorkTask>> GetActiveForUserAsync(int userId) =>
            _db.Tasks.AsNoTracking()
                .Where(t => t.AssignedUserID == userId && t.Status != WorkTaskStatus.Done)
                .OrderBy(t => t.Name)
                .ToListAsync();

        public async Task AddAsync(WorkTask task)
        {
            _db.Tasks.Add(task);
            await _db.SaveChangesAsync();
        }

        //-----------------------------
        //finds the task for a project activity and user
        public Task<WorkTask?> FindAsync(int projectId, int categoryId, int assignedUserId) =>
            _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t =>
                t.ProjectID == projectId && t.CategoryId == categoryId && t.AssignedUserID == assignedUserId);
    }
}
//------------------------------EOF-----------------------------\\
