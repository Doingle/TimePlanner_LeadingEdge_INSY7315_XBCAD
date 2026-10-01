using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Repositories.Interfaces
{
    //-----------------------------
    //data access for projects
    public interface IProjectRepository
    {
        Task<Project?> GetByIdAsync(int id);
        Task<List<Project>> GetByCompanyAsync(int companyId);
        Task<List<Project>> GetAllAsync();
        Task AddAsync(Project project);

        //active projects with their company
        Task<List<Project>> GetActiveAsync();
    }
}
//------------------------------EOF-----------------------------\\
