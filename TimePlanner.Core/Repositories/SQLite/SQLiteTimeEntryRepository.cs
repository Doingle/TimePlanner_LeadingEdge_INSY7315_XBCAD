using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Repositories.SQLite
{
    public class SQLiteTimeEntryRepository : ITimeEntryRepository
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public SQLiteTimeEntryRepository(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }


        public async Task AddRangeAsync(IEnumerable<TimeEntry> entries, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            context.TimeEntries.AddRange(entries);
            await context.SaveChangesAsync(cancellationToken);
        }


        public async Task<List<TimeEntry>> GetForUserAsync(int userId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            return await context.TimeEntries
                .AsNoTracking()
                .Include(e => e.Task)
                    .ThenInclude(t => t!.Project)
                        .ThenInclude(p => p!.Company)
                .Where(e => e.UserId == userId && e.StartTime >= from && e.StartTime < to)
                .OrderBy(e => e.StartTime)
                .ToListAsync(cancellationToken);
        }


        public async Task<List<TimeEntry>> GetLatestForUserAsync(int userId, int count, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            return await context.TimeEntries
                .AsNoTracking()
                .Include(e => e.Task)
                .Where(e => e.UserId == userId)
                .OrderByDescending(e => e.StartTime)
                .ThenByDescending(e => e.TimeEntryId)
                .Take(count)
                .ToListAsync(cancellationToken);
        }
    }
}
