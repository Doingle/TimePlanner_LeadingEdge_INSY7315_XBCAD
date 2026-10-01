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
    public class SQLiteUserRepository : IUserRepository
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public SQLiteUserRepository(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }


        public async Task<AppUser> GetOrCreateLocalUserAsync(string localAccountName, string displayName, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var user = await context.Users
                .Include(u => u.Settings)
                .SingleOrDefaultAsync(u => u.LocalAccountName == localAccountName, cancellationToken);

            if (user == null)
            {
                user = new AppUser { Name = displayName, LocalAccountName = localAccountName, Role = UserRole.Developer };
                context.Users.Add(user);
            }

            user.Settings ??= new UserSettings();

            await context.SaveChangesAsync(cancellationToken);
            return user;
        }


        public async Task UpdateSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var stored = await context.UserSettings.SingleAsync(s => s.UserSettingsId == settings.UserSettingsId, cancellationToken);
            context.Entry(stored).CurrentValues.SetValues(settings);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
