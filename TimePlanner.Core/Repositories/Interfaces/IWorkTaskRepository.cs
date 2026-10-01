using System;
using System.Collections.Generic;
using System.Text;
using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Repositories.Interfaces
{
    public interface IWorkTaskRepository
    {
        Task<List<WorkTask>> GetActiveForUserAsync(int userId, int projectId, CancellationToken cancellationToken = default);
        Task<WorkTask> AddAsync(WorkTask task, CancellationToken cancellationToken = default);
    }
}
