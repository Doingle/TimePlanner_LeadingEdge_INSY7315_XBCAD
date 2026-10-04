using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Tests.Support;
using Xunit;

namespace TimePlanner.Core.Tests.Services
{
    //-----------------------------
    //unit and integration tests for CheckInEngine
    public class CheckInEngineTests : IDisposable
    {
        private readonly FakeClock _clock;
        private readonly CoreTestHost _host;

        public CheckInEngineTests()
        {
            _clock = new FakeClock { Now = new DateTime(2026, 9, 28, 8, 0, 0) };
            _host = new CoreTestHost(s => s.AddSingleton<IClock>(_clock));
        }

        public void Dispose()
        {
            _host.Dispose();
        }

        //-----------------------------
        //helper to set up a user session and engine
        private async Task<(int userId, CheckInEngine engine)> CreateSetupAsync(DateTime? customStartTime = null)
        {
            if (customStartTime != null)
            {
                _clock.Now = customStartTime.Value;
            }

            using var scope = _host.CreateScope();
            var setup = scope.ServiceProvider.GetRequiredService<LocalSetupService>();
            var user = await setup.InitialiseAsync("tester", _clock.Now);

            var sessionService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await sessionService.StartDayAsync(user.UserId, _clock.Now);

            var engine = _host.Provider.GetRequiredService<CheckInEngine>();
            await engine.InitialiseAsync(user.UserId);
            await engine.NotifySessionChangedAsync();

            return (user.UserId, engine);
        }

        //-----------------------------
        //status remains waiting until check in interval passes
        [Fact]
        public async Task Waiting_UntilIntervalPasses()
        {
            var (_, engine) = await CreateSetupAsync();

            _clock.Now = new DateTime(2026, 9, 28, 9, 29, 0);
            var status1 = await engine.TickAsync();

            Assert.Equal(CheckInPhase.Waiting, status1.Phase);
            Assert.Equal(new DateTime(2026, 9, 28, 9, 30, 0), status1.NextCheckInAt);

            _clock.Now = new DateTime(2026, 9, 28, 9, 30, 0);
            var status2 = await engine.TickAsync();

            Assert.Equal(CheckInPhase.Due, status2.Phase);
        }

        //-----------------------------
        //a break delays the next due prompt time
        [Fact]
        public async Task Pause_ShiftsDueTime()
        {
            var (userId, engine) = await CreateSetupAsync();

            using (var scope = _host.CreateScope())
            {
                var sessions = scope.ServiceProvider.GetRequiredService<DaySessionService>();
                await sessions.PauseAsync(userId, new DateTime(2026, 9, 28, 8, 30, 0));
                await engine.NotifySessionChangedAsync();

                await sessions.ResumeAsync(userId, new DateTime(2026, 9, 28, 9, 0, 0));
                await engine.NotifySessionChangedAsync();
            }

            _clock.Now = new DateTime(2026, 9, 28, 9, 30, 0);
            var status1 = await engine.TickAsync();

            Assert.Equal(CheckInPhase.Waiting, status1.Phase);
            Assert.Equal(new DateTime(2026, 9, 28, 10, 0, 0), status1.NextCheckInAt);

            _clock.Now = new DateTime(2026, 9, 28, 10, 0, 0);
            var status2 = await engine.TickAsync();

            Assert.Equal(CheckInPhase.Due, status2.Phase);
        }

        //-----------------------------
        //engine shows paused phase while day session is paused
        [Fact]
        public async Task Paused_ShowsPausedPhase()
        {
            var (userId, engine) = await CreateSetupAsync();

            using var scope = _host.CreateScope();
            var sessions = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await sessions.PauseAsync(userId, new DateTime(2026, 9, 28, 8, 30, 0));
            var status = await engine.NotifySessionChangedAsync();

            Assert.Equal(CheckInPhase.Paused, status.Phase);
        }

        //-----------------------------
        //ending the day session changes phase to not tracking
        [Fact]
        public async Task NoOpenDay_ShowsNotTracking()
        {
            var (userId, engine) = await CreateSetupAsync();

            using var scope = _host.CreateScope();
            var sessions = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await sessions.EndDayAsync(userId, new DateTime(2026, 9, 28, 10, 0, 0));
            var status = await engine.NotifySessionChangedAsync();

            Assert.Equal(CheckInPhase.NotTracking, status.Phase);
        }

        //-----------------------------
        //a prompt due during lunch is deferred until lunch ends
        [Fact]
        public async Task Lunch_DefersPrompt()
        {
            var (_, engine) = await CreateSetupAsync(new DateTime(2026, 9, 28, 10, 45, 0));

            _clock.Now = new DateTime(2026, 9, 28, 12, 15, 0);
            var status1 = await engine.TickAsync();

            Assert.Equal(CheckInPhase.Waiting, status1.Phase);
            Assert.Equal(new DateTime(2026, 9, 28, 13, 0, 0), status1.NextCheckInAt);

            _clock.Now = new DateTime(2026, 9, 28, 13, 0, 0);
            var status2 = await engine.TickAsync();

            Assert.Equal(CheckInPhase.Due, status2.Phase);
        }

        //-----------------------------
        //snoozing delays prompt and returns to due when snooze duration ends
        [Fact]
        public async Task Snooze_ReturnsToDueWhenOver()
        {
            var (_, engine) = await CreateSetupAsync();

            _clock.Now = new DateTime(2026, 9, 28, 9, 30, 0);
            await engine.TickAsync();

            await engine.SnoozeAsync();

            _clock.Now = new DateTime(2026, 9, 28, 9, 39, 0);
            var status1 = await engine.TickAsync();

            Assert.Equal(CheckInPhase.Snoozed, status1.Phase);

            _clock.Now = new DateTime(2026, 9, 28, 9, 40, 0);
            var status2 = await engine.TickAsync();

            Assert.Equal(CheckInPhase.Due, status2.Phase);
        }

        //-----------------------------
        //snooze limit is enforced per check in prompt
        [Fact]
        public async Task Snooze_LimitedPerCheckIn()
        {
            var (_, engine) = await CreateSetupAsync();

            _clock.Now = new DateTime(2026, 9, 28, 9, 30, 0);
            await engine.TickAsync();

            //snooze 1
            await engine.SnoozeAsync();
            _clock.Now = new DateTime(2026, 9, 28, 9, 40, 0);
            await engine.TickAsync();

            //snooze 2
            await engine.SnoozeAsync();
            _clock.Now = new DateTime(2026, 9, 28, 9, 50, 0);
            await engine.TickAsync();

            //snooze 3
            var statusAfter3 = await engine.SnoozeAsync();

            Assert.False(statusAfter3.CanSnooze);
            Assert.Equal(0, statusAfter3.SnoozesLeft);

            _clock.Now = new DateTime(2026, 9, 28, 10, 0, 0);
            await engine.TickAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => engine.SnoozeAsync());
        }

        //-----------------------------
        //skipping a check in restarts the timer and decrements remaining skips
        [Fact]
        public async Task Skip_RestartsTimer()
        {
            var (_, engine) = await CreateSetupAsync();

            _clock.Now = new DateTime(2026, 9, 28, 9, 30, 0);
            await engine.TickAsync();

            var status = await engine.SkipAsync();

            Assert.Equal(CheckInPhase.Waiting, status.Phase);
            Assert.Equal(new DateTime(2026, 9, 28, 11, 0, 0), status.NextCheckInAt);
            Assert.Equal(2, status.SkipsLeft);
        }

        //-----------------------------
        //daily skip limit is enforced and state survives engine recreate
        [Fact]
        public async Task Skip_LimitedPerDayAndSurvivesRestart()
        {
            var (userId, engine) = await CreateSetupAsync();

            //skip 1 at 09:30
            _clock.Now = new DateTime(2026, 9, 28, 9, 30, 0);
            await engine.TickAsync();
            await engine.SkipAsync();

            //skip 2 at 11:00
            _clock.Now = new DateTime(2026, 9, 28, 11, 0, 0);
            await engine.TickAsync();
            await engine.SkipAsync();

            //skip 3 at 13:30 (11:00 + 90m + 60m lunch) -> 13:30
            _clock.Now = new DateTime(2026, 9, 28, 13, 30, 0);
            await engine.TickAsync();
            await engine.SkipAsync();

            //fourth skip at 15:00 should throw
            _clock.Now = new DateTime(2026, 9, 28, 15, 0, 0);
            await engine.TickAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => engine.SkipAsync());

            //recreate engine from scopes to test persistence
            var newEngine = new CheckInEngine(_host.Provider.GetRequiredService<IServiceScopeFactory>(), _clock);
            var restartedStatus = await newEngine.InitialiseAsync(userId);

            Assert.Equal(0, restartedStatus.SkipsLeft);
        }

        //-----------------------------
        //skip count resets on a new calendar day
        [Fact]
        public async Task Skip_CountResetsNextDay()
        {
            var (userId, engine) = await CreateSetupAsync();

            //use 3 skips
            _clock.Now = new DateTime(2026, 9, 28, 9, 30, 0);
            await engine.TickAsync();
            await engine.SkipAsync();

            _clock.Now = new DateTime(2026, 9, 28, 11, 0, 0);
            await engine.TickAsync();
            await engine.SkipAsync();

            _clock.Now = new DateTime(2026, 9, 28, 13, 30, 0);
            await engine.TickAsync();
            await engine.SkipAsync();

            //end day
            using (var scope = _host.CreateScope())
            {
                var sessions = scope.ServiceProvider.GetRequiredService<DaySessionService>();
                await sessions.EndDayAsync(userId, new DateTime(2026, 9, 28, 17, 0, 0));
            }

            //move to next day
            _clock.Now = new DateTime(2026, 9, 29, 8, 0, 0);
            using (var scope = _host.CreateScope())
            {
                var sessions = scope.ServiceProvider.GetRequiredService<DaySessionService>();
                await sessions.StartDayAsync(userId, _clock.Now);
            }

            var nextDayStatus = await engine.NotifySessionChangedAsync();

            Assert.Equal(3, nextDayStatus.SkipsLeft);
        }

        //-----------------------------
        //logging an entry at prompt restarts check in timer
        [Fact]
        public async Task EntryAtPrompt_RestartsTimer()
        {
            var (userId, engine) = await CreateSetupAsync();

            _clock.Now = new DateTime(2026, 9, 28, 9, 30, 0);
            await engine.TickAsync();

            using (var scope = _host.CreateScope())
            {
                var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
                var projects = await entryService.GetActiveProjectsAsync();
                var proj = projects.First();
                await entryService.LogEntryAsync(new LogEntryRequest(userId, proj.ProjectID, 2, "work", EntryMethod.AutoPrompted, _clock.Now));
            }

            var status = await engine.NotifyEntryLoggedAsync(false);

            Assert.Equal(CheckInPhase.Waiting, status.Phase);
            Assert.Equal(new DateTime(2026, 9, 28, 11, 0, 0), status.NextCheckInAt);
        }

        //-----------------------------
        //early logging respects restart timer flag choice
        [Fact]
        public async Task EarlyLog_KeepOrRestartTimer()
        {
            var (userId, engine) = await CreateSetupAsync();

            _clock.Now = new DateTime(2026, 9, 28, 9, 0, 0);

            using (var scope = _host.CreateScope())
            {
                var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
                var projects = await entryService.GetActiveProjectsAsync();
                var proj = projects.First();
                await entryService.LogEntryAsync(new LogEntryRequest(userId, proj.ProjectID, 2, "early 1", EntryMethod.Manual, _clock.Now));
            }

            var status1 = await engine.NotifyEntryLoggedAsync(false);

            Assert.Equal(new DateTime(2026, 9, 28, 9, 30, 0), status1.NextCheckInAt);

            _clock.Now = new DateTime(2026, 9, 28, 9, 10, 0);

            using (var scope = _host.CreateScope())
            {
                var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
                var projects = await entryService.GetActiveProjectsAsync();
                var proj = projects.First();
                await entryService.LogEntryAsync(new LogEntryRequest(userId, proj.ProjectID, 2, "early 2", EntryMethod.Manual, _clock.Now));
            }

            var status2 = await engine.NotifyEntryLoggedAsync(true);

            Assert.Equal(new DateTime(2026, 9, 28, 10, 40, 0), status2.NextCheckInAt);
        }

        //-----------------------------
        //ignored prompt with LogAsUntracked action restarts timer without taking skip
        [Fact]
        public async Task Ignored_LogAsUntracked_RestartsWithoutSkip()
        {
            var (userId, engine) = await CreateSetupAsync();

            using (var scope = _host.CreateScope())
            {
                var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
                var settings = await settingsService.GetAsync(userId);
                settings.IgnoredCheckInAction = IgnoredCheckInAction.LogAsUntracked;
                await settingsService.SaveAsync(settings);
                await engine.ReloadSettingsAsync();
            }

            _clock.Now = new DateTime(2026, 9, 28, 9, 30, 0);
            await engine.TickAsync();

            _clock.Now = new DateTime(2026, 9, 28, 9, 35, 0);
            var status = await engine.TickAsync();

            Assert.Equal(CheckInPhase.Waiting, status.Phase);
            Assert.Equal(new DateTime(2026, 9, 28, 11, 05, 0), status.NextCheckInAt);
            Assert.Equal(3, status.SkipsLeft);
        }

        //-----------------------------
        //ignored prompt with AutoSkip action uses skip
        [Fact]
        public async Task Ignored_AutoSkip_UsesSkip()
        {
            var (userId, engine) = await CreateSetupAsync();

            using (var scope = _host.CreateScope())
            {
                var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
                var settings = await settingsService.GetAsync(userId);
                settings.IgnoredCheckInAction = IgnoredCheckInAction.AutoSkip;
                await settingsService.SaveAsync(settings);
                await engine.ReloadSettingsAsync();
            }

            _clock.Now = new DateTime(2026, 9, 28, 9, 30, 0);
            await engine.TickAsync();

            _clock.Now = new DateTime(2026, 9, 28, 9, 35, 0);
            var status = await engine.TickAsync();

            Assert.Equal(CheckInPhase.Waiting, status.Phase);
            Assert.Equal(2, status.SkipsLeft);
        }
    }
}
//------------------------------EOF-----------------------------\\
