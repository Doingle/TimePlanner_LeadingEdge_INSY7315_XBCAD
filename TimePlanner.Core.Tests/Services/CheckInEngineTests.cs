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
        //The selected interval applies at midday; breaks are controlled by explicit pauses.
        [Fact]
        public async Task Midday_DoesNotDeferPrompt()
        {
            var (_, engine) = await CreateSetupAsync(new DateTime(2026, 9, 28, 10, 45, 0));

            _clock.Now = new DateTime(2026, 9, 28, 12, 15, 0);
            var status1 = await engine.TickAsync();

            Assert.Equal(CheckInPhase.Due, status1.Phase);
            Assert.Equal(new DateTime(2026, 9, 28, 12, 15, 0), status1.DueSince);

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

        //-----------------------------
        //helper to change the check in interval
        private async Task SetIntervalAsync(int userId, CheckInEngine engine, int minutes)
        {
            using var scope = _host.CreateScope();
            var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var settings = await settingsService.GetAsync(userId);
            settings.CheckInIntervalMinutes = minutes;
            await settingsService.SaveAsync(settings);
            await engine.ReloadSettingsAsync();
        }

        //-----------------------------
        //helper to answer the check in now
        private async Task AnswerAsync(int userId, CheckInEngine engine)
        {
            using (var scope = _host.CreateScope())
            {
                var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
                var proj = (await entryService.GetActiveProjectsAsync()).First();
                await entryService.LogEntryAsync(new LogEntryRequest(userId, proj.ProjectID, 2, "work", EntryMethod.AutoPrompted, _clock.Now));
            }

            await engine.NotifyEntryLoggedAsync(true);
        }

        //-----------------------------
        //Clock marks are independent of the saved interval.
        [Theory]
        [InlineData(15)]
        [InlineData(60)]
        [InlineData(90)]
        public async Task OnTheHalfHour_UsesNextMark(int interval)
        {
            var (userId, engine) = await CreateSetupAsync(new DateTime(2026, 9, 28, 8, 10, 0));
            await SetIntervalAsync(userId, engine, interval);
            var status = await engine.SetTimingAsync(CheckInTiming.OnTheHalfHour);

            Assert.Equal(new DateTime(2026, 9, 28, 8, 30, 0), status.NextCheckInAt);

            _clock.Now = new DateTime(2026, 9, 28, 8, 29, 59);
            Assert.Equal(CheckInPhase.Waiting, (await engine.TickAsync()).Phase);

            _clock.Now = new DateTime(2026, 9, 28, 8, 30, 0);
            Assert.Equal(CheckInPhase.Due, (await engine.TickAsync()).Phase);

            //A late reply still leaves the next :00 or :30 scheduled.
            _clock.Now = new DateTime(2026, 9, 28, 8, 34, 0);
            await AnswerAsync(userId, engine);
            Assert.Equal(new DateTime(2026, 9, 28, 9, 0, 0), (await engine.TickAsync()).NextCheckInAt);
        }

        //-----------------------------
        //Starting between marks always waits for the very next one.
        [Fact]
        public async Task OnTheHalfHour_RoundsUpFromQuarterPast()
        {
            var (userId, engine) = await CreateSetupAsync(new DateTime(2026, 9, 28, 8, 20, 0));
            await SetIntervalAsync(userId, engine, 60);
            var status = await engine.SetTimingAsync(CheckInTiming.OnTheHalfHour);

            Assert.Equal(new DateTime(2026, 9, 28, 8, 30, 0), status.NextCheckInAt);
            Assert.Equal(new DateTime(2026, 9, 28, 11, 0, 0), CheckInEngine.NextHalfHour(new DateTime(2026, 9, 28, 10, 45, 0)));
        }

        //-----------------------------
        //No prompts during a break; resume at the next clock mark.
        [Fact]
        public async Task OnTheHalfHour_StillRespectsBreaks()
        {
            var (userId, engine) = await CreateSetupAsync();
            await SetIntervalAsync(userId, engine, 60);
            await engine.SetTimingAsync(CheckInTiming.OnTheHalfHour);

            using (var scope = _host.CreateScope())
            {
                var sessions = scope.ServiceProvider.GetRequiredService<DaySessionService>();
                await sessions.PauseAsync(userId, new DateTime(2026, 9, 28, 8, 30, 0));
                await engine.NotifySessionChangedAsync();

                _clock.Now = new DateTime(2026, 9, 28, 9, 0, 0);
                Assert.Equal(CheckInPhase.Paused, (await engine.TickAsync()).Phase);

                await sessions.ResumeAsync(userId, new DateTime(2026, 9, 28, 9, 10, 0));
                _clock.Now = new DateTime(2026, 9, 28, 9, 10, 0);
                await engine.NotifySessionChangedAsync();
            }

            //Resume at 9:10 waits for 9:30 rather than catching up paused prompts.
            var status = await engine.TickAsync();
            Assert.Equal(CheckInPhase.Waiting, status.Phase);
            Assert.Equal(new DateTime(2026, 9, 28, 9, 30, 0), status.NextCheckInAt);

            _clock.Now = new DateTime(2026, 9, 28, 9, 30, 0);
            Assert.Equal(CheckInPhase.Due, (await engine.TickAsync()).Phase);
        }

        //-----------------------------
        //A late answer must not suppress the next clock mark.
        [Fact]
        public async Task OnTheHalfHour_LateReplyDoesNotSkipNextMark()
        {
            var (userId, engine) = await CreateSetupAsync();
            await SetIntervalAsync(userId, engine, 20);
            await engine.SetTimingAsync(CheckInTiming.OnTheHalfHour);

            //Even nine minutes before a mark, the next check-in stays at :30.
            _clock.Now = new DateTime(2026, 9, 28, 9, 21, 0);
            await AnswerAsync(userId, engine);

            Assert.Equal(new DateTime(2026, 9, 28, 9, 30, 0), (await engine.TickAsync()).NextCheckInAt);
        }

        //-----------------------------
        //a day started by hand on a day off is tracked but never prompts, and its work still counts on a working day
        [Fact]
        public async Task DayOff_NeverPrompts()
        {
            var (_, engine) = await CreateSetupAsync();
            engine.IsCheckInDay = _ => false;

            _clock.Now = new DateTime(2026, 9, 28, 11, 0, 0);
            var status = await engine.TickAsync();

            Assert.Equal(CheckInPhase.DayOff, status.Phase);
            Assert.Null(status.NextCheckInAt);
            Assert.False(status.CanSnooze);
            await Assert.ThrowsAsync<InvalidOperationException>(engine.SnoozeAsync);

            //the same work comes due once the day counts again
            engine.IsCheckInDay = _ => true;
            Assert.Equal(CheckInPhase.Due, (await engine.TickAsync()).Phase);
        }

        //-----------------------------
        //switching timing keeps the work done since the last check in
        [Fact]
        public async Task SwitchingTiming_KeepsTheWorkSinceTheLastCheckIn()
        {
            var (userId, engine) = await CreateSetupAsync(new DateTime(2026, 9, 28, 8, 10, 0));
            await SetIntervalAsync(userId, engine, 60);

            Assert.Equal(new DateTime(2026, 9, 28, 9, 10, 0), (await engine.TickAsync()).NextCheckInAt);
            _clock.Now = new DateTime(2026, 9, 28, 8, 40, 0);
            Assert.Equal(new DateTime(2026, 9, 28, 9, 0, 0), (await engine.SetTimingAsync(CheckInTiming.OnTheHalfHour)).NextCheckInAt);
            Assert.Equal(new DateTime(2026, 9, 28, 9, 10, 0), (await engine.SetTimingAsync(CheckInTiming.FromLastCheckIn)).NextCheckInAt);
        }

        [Theory]
        [InlineData(10, 0, 0, 10, 30)]
        [InlineData(10, 29, 59, 10, 30)]
        [InlineData(10, 30, 0, 11, 0)]
        [InlineData(23, 59, 59, 0, 0)]
        public void ClockMarks_AreStrictlyFutureLocalTimes(int hour, int minute, int second, int nextHour, int nextMinute)
        {
            var from = new DateTime(2026, 9, 28, hour, minute, second, DateTimeKind.Local);
            var next = CheckInEngine.NextHalfHour(from);
            Assert.Equal(from.Date.AddDays(nextHour < hour ? 1 : 0).AddHours(nextHour).AddMinutes(nextMinute), next);
            Assert.Equal(DateTimeKind.Local, next.Kind);
        }

        [Fact]
        public async Task ClockTiming_MissedTickAndRestartStillShowDuePrompt()
        {
            var (userId, engine) = await CreateSetupAsync(new DateTime(2026, 9, 28, 8, 10, 0));
            await engine.SetTimingAsync(CheckInTiming.OnTheHalfHour);
            _clock.Now = new DateTime(2026, 9, 28, 8, 32, 0);
            Assert.Equal(CheckInPhase.Due, (await engine.TickAsync()).Phase);
            var restarted = new CheckInEngine(_host.Provider.GetRequiredService<IServiceScopeFactory>(), _clock)
            { Timing = CheckInTiming.OnTheHalfHour };
            Assert.Equal(CheckInPhase.Due, (await restarted.InitialiseAsync(userId)).Phase);
            await AnswerAsync(userId, restarted);
            Assert.Equal(new DateTime(2026, 9, 28, 9, 0, 0), (await restarted.TickAsync()).NextCheckInAt);
        }

        [Fact]
        public async Task ClockTiming_SnoozeAndDaysOffStillApply()
        {
            var (userId, engine) = await CreateSetupAsync();
            await engine.SetTimingAsync(CheckInTiming.OnTheHalfHour);
            _clock.Now = new DateTime(2026, 9, 28, 8, 30, 0);
            await engine.TickAsync();
            var snoozed = await engine.SnoozeAsync();
            Assert.Equal(CheckInPhase.Snoozed, snoozed.Phase);
            // An unrelated interval change cannot clear a clock-mode snooze.
            await SetIntervalAsync(userId, engine, 15);
            Assert.Equal(snoozed.SnoozedUntil, (await engine.TickAsync()).SnoozedUntil);
            _clock.Now = snoozed.SnoozedUntil!.Value;
            Assert.Equal(CheckInPhase.Due, (await engine.TickAsync()).Phase);
            engine.IsCheckInDay = _ => false;
            Assert.Equal(CheckInPhase.DayOff, (await engine.TickAsync()).Phase);
        //answering without entry clears prompt and resets interval without using skip
        [Fact]
        public async Task AnsweredWithoutEntry_ClearsPromptAndResetsInterval()
        {
            var (_, engine) = await CreateSetupAsync();

            _clock.Now = new DateTime(2026, 9, 28, 9, 30, 0);
            var dueStatus = await engine.TickAsync();
            Assert.Equal(CheckInPhase.Due, dueStatus.Phase);
            var initialSkips = dueStatus.SkipsLeft;

            var answeredAt = new DateTime(2026, 9, 28, 9, 32, 0);
            _clock.Now = answeredAt;
            var afterStatus = await engine.NotifyAnsweredWithoutEntryAsync(answeredAt);

            Assert.Equal(CheckInPhase.Waiting, afterStatus.Phase);
            Assert.Equal(answeredAt.AddMinutes(90), afterStatus.NextCheckInAt);
            Assert.Equal(initialSkips, afterStatus.SkipsLeft);
        }
    }
}
//------------------------------EOF-----------------------------\\
