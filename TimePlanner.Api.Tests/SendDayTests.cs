using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Sync;
using TimePlanner.Dashboard.Services.Submissions;
using Xunit;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //integration tests for replacing days on import and sending days from the widget client
    [Collection("Api")]
    public class SendDayTests
    {
        private const string ImportUrl = "/api/v1/timesheets/import";
        private readonly ApiFactory _factory;

        public SendDayTests(ApiFactory factory) => _factory = factory;

        //-----------------------------
        //creates a test entry dictionary for json requests
        private static object MakeEntry(string startIso, string endIso, string note = "work") => new
        {
            company = "Acme",
            project = "Portal",
            activity = "Coding",
            start = startIso,
            end = endIso,
            note,
            method = "Manual"
        };

        //-----------------------------
        //resending with replaceDays swaps entries for that day only
        [Fact]
        public async Task ResendWithReplaceDays_ReplacesThatDayOnly()
        {
            var user = await _factory.CreateLinkedUserAsync();
            var client = await _factory.LoginClientAsync(user.Email, user.Password);

            var initialRows = new[]
            {
                MakeEntry("2026-09-21T09:00:00", "2026-09-21T10:00:00"),
                MakeEntry("2026-09-21T10:00:00", "2026-09-21T11:00:00"),
                MakeEntry("2026-09-22T09:00:00", "2026-09-22T12:00:00")
            };

            var res1 = await client.PostAsJsonAsync(ImportUrl, new { entries = initialRows });
            Assert.True(res1.IsSuccessStatusCode);

            var replacementRows = new[]
            {
                MakeEntry("2026-09-21T09:00:00", "2026-09-21T10:15:00")
            };

            var res2 = await client.PostAsJsonAsync(ImportUrl, new { entries = replacementRows, replaceDays = true });
            Assert.True(res2.IsSuccessStatusCode);

            using var scope = _factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>();

            var sep21 = await repo.GetForUserAsync(user.AppUserId, new DateTime(2026, 9, 21), new DateTime(2026, 9, 21, 23, 59, 59));
            var single21 = Assert.Single(sep21);
            Assert.Equal(new DateTime(2026, 9, 21, 9, 0, 0), single21.StartTime);
            Assert.Equal(new DateTime(2026, 9, 21, 10, 15, 0), single21.EndTime);

            var sep22 = await repo.GetForUserAsync(user.AppUserId, new DateTime(2026, 9, 22), new DateTime(2026, 9, 22, 23, 59, 59));
            Assert.Single(sep22);
        }

        //-----------------------------
        //resending without replaceDays keeps standard duplicate skip behaviour
        [Fact]
        public async Task ResendWithoutReplaceDays_KeepsSkipBehaviour()
        {
            var user = await _factory.CreateLinkedUserAsync();
            var client = await _factory.LoginClientAsync(user.Email, user.Password);
            var rows = new[] { MakeEntry("2026-09-21T09:00:00", "2026-09-21T10:00:00") };

            var res1 = await client.PostAsJsonAsync(ImportUrl, new { entries = rows });
            Assert.True(res1.IsSuccessStatusCode);

            var res2 = await client.PostAsJsonAsync(ImportUrl, new { entries = rows });
            Assert.True(res2.IsSuccessStatusCode);

            var body = await res2.Content.ReadFromJsonAsync<ImportResultBody>();
            Assert.NotNull(body);
            Assert.Equal(0, body.Created);
            Assert.Equal(1, body.Skipped);
        }

        //-----------------------------
        //replaceDays deletes entries only for the sending user
        [Fact]
        public async Task ReplaceDays_NeverTouchesOtherUsers()
        {
            var userA = await _factory.CreateLinkedUserAsync();
            var userB = await _factory.CreateLinkedUserAsync();

            var clientA = await _factory.LoginClientAsync(userA.Email, userA.Password);
            var clientB = await _factory.LoginClientAsync(userB.Email, userB.Password);

            var row = new[] { MakeEntry("2026-09-21T09:00:00", "2026-09-21T10:00:00") };

            await clientA.PostAsJsonAsync(ImportUrl, new { entries = row });
            await clientB.PostAsJsonAsync(ImportUrl, new { entries = row });

            var replacementA = new[] { MakeEntry("2026-09-21T09:00:00", "2026-09-21T11:00:00") };
            await clientA.PostAsJsonAsync(ImportUrl, new { entries = replacementA, replaceDays = true });

            using var scope = _factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>();

            var entriesB = await repo.GetForUserAsync(userB.AppUserId, new DateTime(2026, 9, 21), new DateTime(2026, 9, 21, 23, 59, 59));
            var singleB = Assert.Single(entriesB);
            Assert.Equal(new DateTime(2026, 9, 21, 10, 0, 0), singleB.EndTime);
        }

        //-----------------------------
        //dashboard client signs in and sends day end to end
        [Fact]
        public async Task WidgetClient_SendsDayEndToEnd()
        {
            var user = await _factory.CreateLinkedUserAsync();
            var client = new DashboardClient(_factory.NewClient());

            var login = await client.SignInAsync(user.Email, user.Password);
            Assert.Equal(SignInStatus.SignedIn, login.Status);
            Assert.NotNull(login.Token);

            var rows = new[]
            {
                new DayEntryPayload(
                    "Acme",
                    "Portal",
                    "Coding",
                    DateTime.SpecifyKind(new DateTime(2026, 9, 21, 9, 0, 0), DateTimeKind.Unspecified),
                    DateTime.SpecifyKind(new DateTime(2026, 9, 21, 10, 0, 0), DateTimeKind.Unspecified),
                    "e2e test",
                    "Manual")
            };

            var send = await client.SendDayAsync(login.Token.AccessToken, rows);
            Assert.Equal(SendStatus.Sent, send.Status);
            Assert.Equal(1, send.Created);

            using var scope = _factory.Services.CreateScope();
            var submissions = scope.ServiceProvider.GetRequiredService<SubmissionService>();
            var userSubmissions = await submissions.ForUserAsync(user.AppUserId, new DateTime(2026, 9, 21), new DateTime(2026, 9, 21));

            Assert.True(userSubmissions.ContainsKey(new DateTime(2026, 9, 21)));
        }

        private record ImportResultBody(int Created, int Skipped);
    }
}
//------------------------------EOF-----------------------------\\
