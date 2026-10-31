using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Sync;
using TimePlanner.Core.Tests.Support;
using Xunit;

namespace TimePlanner.Core.Tests.Sync
{
    //-----------------------------
    //tests for days the user sent that did not get through: they wait, they are sent again later, and nothing else is ever sent
    public class PendingSendTests : IDisposable
    {
        private static readonly DateOnly Day = new(2026, 9, 28);
        private static readonly DateTime Now = new(2026, 9, 29, 10, 0, 0);

        private readonly string _historyPath = Path.Combine(Path.GetTempPath(), $"sent-days-{Guid.NewGuid():N}.json");
        private readonly string _pendingPath = Path.Combine(Path.GetTempPath(), $"pending-sends-{Guid.NewGuid():N}.json");
        private readonly DateTime _nowUtc = DateTime.UtcNow;

        //-----------------------------
        //deletes the temporary files
        public void Dispose()
        {
            foreach (var path in new[] { _historyPath, _pendingPath, _pendingPath + ".tmp" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Stored() => Json(HttpStatusCode.OK, "{\"created\":1,\"skipped\":0,\"errors\":[]}");

        //-----------------------------
        //a host with a signed in user and one finished day on 2026-09-28
        private async Task<(CoreTestHost Host, IServiceScope Scope, DaySendService Service, int UserId, InMemoryTokenStore Tokens)> SetUpAsync(FakeDashboardHandler handler)
        {
            var tokens = new InMemoryTokenStore();
            await tokens.SaveAsync(new StoredToken("abc", DateTime.UtcNow.AddHours(1), "tester@x.com"));
            var host = new CoreTestHost(s =>
            {
                s.AddSingleton(new DashboardClient(new HttpClient(handler) { BaseAddress = new Uri("https://dashboard.test/") }));
                s.AddSingleton<ITokenStore>(tokens);
                s.AddSingleton<ISendHistoryStore>(new FileSendHistoryStore(_historyPath));
                s.AddSingleton<IPendingSendStore>(new FilePendingSendStore(_pendingPath));
                s.AddScoped<DaySendService>();
            });
            var scope = host.CreateScope();

            var dayStart = new DateTime(2026, 9, 28, 8, 0, 0);
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", dayStart);
            var sessions = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await sessions.StartDayAsync(user.UserId, dayStart);
            await LogAsync(scope.ServiceProvider, user.UserId, "fixed login", dayStart.AddHours(1));
            await sessions.EndDayAsync(user.UserId, dayStart.AddHours(9));

            return (host, scope, scope.ServiceProvider.GetRequiredService<DaySendService>(), user.UserId, tokens);
        }

        private static async Task LogAsync(IServiceProvider sp, int userId, string note, DateTime until)
        {
            var entries = sp.GetRequiredService<EntryService>();
            var project = (await entries.GetActiveProjectsAsync()).First(p => p.Name == LocalSetupService.InternalProjectName);
            await entries.LogEntryAsync(new LogEntryRequest(userId, project.ProjectID, 2, note, EntryMethod.AutoPrompted, until));
        }

        //-----------------------------
        //the retry waits follow a minute, two minutes, then five at a time
        [Fact]
        public void Backoff_GrowsThenStaysAtFiveMinutes()
        {
            Assert.Equal(TimeSpan.FromMinutes(1), DaySendService.Backoff(1));
            Assert.Equal(TimeSpan.FromMinutes(2), DaySendService.Backoff(2));
            Assert.Equal(TimeSpan.FromMinutes(5), DaySendService.Backoff(3));
            Assert.Equal(TimeSpan.FromMinutes(5), DaySendService.Backoff(40));
        }

        //-----------------------------
        //an offline send waits, with the reason and a promise to keep trying, and the day is not in the history
        [Fact]
        public async Task OfflineSend_QueuesTheDay()
        {
            var handler = new FakeDashboardHandler { ThrowOffline = true };
            var (host, scope, service, userId, _) = await SetUpAsync(handler);
            using (host) using (scope)
            {
                var outcome = await service.SendDayAsync(userId, Day, Now, _nowUtc);

                Assert.Equal(SendStatus.Offline, outcome.Status);
                Assert.True(outcome.Retryable);
                Assert.EndsWith("The widget will keep trying.", outcome.Message);
                var waiting = Assert.Single(await service.GetPendingAsync());
                Assert.Equal(Day, waiting.Day);
                Assert.Equal(1, waiting.Attempts);
                Assert.False(waiting.Rejected);
                Assert.Empty(await service.GetHistoryAsync());
            }
        }

        //-----------------------------
        //answers that can pass are queued
        [Theory]
        [InlineData(429)]
        [InlineData(500)]
        [InlineData(503)]
        public async Task TemporaryAnswers_QueueTheDay(int status)
        {
            var handler = new FakeDashboardHandler { Respond = _ => new HttpResponseMessage((HttpStatusCode)status) };
            var (host, scope, service, userId, _) = await SetUpAsync(handler);
            using (host) using (scope)
            {
                await service.SendDayAsync(userId, Day, Now, _nowUtc);

                Assert.Single(await service.GetPendingAsync());
            }
        }

        //-----------------------------
        //answers that will not change are never queued, and neither is a day the user has to sign in for
        [Theory]
        [InlineData(400)]
        [InlineData(403)]
        [InlineData(404)]
        public async Task RefusedAnswers_AreNotQueued(int status)
        {
            var handler = new FakeDashboardHandler
            {
                Respond = _ => Json((HttpStatusCode)status, "{\"created\":0,\"skipped\":0,\"errors\":[{\"row\":1,\"message\":\"Unknown activity\"}]}")
            };
            var (host, scope, service, userId, _) = await SetUpAsync(handler);
            using (host) using (scope)
            {
                await service.SendDayAsync(userId, Day, Now, _nowUtc);

                Assert.Empty(await service.GetPendingAsync());
            }
        }

        [Fact]
        public async Task SendThatNeedsSignIn_IsNotQueued()
        {
            var handler = new FakeDashboardHandler();
            var (host, scope, service, userId, tokens) = await SetUpAsync(handler);
            using (host) using (scope)
            {
                await tokens.ClearAsync();

                var outcome = await service.SendDayAsync(userId, Day, Now, _nowUtc);

                Assert.Equal(SendStatus.NeedsSignIn, outcome.Status);
                Assert.Empty(await service.GetPendingAsync());
            }
        }

        //-----------------------------
        //nothing leaves the pc for a day the user never sent: with nothing queued a retry makes no call at all
        [Fact]
        public async Task Retry_WithNothingQueued_SendsNothing()
        {
            var handler = new FakeDashboardHandler { Respond = _ => Stored() };
            var (host, scope, service, userId, _) = await SetUpAsync(handler);
            using (host) using (scope)
            {
                var delivered = await service.RetryPendingAsync(userId, Now, _nowUtc.AddHours(1));

                Assert.Empty(delivered);
                Assert.Equal(0, handler.Calls);
            }
        }

        //-----------------------------
        //once the dashboard is back the day is sent again with the entries as they are now, and then it stops waiting
        [Fact]
        public async Task Retry_SendsTheCurrentEntries_AndClearsTheQueue()
        {
            var handler = new FakeDashboardHandler { ThrowOffline = true };
            var (host, scope, service, userId, _) = await SetUpAsync(handler);
            using (host) using (scope)
            {
                await service.SendDayAsync(userId, Day, Now, _nowUtc);

                //the user corrects the entry while the day is waiting
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var entry = await db.TimeEntries.SingleAsync();
                entry.Note = "corrected while waiting";
                await db.SaveChangesAsync();
                handler.ThrowOffline = false;
                handler.Respond = _ => Stored();

                var delivered = await service.RetryPendingAsync(userId, Now, _nowUtc.AddMinutes(2));

                Assert.Equal(new[] { Day }, delivered);
                Assert.Empty(await service.GetPendingAsync());
                Assert.Equal(Day, Assert.Single(await service.GetHistoryAsync()).Day);
                var body = handler.Bodies.Last();
                Assert.Contains("\"replaceDays\":true", body);
                Assert.Contains("\"note\":\"corrected while waiting\"", body);
                Assert.DoesNotContain("fixed login", body);
            }
        }

        //-----------------------------
        //a day is not tried before it is due, and a failed retry waits longer next time
        [Fact]
        public async Task Retry_WaitsForTheBackoff_ThenBacksOffFurther()
        {
            var handler = new FakeDashboardHandler { ThrowOffline = true };
            var (host, scope, service, userId, _) = await SetUpAsync(handler);
            using (host) using (scope)
            {
                await service.SendDayAsync(userId, Day, Now, _nowUtc);
                var calls = handler.Calls;

                await service.RetryPendingAsync(userId, Now, _nowUtc.AddSeconds(30));
                Assert.Equal(calls, handler.Calls);

                var retryAt = _nowUtc.AddMinutes(1);
                await service.RetryPendingAsync(userId, Now, retryAt);
                Assert.Equal(calls + 1, handler.Calls);
                var waiting = Assert.Single(await service.GetPendingAsync());
                Assert.Equal(2, waiting.Attempts);
                Assert.Equal(retryAt + TimeSpan.FromMinutes(2), waiting.NextAttemptUtc);
            }
        }

        //-----------------------------
        //when the user has to sign in again nothing is sent and the day keeps waiting without losing its place
        [Fact]
        public async Task Retry_PausesWhenSignInIsNeeded()
        {
            var handler = new FakeDashboardHandler { ThrowOffline = true };
            var (host, scope, service, userId, tokens) = await SetUpAsync(handler);
            using (host) using (scope)
            {
                await service.SendDayAsync(userId, Day, Now, _nowUtc);
                var calls = handler.Calls;
                await tokens.ClearAsync();

                var delivered = await service.RetryPendingAsync(userId, Now, _nowUtc.AddMinutes(10));

                Assert.Empty(delivered);
                Assert.Equal(calls, handler.Calls);
                var waiting = Assert.Single(await service.GetPendingAsync());
                Assert.Equal(1, waiting.Attempts);
                Assert.False(waiting.Rejected);
            }
        }

        //-----------------------------
        //a day the dashboard refuses on a retry stops being retried and keeps the reason for the timesheet
        [Fact]
        public async Task Retry_StopsWhenTheDashboardRefusesTheDay()
        {
            var handler = new FakeDashboardHandler { ThrowOffline = true };
            var (host, scope, service, userId, _) = await SetUpAsync(handler);
            using (host) using (scope)
            {
                await service.SendDayAsync(userId, Day, Now, _nowUtc);
                handler.ThrowOffline = false;
                handler.Respond = _ => Json(HttpStatusCode.BadRequest, "{\"created\":0,\"skipped\":0,\"errors\":[{\"row\":1,\"message\":\"Unknown activity\"}]}");

                await service.RetryPendingAsync(userId, Now, _nowUtc.AddMinutes(2));

                var refused = Assert.Single(await service.GetPendingAsync());
                Assert.True(refused.Rejected);
                Assert.Equal("Row 1: Unknown activity", refused.Message);

                var calls = handler.Calls;
                await service.RetryPendingAsync(userId, Now, _nowUtc.AddHours(1));
                Assert.Equal(calls, handler.Calls);
            }
        }

        //-----------------------------
        //sending the day again by hand settles it, so it no longer waits
        [Fact]
        public async Task SendingAgainByHand_ClearsTheWaitingDay()
        {
            var handler = new FakeDashboardHandler { ThrowOffline = true };
            var (host, scope, service, userId, _) = await SetUpAsync(handler);
            using (host) using (scope)
            {
                await service.SendDayAsync(userId, Day, Now, _nowUtc);
                handler.ThrowOffline = false;
                handler.Respond = _ => Stored();

                var outcome = await service.SendDayAsync(userId, Day, Now, _nowUtc.AddSeconds(5));

                Assert.Equal(SendStatus.Sent, outcome.Status);
                Assert.Empty(await service.GetPendingAsync());
            }
        }

        //-----------------------------
        //the waiting list is a file, so a day still waits after the widget restarts, and an unreadable file just means nothing is waiting
        [Fact]
        public async Task PendingFile_SurvivesARestart_AndToleratesDamage()
        {
            var pending = new PendingDay(Day, Now, 2, _nowUtc);
            await new FilePendingSendStore(_pendingPath).SaveAsync(pending);

            Assert.Equal(pending, Assert.Single(await new FilePendingSendStore(_pendingPath).GetAsync()));

            await File.WriteAllTextAsync(_pendingPath, "{ not json");
            Assert.Empty(await new FilePendingSendStore(_pendingPath).GetAsync());
        }
    }
}
//------------------------------EOF-----------------------------\\
