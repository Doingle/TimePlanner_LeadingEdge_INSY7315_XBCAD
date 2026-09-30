using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Repositories.Interfaces
{
    //-----------------------------
    //data access for users
    public interface IAppUserRepository
    {
        Task<AppUser?> GetByIdAsync(int id);
        Task<AppUser?> GetByEmailAsync(string email);
        Task<AppUser?> GetByLocalAccountNameAsync(string localAccountName);
        Task AddAsync(AppUser user);

        //adds or updates one settings row
        Task SaveSettingsAsync(UserSettings settings);
    }
}
//------------------------------EOF-----------------------------\\
