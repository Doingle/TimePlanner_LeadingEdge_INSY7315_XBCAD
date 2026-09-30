using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Repositories.Interfaces
{
    //-----------------------------
    //data access for tasks
    public interface IWorkTaskRepository
    {
        Task<WorkTask?> GetByIdAsync(int id);
        Task<List<WorkTask>> GetByProjectAsync(int projectId);

        //tasks assigned to the user that are not Done, used by the check in task picker
        Task<List<WorkTask>> GetActiveForUserAsync(int userId);
        Task AddAsync(WorkTask task);

        //the task for one project activity and user
        Task<WorkTask?> FindAsync(int projectId, int categoryId, int assignedUserId);
    }
}
//------------------------------EOF-----------------------------\\
