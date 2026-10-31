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
    //tests for moving from one day to the next
    public class DayRulesTests : IDisposable
    {
        private static readonly DateTime Friday = new(2026, 10, 23, 8, 0, 0);
        private static readonly DateTime Saturday = new(2026, 10, 24, 9, 0, 0);

        private readonly CoreTestHost _host = new();

        public void Dispose() => _host.Dispose();

        //-----------------------------
        //today's state comes from a day that started today, never from an earlier one
        [Fact]
        public void StateOn_OnlyCountsDaysFromToday()
        {
            var open = new DaySession { StartedAt = Friday };
            var paused = new DaySession { StartedAt = Friday, Pauses = { new SessionPause { StartedAt = Friday.AddHours(2) } } };
            var ended = new DaySession { StartedAt = Friday, EndedAt = Friday.AddHours(8) };

            Assert.Equal(DayState.NotStarted, DayRules.StateOn(Friday, null, null));
            Assert.Equal(DayState.Tracking, DayRules.StateOn(Friday.AddHours(1), open, null));
            Assert.Equal(DayState.Paused, DayRules.StateOn(Friday.AddHours(3), paused, null));
            Assert.Equal(DayState.Ended, DayRules.StateOn(Friday.AddHours(9), null, ended));

            //the next morning nothing has started yet
            Assert.Equal(DayState.NotStarted, DayRules.StateOn(Saturday, null, ended));
            Assert.Equal(DayState.NotStarted, DayRules.StateOn(Saturday, open, null));
        }

        //-----------------------------
        //resume day only works on the date the day started
        [Fact]
        public void CanResume_OnlyOnTheSameDate()
        {
            var ended = new DaySession { StartedAt = Friday, EndedAt = Friday.AddHours(8) };

            Assert.True(DayRules.CanResume(ended, Friday.AddHours(9)));
            Assert.False(DayRules.CanResume(ended, Saturday));
            Assert.False(DayRules.CanResume(new DaySession { StartedAt = Friday }, Friday.AddHours(1)));
            Assert.False(DayRules.CanResume(null, Friday));
        }

        //-----------------------------
        //an open day is left open once the date has moved on
        [Fact]
        public void IsLeftOpen_AfterTheDateChanges()
        {
            var open = new DaySession { StartedAt = Friday };

            Assert.False(DayRules.IsLeftOpen(open, Friday.AddHours(15)));
            Assert.True(DayRules.IsLeftOpen(open, Saturday));
            Assert.False(DayRules.IsLeftOpen(new DaySession { StartedAt = Friday, EndedAt = Friday.AddHours(8) }, Saturday));
            Assert.False(DayRules.IsLeftOpen(null, Saturday));
        }

        //-----------------------------
        //the last activity is the latest of the start, the last entry and any break
        [Fact]
        public void LastActivity_TakesTheLatestTime()
        {
            var day = new DaySession { StartedAt = Friday, Pauses = { new SessionPause { StartedAt = Friday.AddHours(4), EndedAt = Friday.AddHours(5) } } };

            Assert.Equal(Friday.AddHours(5), DayRules.LastActivity(day, Friday.AddHours(3)));
            Assert.Equal(Friday.AddHours(7), DayRules.LastActivity(day, Friday.AddHours(7)));
            Assert.Equal(Friday, DayRules.LastActivity(new DaySession { StartedAt = Friday }, null));
        }

        //-----------------------------
        //only the latest earlier day with time is flagged, and only until it is sent
        [Fact]
        public void UnsentEarlierDay_IsTheLatestEarlierDayNotSent()
        {
            var thursday = new DateOnly(2026, 10, 22);
            var friday = new DateOnly(2026, 10, 23);
            var monday = new DateOnly(2026, 10, 26);

            Assert.Equal(friday, DayRules.UnsentEarlierDay([thursday, friday, monday], [], monday));
            Assert.Null(DayRules.UnsentEarlierDay([thursday, friday, monday], [friday], monday));
            Assert.Null(DayRules.UnsentEarlierDay([monday], [], monday));
            Assert.Null(DayRules.UnsentEarlierDay([], [], monday));
        }

        //-----------------------------
        //closing a day left open gives it back, ended at its last activity
        [Fact]
        public async Task CloseStaleSessions_GivesBackTheDayItClosed()
        {
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", Friday);
            var days = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            var entries = scope.ServiceProvider.GetRequiredService<EntryService>();
            await days.StartDayAsync(user.UserId, Friday);
            var project = (await entries.GetActiveProjectsAsync()).First();
            await entries.LogEntryAsync(new LogEntryRequest(user.UserId, project.ProjectID, 2, "work", EntryMethod.Manual, Friday.AddHours(6)));

            Assert.Null(await days.CloseStaleSessionsAsync(user.UserId, Friday.AddHours(10)));

            var closed = await days.CloseStaleSessionsAsync(user.UserId, Saturday);
            Assert.NotNull(closed);
            Assert.Equal(Friday.AddHours(6), closed.EndedAt);
            Assert.Null(await days.GetOpenSessionAsync(user.UserId));
            Assert.Null(await days.CloseStaleSessionsAsync(user.UserId, Saturday));
        }

        //-----------------------------
        //the widget can ask setup to leave a left open day for it to close
        [Fact]
        public async Task Initialise_CanLeaveLeftOpenDaysOpen()
        {
            using var scope = _host.CreateScope();
            var setup = scope.ServiceProvider.GetRequiredService<LocalSetupService>();
            var user = await setup.InitialiseAsync("tester", Friday);
            await scope.ServiceProvider.GetRequiredService<DaySessionService>().StartDayAsync(user.UserId, Friday);

            await setup.InitialiseAsync("tester", Saturday, closeLeftOpenDays: false);

            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IDaySessionRepository>().GetOpenAsync(user.UserId));
        }
    }
}
//------------------------------EOF-----------------------------\\
