using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Sync;
using TimePlanner.Core.Tests.Support;
using Xunit;

namespace TimePlanner.Core.Tests.Sync
{
    //-----------------------------
    //tests for previewing and sending days to the dashboard
    public class DaySendServiceTests : IDisposable
    {
        private readonly string _historyPath = Path.Combine(Path.GetTempPath(), $"sent-days-{Guid.NewGuid():N}.json");

        //-----------------------------
        //deletes temporary test history file
        public void Dispose()
        {
            //removes history file created during tests
            if (File.Exists(_historyPath))
            {
                File.Delete(_historyPath);
            }
        }

        //-----------------------------
        //creates test host with sync services registered
        private CoreTestHost CreateHost(FakeDashboardHandler handler, InMemoryTokenStore tokens)
        {
            return new CoreTestHost(s =>
            {
                s.AddSingleton(new DashboardClient(new HttpClient(handler) { BaseAddress = new Uri("https://dashboard.test/") }));
                s.AddSingleton<ITokenStore>(tokens);
                s.AddSingleton<ISendHistoryStore>(new FileSendHistoryStore(_historyPath));
                s.AddScoped<DaySendService>();
            });
        }

        //-----------------------------
        //seeds a finished day on 2026-09-28 with one entry
        private static async Task<int> SeedDayAsync(IServiceProvider sp)
        {
            var dayStart = new DateTime(2026, 9, 28, 8, 0, 0);
            var user = await sp.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", dayStart);
            var sessions = sp.GetRequiredService<DaySessionService>();
            await sessions.StartDayAsync(user.UserId, dayStart);

            var entries = sp.GetRequiredService<EntryService>();
            var projects = await entries.GetActiveProjectsAsync();
            var proj = projects.First(p => p.Name == LocalSetupService.InternalProjectName);

            await entries.LogEntryAsync(new LogEntryRequest(
                user.UserId,
                proj.ProjectID,
                2,
                "fixed login",
                EntryMethod.AutoPrompted,
                dayStart.AddHours(1)));

            await sessions.EndDayAsync(user.UserId, dayStart.AddHours(9));
            return user.UserId;
        }

        //-----------------------------
        //sending a day posts reviewed rows with replaceDays flag
        [Fact]
        public async Task SendDay_PostsReviewedRowsWithReplaceDays()
        {
            var handler = new FakeDashboardHandler
            {
                Respond = _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"created\":1,\"skipped\":0,\"errors\":[]}", System.Text.Encoding.UTF8, "application/json")
                }
            };
            var tokens = new InMemoryTokenStore();
            await tokens.SaveAsync(new StoredToken("abc", DateTime.UtcNow.AddHours(1), "tester@x.com"));
            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var userId = await SeedDayAsync(scope.ServiceProvider);
            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();
            var day = new DateOnly(2026, 9, 28);
            var now = new DateTime(2026, 9, 29, 10, 0, 0);

            var outcome = await service.SendDayAsync(userId, day, now, DateTime.UtcNow);

            Assert.Equal(SendStatus.Sent, outcome.Status);
            Assert.Equal(1, outcome.Created);
            Assert.Equal(1, handler.Calls);

            var body = Assert.Single(handler.Bodies);
            Assert.Contains("\"replaceDays\":true", body);
            Assert.Contains("\"activity\":\"Coding\"", body);
            Assert.Contains("\"company\":\"Leading Edge (Internal)\"", body);
            Assert.Contains("\"project\":\"Internal\"", body);
            Assert.Contains("\"start\":\"2026-09-28T08:00:00\"", body);
            Assert.Contains("\"method\":\"AutoPrompted\"", body);
            Assert.Contains("\"note\":\"fixed login\"", body);

            var history = await service.GetHistoryAsync();
            var sent = Assert.Single(history);
            Assert.Equal(day, sent.Day);
            Assert.Equal(1, sent.Entries);
        }

        //-----------------------------
        //today cannot be sent while its day session is still open
        [Fact]
        public async Task SendDay_RefusesTodayWhileDayIsOpen()
        {
            var handler = new FakeDashboardHandler();
            var tokens = new InMemoryTokenStore();
            await tokens.SaveAsync(new StoredToken("abc", DateTime.UtcNow.AddHours(1), "tester@x.com"));
            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var dayStart = new DateTime(2026, 9, 28, 8, 0, 0);
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", dayStart);
            var sessions = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await sessions.StartDayAsync(user.UserId, new DateTime(2026, 9, 29, 8, 0, 0));

            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();
            var today = new DateOnly(2026, 9, 29);
            var now = new DateTime(2026, 9, 29, 10, 0, 0);

            var outcome = await service.SendDayAsync(user.UserId, today, now, DateTime.UtcNow);

            Assert.Equal(SendStatus.NotEnded, outcome.Status);
            Assert.Equal(0, handler.Calls);
        }

        //-----------------------------
        //sending without a valid saved token asks the user to sign in
        [Fact]
        public async Task SendDay_NeedsSignInWithoutToken()
        {
            var handler = new FakeDashboardHandler();
            var tokens = new InMemoryTokenStore();
            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var userId = await SeedDayAsync(scope.ServiceProvider);
            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();
            var day = new DateOnly(2026, 9, 28);
            var now = new DateTime(2026, 9, 29, 10, 0, 0);

            var outcome = await service.SendDayAsync(userId, day, now, DateTime.UtcNow);

            Assert.Equal(SendStatus.NeedsSignIn, outcome.Status);
            Assert.Equal(0, handler.Calls);
        }

        //-----------------------------
        //a 401 response from the server clears the stored token
        [Fact]
        public async Task SendDay_ClearsTokenWhenServerRefusesIt()
        {
            var handler = new FakeDashboardHandler
            {
                Respond = _ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
            };
            var tokens = new InMemoryTokenStore();
            await tokens.SaveAsync(new StoredToken("abc", DateTime.UtcNow.AddHours(1), "tester@x.com"));
            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var userId = await SeedDayAsync(scope.ServiceProvider);
            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();
            var day = new DateOnly(2026, 9, 28);
            var now = new DateTime(2026, 9, 29, 10, 0, 0);

            var outcome = await service.SendDayAsync(userId, day, now, DateTime.UtcNow);

            Assert.Equal(SendStatus.NeedsSignIn, outcome.Status);
            Assert.Null(await tokens.LoadAsync());
        }

        //-----------------------------
        //a 400 response with row errors sets rejected status and returns error list
        [Fact]
        public async Task SendDay_ReturnsRowErrors()
        {
            var handler = new FakeDashboardHandler
            {
                Respond = _ => new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"created\":0,\"skipped\":0,\"errors\":[{\"row\":1,\"message\":\"Unknown activity\"}]}", System.Text.Encoding.UTF8, "application/json")
                }
            };
            var tokens = new InMemoryTokenStore();
            await tokens.SaveAsync(new StoredToken("abc", DateTime.UtcNow.AddHours(1), "tester@x.com"));
            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var userId = await SeedDayAsync(scope.ServiceProvider);
            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();
            var day = new DateOnly(2026, 9, 28);
            var now = new DateTime(2026, 9, 29, 10, 0, 0);

            var outcome = await service.SendDayAsync(userId, day, now, DateTime.UtcNow);

            Assert.Equal(SendStatus.Rejected, outcome.Status);
            var err = Assert.Single(outcome.Errors);
            Assert.Equal("Row 1: Unknown activity", err);

            var history = await service.GetHistoryAsync();
            Assert.Empty(history);
        }

        //-----------------------------
        //network offline returns offline status without affecting local data
        [Fact]
        public async Task SendDay_OfflineKeepsDayLocal()
        {
            var handler = new FakeDashboardHandler { ThrowOffline = true };
            var tokens = new InMemoryTokenStore();
            await tokens.SaveAsync(new StoredToken("abc", DateTime.UtcNow.AddHours(1), "tester@x.com"));
            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var userId = await SeedDayAsync(scope.ServiceProvider);
            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();
            var day = new DateOnly(2026, 9, 28);
            var now = new DateTime(2026, 9, 29, 10, 0, 0);

            var outcome = await service.SendDayAsync(userId, day, now, DateTime.UtcNow);

            Assert.Equal(SendStatus.Offline, outcome.Status);

            var history = await service.GetHistoryAsync();
            Assert.Empty(history);

            var preview = await service.PreviewDayAsync(userId, day, now);
            Assert.Single(preview.Rows);
        }

        //-----------------------------
        //sign in saves token only when server responds 200 OK
        [Fact]
        public async Task SignIn_SavesTokenOnlyOnSuccess()
        {
            var handler = new FakeDashboardHandler
            {
                Respond = _ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
            };
            var tokens = new InMemoryTokenStore();
            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();

            var outcome1 = await service.SignInAsync("user@test.com", "wrong");
            Assert.Equal(SignInStatus.InvalidDetails, outcome1.Status);
            Assert.Null(await tokens.LoadAsync());

            handler.Respond = _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"accessToken\":\"t1\",\"tokenType\":\"Bearer\",\"expiresAtUtc\":\"2030-01-01T00:00:00Z\"}", System.Text.Encoding.UTF8, "application/json")
            };

            var outcome2 = await service.SignInAsync("user@test.com", "secret");
            Assert.Equal(SignInStatus.SignedIn, outcome2.Status);
            var saved = await tokens.LoadAsync();
            Assert.NotNull(saved);
            Assert.Equal("t1", saved.AccessToken);
        }

        //-----------------------------
        //dashboard address validation requires https except on loopback
        [Fact]
        public void ValidateDashboardUrl_RequiresHttps()
        {
            Assert.Throws<ArgumentException>(() => SyncServiceCollectionExtensions.ValidateDashboardUrl("http://example.com"));

            var local = SyncServiceCollectionExtensions.ValidateDashboardUrl("http://localhost:5075");
            Assert.Equal("http://localhost:5075/", local.ToString());

            var https = SyncServiceCollectionExtensions.ValidateDashboardUrl("https://example.com");
            Assert.Equal("https://example.com/", https.ToString());
        }

        //-----------------------------
        //sending the same day twice replaces the earlier history entry
        [Fact]
        public async Task History_ReplacesSameDay()
        {
            var store = new FileSendHistoryStore(_historyPath);
            var day = new DateOnly(2026, 9, 28);

            await store.AddAsync(new SentDay(day, DateTime.UtcNow.AddHours(-1), 1, 8.0));
            await store.AddAsync(new SentDay(day, DateTime.UtcNow, 2, 7.5));

            var history = await store.GetAsync();
            var sent = Assert.Single(history);
            Assert.Equal(2, sent.Entries);
            Assert.Equal(7.5, sent.Hours);
        }

        //-----------------------------
        //address resolution prefers explicit code over environment and default
        [Fact]
        public void ResolveDashboardUrl_PrefersCodeThenEnvironmentThenDefault()
        {
            var first = SyncServiceCollectionExtensions.ResolveDashboardUrl("https://a.test/", "https://b.test/");
            Assert.Equal("https://a.test/", first);

            var second = SyncServiceCollectionExtensions.ResolveDashboardUrl(null, "https://localhost:7043/");
            Assert.Equal("https://localhost:7043/", second);

            var third = SyncServiceCollectionExtensions.ResolveDashboardUrl(null, null);
            Assert.Equal(SyncServiceCollectionExtensions.DefaultDashboardUrl, third);
        }

        //-----------------------------
        //sign in stores access token refresh token and expiry dates
        [Fact]
        public async Task SignIn_StoresRefreshTokenAndExpiry()
        {
            var handler = new FakeDashboardHandler
            {
                Respond = _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"accessToken\":\"acc1\",\"tokenType\":\"Bearer\",\"expiresAtUtc\":\"2030-01-01T00:00:00Z\",\"refreshToken\":\"ref1\",\"refreshExpiresAtUtc\":\"2030-02-01T00:00:00Z\"}", System.Text.Encoding.UTF8, "application/json")
                }
            };
            var tokens = new InMemoryTokenStore();
            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();
            var outcome = await service.SignInAsync("user@test.com", "pass");

            Assert.Equal(SignInStatus.SignedIn, outcome.Status);
            var saved = await tokens.LoadAsync();
            Assert.NotNull(saved);
            Assert.Equal("acc1", saved.AccessToken);
            Assert.Equal("ref1", saved.RefreshToken);
            Assert.Equal(new DateTime(2030, 2, 1, 0, 0, 0, DateTimeKind.Utc), saved.RefreshExpiresAtUtc);
        }

        //-----------------------------
        //expired access token with valid refresh token refreshes silently before sending
        [Fact]
        public async Task SendDay_ExpiredAccessToken_RefreshesSilentlyAndSends()
        {
            var calls = new List<string>();
            var handler = new FakeDashboardHandler
            {
                Respond = request =>
                {
                    calls.Add(request.RequestUri!.AbsolutePath);
                    if (request.RequestUri.AbsolutePath.EndsWith("/refresh"))
                    {
                        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"accessToken\":\"acc2\",\"tokenType\":\"Bearer\",\"expiresAtUtc\":\"2030-01-01T00:00:00Z\",\"refreshToken\":\"ref2\",\"refreshExpiresAtUtc\":\"2030-02-01T00:00:00Z\"}", System.Text.Encoding.UTF8, "application/json")
                        };
                    }
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"created\":1,\"skipped\":0,\"errors\":[]}", System.Text.Encoding.UTF8, "application/json")
                    };
                }
            };
            var tokens = new InMemoryTokenStore();
            var expiredAccess = DateTime.UtcNow.AddMinutes(-5);
            var validRefresh = DateTime.UtcNow.AddDays(10);
            await tokens.SaveAsync(new StoredToken("acc1", expiredAccess, "user@test.com", "ref1", validRefresh));

            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var userId = await SeedDayAsync(scope.ServiceProvider);
            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();
            var day = new DateOnly(2026, 9, 28);
            var now = new DateTime(2026, 9, 29, 10, 0, 0);

            var outcome = await service.SendDayAsync(userId, day, now, DateTime.UtcNow);

            Assert.Equal(SendStatus.Sent, outcome.Status);
            Assert.Equal(new[] { "/api/v1/auth/refresh", "/api/v1/timesheets/import" }, calls);

            var saved = await tokens.LoadAsync();
            Assert.NotNull(saved);
            Assert.Equal("acc2", saved.AccessToken);
            Assert.Equal("ref2", saved.RefreshToken);
        }

        //-----------------------------
        //refused refresh token clears token store and requires sign in
        [Fact]
        public async Task SendDay_RefreshRefused401_ReturnsNeedsSignInAndClearsStore()
        {
            var handler = new FakeDashboardHandler
            {
                Respond = request => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
            };
            var tokens = new InMemoryTokenStore();
            var expiredAccess = DateTime.UtcNow.AddMinutes(-5);
            var validRefresh = DateTime.UtcNow.AddDays(10);
            await tokens.SaveAsync(new StoredToken("acc1", expiredAccess, "user@test.com", "ref1", validRefresh));

            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var userId = await SeedDayAsync(scope.ServiceProvider);
            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();
            var day = new DateOnly(2026, 9, 28);
            var now = new DateTime(2026, 9, 29, 10, 0, 0);

            var outcome = await service.SendDayAsync(userId, day, now, DateTime.UtcNow);

            Assert.Equal(SendStatus.NeedsSignIn, outcome.Status);
            Assert.Null(await tokens.LoadAsync());
        }

        //-----------------------------
        //network offline during refresh returns offline status and keeps stored token
        [Fact]
        public async Task SendDay_OfflineDuringRefresh_ReturnsOfflineAndKeepsStore()
        {
            var handler = new FakeDashboardHandler { ThrowOffline = true };
            var tokens = new InMemoryTokenStore();
            var expiredAccess = DateTime.UtcNow.AddMinutes(-5);
            var validRefresh = DateTime.UtcNow.AddDays(10);
            await tokens.SaveAsync(new StoredToken("acc1", expiredAccess, "user@test.com", "ref1", validRefresh));

            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var userId = await SeedDayAsync(scope.ServiceProvider);
            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();
            var day = new DateOnly(2026, 9, 28);
            var now = new DateTime(2026, 9, 29, 10, 0, 0);

            var outcome = await service.SendDayAsync(userId, day, now, DateTime.UtcNow);

            Assert.Equal(SendStatus.Offline, outcome.Status);
            var saved = await tokens.LoadAsync();
            Assert.NotNull(saved);
            Assert.Equal("acc1", saved.AccessToken);
        }

        //-----------------------------
        //import 401 with valid looking access token refreshes once and retries import
        [Fact]
        public async Task SendDay_ImportRefused401_RefreshesAndRetries()
        {
            var calls = new List<string>();
            var handler = new FakeDashboardHandler
            {
                Respond = request =>
                {
                    calls.Add(request.RequestUri!.AbsolutePath);
                    if (calls.Count == 1)
                    {
                        return new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
                    }
                    if (request.RequestUri.AbsolutePath.EndsWith("/refresh"))
                    {
                        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"accessToken\":\"acc2\",\"tokenType\":\"Bearer\",\"expiresAtUtc\":\"2030-01-01T00:00:00Z\",\"refreshToken\":\"ref2\",\"refreshExpiresAtUtc\":\"2030-02-01T00:00:00Z\"}", System.Text.Encoding.UTF8, "application/json")
                        };
                    }
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"created\":1,\"skipped\":0,\"errors\":[]}", System.Text.Encoding.UTF8, "application/json")
                    };
                }
            };
            var tokens = new InMemoryTokenStore();
            await tokens.SaveAsync(new StoredToken("acc1", DateTime.UtcNow.AddHours(1), "user@test.com", "ref1", DateTime.UtcNow.AddDays(10)));

            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var userId = await SeedDayAsync(scope.ServiceProvider);
            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();
            var day = new DateOnly(2026, 9, 28);
            var now = new DateTime(2026, 9, 29, 10, 0, 0);

            var outcome = await service.SendDayAsync(userId, day, now, DateTime.UtcNow);

            Assert.Equal(SendStatus.Sent, outcome.Status);
            Assert.Equal(new[] { "/api/v1/timesheets/import", "/api/v1/auth/refresh", "/api/v1/timesheets/import" }, calls);
        }

        //-----------------------------
        //expired token without refresh token prompts sign in without calling refresh
        [Fact]
        public async Task SendDay_ExpiredTokenWithoutRefreshToken_NeedsSignInWithoutRefreshCall()
        {
            var handler = new FakeDashboardHandler();
            var tokens = new InMemoryTokenStore();
            await tokens.SaveAsync(new StoredToken("acc1", DateTime.UtcNow.AddMinutes(-5), "user@test.com", null, null));

            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var userId = await SeedDayAsync(scope.ServiceProvider);
            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();
            var day = new DateOnly(2026, 9, 28);
            var now = new DateTime(2026, 9, 29, 10, 0, 0);

            var outcome = await service.SendDayAsync(userId, day, now, DateTime.UtcNow);

            Assert.Equal(SendStatus.NeedsSignIn, outcome.Status);
            Assert.Equal(0, handler.Calls);
        }

        //-----------------------------
        //is signed in returns true with expired access token and valid refresh token
        [Fact]
        public async Task IsSignedInAsync_ChecksRefreshValidityWhenAccessExpired()
        {
            var handler = new FakeDashboardHandler();
            var tokens = new InMemoryTokenStore();
            var now = DateTime.UtcNow;

            await tokens.SaveAsync(new StoredToken("acc1", now.AddMinutes(-5), "user@test.com", "ref1", now.AddDays(1)));

            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();

            Assert.True(await service.IsSignedInAsync(now));

            await tokens.SaveAsync(new StoredToken("acc1", now.AddMinutes(-5), "user@test.com", "ref1", now.AddMinutes(-1)));
            Assert.False(await service.IsSignedInAsync(now));
        }

        //-----------------------------
        //sign out posts logout endpoint and clears token store
        [Fact]
        public async Task SignOutAsync_CallsLogoutEndpointAndClearsStore()
        {
            var calls = new List<string>();
            var handler = new FakeDashboardHandler
            {
                Respond = request =>
                {
                    calls.Add(request.RequestUri!.AbsolutePath);
                    return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
                }
            };
            var tokens = new InMemoryTokenStore();
            await tokens.SaveAsync(new StoredToken("acc1", DateTime.UtcNow.AddHours(1), "user@test.com", "ref1", DateTime.UtcNow.AddDays(1)));

            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();

            await service.SignOutAsync();

            Assert.Equal(new[] { "/api/v1/auth/logout" }, calls);
            Assert.Null(await tokens.LoadAsync());

            handler.ThrowOffline = true;
            await tokens.SaveAsync(new StoredToken("acc1", DateTime.UtcNow.AddHours(1), "user@test.com", "ref1", DateTime.UtcNow.AddDays(1)));
            await service.SignOutAsync();
            Assert.Null(await tokens.LoadAsync());
        }

        //-----------------------------
        //concurrent send day calls with expired access token issue only one refresh call
        [Fact]
        public async Task SendDayAsync_ConcurrentCalls_RefreshesOnlyOnce()
        {
            var refreshCount = 0;
            var handler = new FakeDashboardHandler
            {
                Respond = request =>
                {
                    if (request.RequestUri!.AbsolutePath.EndsWith("/refresh"))
                    {
                        Interlocked.Increment(ref refreshCount);
                        Thread.Sleep(50);
                        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"accessToken\":\"acc2\",\"tokenType\":\"Bearer\",\"expiresAtUtc\":\"2030-01-01T00:00:00Z\",\"refreshToken\":\"ref2\",\"refreshExpiresAtUtc\":\"2030-02-01T00:00:00Z\"}", System.Text.Encoding.UTF8, "application/json")
                        };
                    }
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"created\":1,\"skipped\":0,\"errors\":[]}", System.Text.Encoding.UTF8, "application/json")
                    };
                }
            };
            var tokens = new InMemoryTokenStore();
            await tokens.SaveAsync(new StoredToken("acc1", DateTime.UtcNow.AddMinutes(-5), "user@test.com", "ref1", DateTime.UtcNow.AddDays(10)));

            using var host = CreateHost(handler, tokens);
            using var scope = host.CreateScope();

            var userId = await SeedDayAsync(scope.ServiceProvider);
            var service = scope.ServiceProvider.GetRequiredService<DaySendService>();
            var day = new DateOnly(2026, 9, 28);
            var now = new DateTime(2026, 9, 29, 10, 0, 0);

            var task1 = service.SendDayAsync(userId, day, now, DateTime.UtcNow);
            var task2 = service.SendDayAsync(userId, day, now, DateTime.UtcNow);

            var outcome1 = await task1;
            var outcome2 = await task2;

            Assert.Equal(1, refreshCount);
            Assert.Equal(SendStatus.Sent, outcome1.Status);
            Assert.Equal(SendStatus.Sent, outcome2.Status);
        }
    }
}
//------------------------------EOF-----------------------------\\
