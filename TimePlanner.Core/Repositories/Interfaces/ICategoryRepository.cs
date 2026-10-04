using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Repositories.Interfaces
{
    //-----------------------------
    //data access for activities
    public interface ICategoryRepository
    {
        //every activity at every level
        Task<List<Category>> GetAllAsync();
        Task<Category?> GetByIdAsync(int id);
        Task AddAsync(Category category);

        //saves a changed activity
        Task UpdateAsync(Category category);

        //deletes an activity by id
        Task DeleteAsync(int id);
    }
}
//------------------------------EOF-----------------------------\\
