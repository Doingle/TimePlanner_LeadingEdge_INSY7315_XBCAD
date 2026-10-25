using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Repositories.Interfaces
{
    //-----------------------------
    //data access for companies
    public interface ICompanyRepository
    {
        Task<Company?> GetByIdAsync(int id);
        Task<List<Company>> GetAllAsync();
        Task AddAsync(Company company);

        //saves a changed company
        Task UpdateAsync(Company company);

        //deletes a company by id
        Task DeleteAsync(int id);
    }
}
//------------------------------EOF-----------------------------\\
