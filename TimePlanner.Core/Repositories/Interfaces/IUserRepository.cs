using System;
using System.Collections.Generic;
using System.Text;
using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Repositories.Interfaces
{
    public interface IUserRepository
    {
        Task<AppUser> GetOrCreateLocalUserAsync(string localAccountName, string displayName, CancellationToken cancellationToken = default);
        Task UpdateSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default);
    }
}
