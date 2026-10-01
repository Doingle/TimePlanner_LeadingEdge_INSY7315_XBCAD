using System;
using System.Collections.Generic;
using System.Text;
using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Repositories.Interfaces
{
    public interface IProjectRepository
    {
        Task<List<Project>> GetActiveAsync(CancellationToken cancellationToken = default);
    }
}
