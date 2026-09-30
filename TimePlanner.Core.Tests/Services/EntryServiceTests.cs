using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Tests.Support;
using Xunit;

namespace TimePlanner.Core.Tests.Services
{
    //-----------------------------
    //unit and integration tests for EntryService
    public class EntryServiceTests : IDisposable
    {
        private readonly CoreTestHost _host;

        public EntryServiceTests()
        {
            _host = new CoreTestHost();
        }

        public void Dispose()
        {
            _host.Dispose();
        }

        //-----------------------------
        //unlogged span starts at day start when no previous entry exists
        [Fact]
        public async Task UnloggedSpan_StartsAtDayStart()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var now = baseTime.AddHours(1.5);
            var span = await entryService.GetUnloggedSpanAsync(user.UserId, now);

            Assert.Equal(baseTime, span.Start);
            Assert.Equal(now, span.End);
            Assert.Equal(TimeSpan.FromHours(1.5), span.WorkedTime);
        }

        //-----------------------------
        //logging an entry splits unlogged time around recorded breaks
        [Fact]
        public async Task LogEntry_SplitsAroundBreak()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);
            await dayService.PauseAsync(user.UserId, baseTime.AddHours(1));
            await dayService.ResumeAsync(user.UserId, baseTime.AddHours(1.5));

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var projects = await entryService.GetActiveProjectsAsync();
            var internalProj = projects.First(p => p.Name == LocalSetupService.InternalProjectName);

            var now = baseTime.AddHours(2);
            var logged = await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "split work", EntryMethod.Manual, now));

            Assert.Equal(2, logged.Count);
            Assert.Equal(baseTime, logged[0].StartTime);
            Assert.Equal(baseTime.AddHours(1), logged[0].EndTime);
            Assert.Equal(baseTime.AddHours(1.5), logged[1].StartTime);
            Assert.Equal(now, logged[1].EndTime);
            Assert.Equal(logged[0].TaskId, logged[1].TaskId);
        }

        //-----------------------------
        //logging multiple times for same project and activity reuses one task named Coding
        [Fact]
        public async Task LogEntry_ReusesTaskForSameProjectAndActivity()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var projects = await entryService.GetActiveProjectsAsync();
            var internalProj = projects.First(p => p.Name == LocalSetupService.InternalProjectName);

            var log1 = await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "first", EntryMethod.Manual, baseTime.AddHours(1)));
            var log2 = await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "second", EntryMethod.Manual, baseTime.AddHours(2)));

            var entry1 = Assert.Single(log1);
            var entry2 = Assert.Single(log2);
            Assert.Equal(entry1.TaskId, entry2.TaskId);

            var tasks = scope.ServiceProvider.GetRequiredService<IWorkTaskRepository>();
            var task = await tasks.GetByIdAsync(entry1.TaskId);
            Assert.NotNull(task);
            Assert.Equal("Coding", task.Name);
        }

        //-----------------------------
        //logging while paused throws an exception
        [Fact]
        public async Task LogEntry_WhilePaused_Throws()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);
            await dayService.PauseAsync(user.UserId, baseTime.AddHours(1));

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var projects = await entryService.GetActiveProjectsAsync();
            var internalProj = projects.First(p => p.Name == LocalSetupService.InternalProjectName);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "paused log", EntryMethod.Manual, baseTime.AddHours(1.5))));
        }

        //-----------------------------
        //logging under one minute of work throws an exception
        [Fact]
        public async Task LogEntry_UnderOneMinute_Throws()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var projects = await entryService.GetActiveProjectsAsync();
            var internalProj = projects.First(p => p.Name == LocalSetupService.InternalProjectName);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "short", EntryMethod.Manual, baseTime.AddSeconds(30))));
        }

        //-----------------------------
        //adding a project reuses company and project ignoring case
        [Fact]
        public async Task AddProject_ReusesCompanyAndProjectIgnoringCase()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var proj1 = await entryService.AddProjectAsync("Acme", "Portal", "#FFFFFF");
            var proj2 = await entryService.AddProjectAsync("acme", "PORTAL", "#000000");

            Assert.Equal(proj1.ProjectID, proj2.ProjectID);

            var companies = await scope.ServiceProvider.GetRequiredService<ICompanyRepository>().GetAllAsync();
            Assert.Single(companies, c => c.Name.Equals("Acme", StringComparison.OrdinalIgnoreCase));
        }

        //-----------------------------
        //last entry returns project id and activity path
        [Fact]
        public async Task GetLastEntry_ReturnsProjectAndPath()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var projects = await entryService.GetActiveProjectsAsync();
            var internalProj = projects.First(p => p.Name == LocalSetupService.InternalProjectName);

            var endTime = baseTime.AddHours(1);
            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "coding task", EntryMethod.Manual, endTime));

            var lastEntry = await entryService.GetLastEntryAsync(user.UserId);
            Assert.NotNull(lastEntry);
            Assert.Equal(internalProj.ProjectID, lastEntry!.ProjectId);
            Assert.Equal(2, lastEntry.CategoryId);
            Assert.Equal(new[] { "Coding" }, lastEntry.ActivityPath);
            Assert.Equal(endTime, lastEntry.EndedAt);
        }
    }
}
//------------------------------EOF-----------------------------\\
