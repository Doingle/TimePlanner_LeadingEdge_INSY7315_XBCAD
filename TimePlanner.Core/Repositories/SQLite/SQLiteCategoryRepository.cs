using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Repositories.SQLite
{
    //-----------------------------
    //ef core implementation of ICategoryRepository
    public class SQLiteCategoryRepository : ICategoryRepository
    {
        private readonly AppDbContext _db;

        public SQLiteCategoryRepository(AppDbContext db) => _db = db;

        //-----------------------------
        //every activity at every level
        public Task<List<Category>> GetAllAsync() => _db.Categories.AsNoTracking().ToListAsync();

        //-----------------------------
        //one activity by id
        public Task<Category?> GetByIdAsync(int id) =>
            _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.CategoryId == id);

        //-----------------------------
        //adds a new activity
        public async Task AddAsync(Category category)
        {
            _db.Categories.Add(category);
            await _db.SaveChangesAsync();
        }

        //-----------------------------
        //saves a changed activity
        public async Task UpdateAsync(Category category)
        {
            _db.ChangeTracker.Clear();
            _db.Entry(category).State = EntityState.Modified;
            await _db.SaveChangesAsync();
        }

        //-----------------------------
        //deletes an activity by id
        public async Task DeleteAsync(int id)
        {
            var item = await _db.Categories.FindAsync(id);

            //removes the activity when found
            if (item != null)
            {
                _db.Categories.Remove(item);
                await _db.SaveChangesAsync();
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
