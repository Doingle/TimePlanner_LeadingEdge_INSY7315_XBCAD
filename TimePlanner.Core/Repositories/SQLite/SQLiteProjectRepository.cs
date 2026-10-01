using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Repositories.SQLite
{
    public class SQLiteProjectRepository : IProjectRepository
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public SQLiteProjectRepository(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }


        public async Task<List<Project>> GetActiveAsync(CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            return await context.Projects
                .AsNoTracking()
                .Include(p => p.Company)
                .Where(p => p.Status == ProjectStatus.Active)
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);
        }
    }
}
