using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Repositories.SQLite
{
    public class SQLiteWorkTaskRepository : IWorkTaskRepository
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public SQLiteWorkTaskRepository(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }


        public async Task<List<WorkTask>> GetActiveForUserAsync(int userId, int projectId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            return await context.Tasks
                .AsNoTracking()
                .Where(t => t.AssignedUserID == userId && t.ProjectID == projectId && t.Status != WorkTaskStatus.Done)
                .OrderBy(t => t.TaskID)
                .ToListAsync(cancellationToken);
        }


        public async Task<WorkTask> AddAsync(WorkTask task, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            context.Tasks.Add(task);
            await context.SaveChangesAsync(cancellationToken);
            return task;
        }
    }
}
