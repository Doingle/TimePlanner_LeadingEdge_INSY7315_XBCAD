using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Services
{
    //-----------------------------
    //prepares the database and windows user on start
    public class LocalSetupService
    {
        //company that owns internal work
        public const string InternalCompanyName = "Leading Edge (Internal)";

        //project for internal work
        public const string InternalProjectName = "Internal";

        private readonly AppDbContext _db;
        private readonly IAppUserRepository _users;
        private readonly ICompanyRepository _companies;
        private readonly IProjectRepository _projects;
        private readonly DaySessionService _sessions;

        public LocalSetupService(AppDbContext db, IAppUserRepository users, ICompanyRepository companies, IProjectRepository projects, DaySessionService sessions)
        {
            _db = db;
            _users = users;
            _companies = companies;
            _projects = projects;
            _sessions = sessions;
        }

        //-----------------------------
        //migrates makes user settings and internal project available to app
        public async Task<AppUser> InitialiseAsync(string localAccountName, DateTime now)
        {
            //the account name finds the user again
            if (string.IsNullOrWhiteSpace(localAccountName))
            {
                throw new ArgumentException("A local account name is required.", nameof(localAccountName));
            }

            await _db.Database.MigrateAsync();

            var user = await _users.GetByLocalAccountNameAsync(localAccountName);

            //first launch creates the user
            if (user == null)
            {
                user = new AppUser { Name = localAccountName, LocalAccountName = localAccountName, Role = UserRole.Developer };
                await _users.AddAsync(user);
            }

            var loaded = await _users.GetByIdAsync(user.UserId);

            //a user without settings gets defaults
            if (loaded!.Settings == null)
            {
                await _users.SaveSettingsAsync(new UserSettings { UserId = user.UserId });
            }

            await EnsureInternalProjectAsync();
            await _sessions.CloseStaleSessionsAsync(user.UserId, now);

            return (await _users.GetByIdAsync(user.UserId))!;
        }

        //-----------------------------
        //creates the internal company and project once
        private async Task EnsureInternalProjectAsync()
        {
            var company = (await _companies.GetAllAsync()).FirstOrDefault(c => c.Name == InternalCompanyName);

            //the internal company is created once
            if (company == null)
            {
                company = new Company { Name = InternalCompanyName };
                await _companies.AddAsync(company);
            }

            var projects = await _projects.GetByCompanyAsync(company.CompanyId);

            //the internal project is created once
            if (!projects.Any(p => p.Name == InternalProjectName))
            {
                await _projects.AddAsync(new Project { Name = InternalProjectName, CompanyId = company.CompanyId, Colour = "#71717A" });
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
