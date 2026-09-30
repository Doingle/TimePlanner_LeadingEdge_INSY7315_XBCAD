using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Tests.Support;
using Xunit;

namespace TimePlanner.Core.Tests.Services
{
    //-----------------------------
    //integration tests for LocalSetupService
    public class LocalSetupServiceTests : IDisposable
    {
        private readonly CoreTestHost _host;

        public LocalSetupServiceTests()
        {
            _host = new CoreTestHost();
        }

        public void Dispose()
        {
            _host.Dispose();
        }

        //-----------------------------
        //initialise creates user settings and internal project
        [Fact]
        public async Task Initialise_CreatesUserSettingsAndInternalProject()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var setup = scope.ServiceProvider.GetRequiredService<LocalSetupService>();
            var user = await setup.InitialiseAsync("tester", baseTime);

            Assert.Equal("tester", user.LocalAccountName);
            Assert.Equal(UserRole.Developer, user.Role);
            Assert.NotNull(user.Settings);
            Assert.Equal(90, user.Settings.CheckInIntervalMinutes);

            var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
            var activeProjects = await projects.GetActiveAsync();
            var internalProj = Assert.Single(activeProjects, p => p.Name == LocalSetupService.InternalProjectName);
            Assert.Equal(LocalSetupService.InternalCompanyName, internalProj.Company!.Name);
        }

        //-----------------------------
        //initialise called twice creates nothing new
        [Fact]
        public async Task Initialise_RunTwice_CreatesNothingNew()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope1 = _host.CreateScope();
            var user1 = await scope1.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            using var scope2 = _host.CreateScope();
            var user2 = await scope2.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            Assert.Equal(user1.UserId, user2.UserId);

            var companies = await scope2.ServiceProvider.GetRequiredService<ICompanyRepository>().GetAllAsync();
            Assert.Single(companies, c => c.Name == LocalSetupService.InternalCompanyName);

            var activeProjects = await scope2.ServiceProvider.GetRequiredService<IProjectRepository>().GetActiveAsync();
            Assert.Single(activeProjects, p => p.Name == LocalSetupService.InternalProjectName);
        }

        //-----------------------------
        //initialise closes a stale session at its last activity time
        [Fact]
        public async Task Initialise_ClosesStaleSessionAtLastActivity()
        {
            var yesterday = new DateTime(2026, 9, 27, 8, 0, 0);
            var today = new DateTime(2026, 9, 28, 8, 0, 0);

            using var scope1 = _host.CreateScope();
            var setup = scope1.ServiceProvider.GetRequiredService<LocalSetupService>();
            var user = await setup.InitialiseAsync("tester", yesterday);

            var dayService = scope1.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, yesterday);

            var entryService = scope1.ServiceProvider.GetRequiredService<EntryService>();
            var projects = await entryService.GetActiveProjectsAsync();
            var internalProj = projects.First(p => p.Name == LocalSetupService.InternalProjectName);

            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "work", EntryMethod.Manual, yesterday.AddHours(2)));

            using var scope2 = _host.CreateScope();
            await scope2.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", today);

            var sessions = scope2.ServiceProvider.GetRequiredService<IDaySessionRepository>();
            var open = await sessions.GetOpenAsync(user.UserId);
            Assert.Null(open);

            var daySessions = await sessions.GetForUserBetweenAsync(user.UserId, yesterday.Date, today.Date.AddDays(1));
            var closed = Assert.Single(daySessions);
            Assert.Equal(yesterday.AddHours(2), closed.EndedAt);
        }
    }
}
//------------------------------EOF-----------------------------\\
