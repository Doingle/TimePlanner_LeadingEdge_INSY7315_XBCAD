using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Services;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the audit trail: what is recorded, what must never be, and who may read it. Each test runs its own host so the rows it looks at are only its own
    public class AuditTests
    {
        private const string WrongPassword = "Wrong-Pass-123!";

        private static Task<HttpResponseMessage> ApiLogin(HttpClient client, string email, string password) =>
            client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

        [Fact]
        public async Task ApiLogins_AreRecorded_WhetherTheyWorkOrNot()
        {
            using var factory = new ApiFactory();
            var user = await factory.CreateLinkedUserAsync();
            var client = factory.NewClient();

            await ApiLogin(client, user.Email, WrongPassword);
            await ApiLogin(client, user.Email, user.Password);
            var events = await factory.AuditEventsAsync();

            Assert.Equal(new[] { AuditActions.LoginFailed, AuditActions.LoginSucceeded }, events.Select(e => e.Action).ToArray());
            Assert.All(events, e => { Assert.Equal(user.Email, e.Email); Assert.Equal("api", e.Detail); });
            Assert.NotNull(events[1].UserId);
            Assert.InRange(events[1].TimestampUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        }

        [Fact]
        public async Task ALockout_IsRecordedAsItsOwnAction()
        {
            using var factory = new ApiFactory();
            var user = await factory.CreateLinkedUserAsync();
            var client = factory.NewClient();

            for (var i = 0; i < 5; i++)
                await ApiLogin(client, user.Email, WrongPassword);
            await ApiLogin(client, user.Email, user.Password);

            var actions = (await factory.AuditEventsAsync()).Select(e => e.Action).ToList();
            Assert.Equal(4, actions.Count(a => a == AuditActions.LoginFailed));
            Assert.Equal(2, actions.Count(a => a == AuditActions.LoginLockedOut));
            Assert.DoesNotContain(AuditActions.LoginSucceeded, actions);
        }

        [Fact]
        public async Task WebsiteLoginAndLogout_AreRecorded()
        {
            using var factory = new ApiFactory();
            var user = await factory.CreateLinkedUserAsync();
            var browser = factory.NewClient();

            await ApiFactory.PostLoginAsync(browser, user.Email, WrongPassword);
            await ApiFactory.PostLoginAsync(browser, user.Email, user.Password);
            var token = System.Text.RegularExpressions.Regex.Match(await browser.GetStringAsync("/"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            await browser.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

            var events = await factory.AuditEventsAsync();
            Assert.Equal(new[] { AuditActions.LoginFailed, AuditActions.LoginSucceeded, AuditActions.Logout }, events.Select(e => e.Action).ToArray());
            Assert.All(events, e => { Assert.Equal(user.Email, e.Email); Assert.Equal("website", e.Detail); });
        }

        [Fact]
        public async Task ImportsAndExports_AreRecordedAgainstTheCaller()
        {
            using var factory = new ApiFactory();
            var user = await factory.CreateLinkedUserAsync();
            var client = await factory.LoginClientAsync(user.Email, user.Password);
            var day = DateTime.Today.AddDays(-3);
            string Entry(string activity) => $"{{\"entries\":[{{\"company\":\"Acme\",\"project\":\"Web\",\"activity\":\"{activity}\",\"start\":\"{day.AddHours(9):s}\",\"end\":\"{day.AddHours(10):s}\"}}]}}";

            await client.PostAsync("/api/v1/timesheets/import", new StringContent(Entry("Coding"), System.Text.Encoding.UTF8, "application/json"));
            await client.PostAsync("/api/v1/timesheets/import", new StringContent(Entry("Napping"), System.Text.Encoding.UTF8, "application/json"));
            await client.GetAsync($"/api/v1/reports/timesheet.csv?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}");

            var events = (await factory.AuditEventsAsync()).Where(e => e.Action != AuditActions.LoginSucceeded).ToList();
            Assert.Equal(new[] { AuditActions.TimesheetImported, AuditActions.TimesheetImportRejected, AuditActions.TimesheetExported }, events.Select(e => e.Action).ToArray());
            Assert.All(events, e => Assert.Equal(user.Email, e.Email));
            Assert.Contains("created 1", events[0].Detail);
            Assert.Contains("1 problem(s)", events[1].Detail);
            Assert.Contains("1 entries", events[2].Detail);
        }

        [Fact]
        public async Task Passwords_AreNeverWrittenToTheAuditTrail()
        {
            using var factory = new ApiFactory();
            var user = await factory.CreateLinkedUserAsync();
            var client = factory.NewClient();
            var browser = factory.NewClient();

            await ApiLogin(client, user.Email, WrongPassword);
            await ApiLogin(client, user.Email, user.Password);
            await ApiFactory.PostLoginAsync(browser, user.Email, WrongPassword);
            await ApiFactory.PostLoginAsync(browser, user.Email, user.Password);

            var everything = JsonSerializer.Serialize(await factory.AuditEventsAsync());
            Assert.DoesNotContain(WrongPassword, everything);
            Assert.DoesNotContain(user.Password, everything);
        }

        [Fact]
        public async Task TextFromARequest_IsShortenedAndStrippedOfControlCharacters()
        {
            using var factory = new ApiFactory();
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<AuditLogger>().LogAsync(
                    AuditActions.LoginFailed, "first line\r\nFORGED: LoginSucceeded for admin", email: new string('a', 400) + "\n@x");
            }

            var row = Assert.Single(await factory.AuditEventsAsync());
            Assert.Equal("first lineFORGED: LoginSucceeded for admin", row.Detail);
            Assert.Equal(256, row.Email!.Length);
            Assert.DoesNotContain('\n', row.Email);
        }

        // ---------- reading the trail ----------

        [Fact]
        public async Task OnlyAdministratorsCanReadTheTrail()
        {
            using var factory = new ApiFactory();
            var developer = await factory.CreateLinkedUserAsync("Developer");
            var billing = await factory.CreateLinkedUserAsync("Billing");

            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().GetAsync("/api/v1/audit")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.LoginClientAsync(developer.Email, developer.Password)).GetAsync("/api/v1/audit")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await (await factory.LoginClientAsync(billing.Email, billing.Password)).GetAsync("/api/v1/audit")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await (await factory.LoginClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword)).GetAsync("/api/v1/audit")).StatusCode);
        }

        [Fact]
        public async Task TheTrail_ListsNewestFirst_CanBeFilteredAndLimited()
        {
            using var factory = new ApiFactory();
            var user = await factory.CreateLinkedUserAsync();
            var plain = factory.NewClient();
            await ApiLogin(plain, user.Email, WrongPassword);
            await ApiLogin(plain, user.Email, WrongPassword);
            await ApiLogin(plain, user.Email, user.Password);
            var admin = await factory.LoginClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            var all = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/audit"))!;
            var failed = (await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/audit?action={AuditActions.LoginFailed}"))!;
            var latestTwo = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/audit?take=2"))!;

            Assert.Equal(all.Select(e => e.GetProperty("id").GetInt32()).OrderByDescending(x => x), all.Select(e => e.GetProperty("id").GetInt32()));
            Assert.Equal(2, failed.Length);
            Assert.All(failed, e => Assert.Equal(AuditActions.LoginFailed, e.GetProperty("action").GetString()));
            Assert.Equal(2, latestTwo.Length);
            Assert.Equal(all[0].GetProperty("id").GetInt32(), latestTwo[0].GetProperty("id").GetInt32());
        }
    }
}
