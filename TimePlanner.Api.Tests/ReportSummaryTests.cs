using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the summary behind My Reports and the admin overview: totals, billable against non-billable, and the breakdowns by category, project and person.
    //the host's clock is fixed at Wednesday 16 September 2026, so "this week" is 14 to 20 September
    public class ReportSummaryTests : IClassFixture<ClockedFactory>
    {
        private const int Meeting = 1, Coding = 2, Email = 4, Learning = 7;
        private const string InternalCompany = "Leading Edge (Internal)";

        private readonly ClockedFactory _factory;

        public ReportSummaryTests(ClockedFactory factory) => _factory = factory;

        private async Task<(HttpClient Client, int ProfileId, string Email)> PersonAsync(string role = "Developer")
        {
            var user = await _factory.CreateLinkedUserAsync(role);
            return (await _factory.LoginClientAsync(user.Email, user.Password), user.AppUserId, user.Email);
        }

        private static async Task<JsonElement> SummaryAsync(HttpClient client, string query = "") =>
            await client.GetFromJsonAsync<JsonElement>("/api/v1/reports/summary" + query);

        private static string[] Labels(JsonElement rows) => rows.EnumerateArray().Select(r => r.GetProperty("label").GetString()!).ToArray();

        // ---------- totals ----------

        [Fact]
        public async Task TheSummary_SplitsBillableFromNonBillable_AndCountsEntries()
        {
            var person = await PersonAsync();
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 120);
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Learning, ClockedFactory.Tuesday.AddHours(9), 60);                 // the activity does not matter
            await _factory.AddEntryAsync(person.ProfileId, InternalCompany, "Internal", Email, ClockedFactory.Wednesday.AddHours(9), 60);    // internal

            var s = await SummaryAsync(person.Client);

            Assert.Equal(4.0, s.GetProperty("totalHours").GetDouble());
            Assert.Equal(240, s.GetProperty("totalMinutes").GetInt32());
            Assert.Equal(2.0, s.GetProperty("billableHours").GetDouble());
            Assert.Equal(2.0, s.GetProperty("nonBillableHours").GetDouble());
            Assert.Equal(3, s.GetProperty("entries").GetInt32());
        }

        [Fact]
        public async Task AnEmptyPeriod_GivesZerosAndNoRows()
        {
            var person = await PersonAsync();

            var s = await SummaryAsync(person.Client);

            Assert.Equal(0.0, s.GetProperty("totalHours").GetDouble());
            Assert.Equal(0, s.GetProperty("entries").GetInt32());
            Assert.Empty(s.GetProperty("byCategory").EnumerateArray());
            Assert.Empty(s.GetProperty("byProject").EnumerateArray());
            Assert.Empty(s.GetProperty("byPerson").EnumerateArray());
        }

        // ---------- the breakdowns ----------

        [Fact]
        public async Task Categories_AreTheTopLevelActivities_LargestFirst_WithSharesThatAddUp()
        {
            var person = await PersonAsync();
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60);
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Meeting, ClockedFactory.Monday.AddHours(11), 120);
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Email, ClockedFactory.Monday.AddHours(14), 60);
            //a sub activity adds to its parent
            await person.Client.PostAsJsonAsync("/api/v1/timesheets/import", new
            {
                entries = new[] { new { company = "Acme", project = "Web", activity = "Coding > Backend", start = ClockedFactory.Tuesday.AddHours(9).ToString("s"), end = ClockedFactory.Tuesday.AddHours(10).ToString("s") } }
            });

            var categories = (await SummaryAsync(person.Client)).GetProperty("byCategory");

            //Meeting 120 and Coding 120 (60 + the sub activity) tie, then Email 60: the largest come first and ties are in alphabetical order
            Assert.Equal(new[] { "Coding", "Meeting", "Email" }, Labels(categories));
            var rows = categories.EnumerateArray().ToArray();
            Assert.Equal(120, rows.Single(r => r.GetProperty("label").GetString() == "Coding").GetProperty("minutes").GetInt32());
            Assert.Equal(2, rows.Single(r => r.GetProperty("label").GetString() == "Coding").GetProperty("entries").GetInt32());
            Assert.Equal(100.0, Math.Round(rows.Sum(r => r.GetProperty("percent").GetDouble())), 0);
            Assert.True(rows.Zip(rows.Skip(1)).All(p => p.First.GetProperty("minutes").GetInt32() >= p.Second.GetProperty("minutes").GetInt32()));
        }

        [Fact]
        public async Task Projects_CarryTheirBillableFlag()
        {
            var person = await PersonAsync();
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 90);
            await _factory.AddEntryAsync(person.ProfileId, InternalCompany, "Internal", Email, ClockedFactory.Monday.AddHours(11), 30);

            var projects = (await SummaryAsync(person.Client)).GetProperty("byProject").EnumerateArray().ToArray();

            Assert.Equal("Acme / Web", projects[0].GetProperty("label").GetString());
            Assert.True(projects[0].GetProperty("billable").GetBoolean());
            Assert.Equal(75.0, projects[0].GetProperty("percent").GetDouble());
            Assert.False(projects[1].GetProperty("billable").GetBoolean());
        }

        [Fact]
        public async Task SharesAreRoundedToOneDecimal()
        {
            var person = await PersonAsync();
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "A", Coding, ClockedFactory.Monday.AddHours(9), 60);
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "B", Coding, ClockedFactory.Monday.AddHours(11), 120);

            var projects = (await SummaryAsync(person.Client)).GetProperty("byProject").EnumerateArray().ToArray();

            Assert.Equal(66.7, projects[0].GetProperty("percent").GetDouble());
            Assert.Equal(33.3, projects[1].GetProperty("percent").GetDouble());
        }

        // ---------- periods ----------

        [Fact]
        public async Task ThePeriod_IsTheWeekByDefault_AMonthOnRequest_OrAnyRange()
        {
            var person = await PersonAsync();
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60);                          // this week
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddDays(-9).AddHours(9), 120);              // 5 Sep, this month
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddDays(-20).AddHours(9), 240);             // 25 Aug

            Assert.Equal(1.0, (await SummaryAsync(person.Client)).GetProperty("totalHours").GetDouble());
            Assert.Equal(3.0, (await SummaryAsync(person.Client, "?view=month")).GetProperty("totalHours").GetDouble());
            Assert.Equal(4.0, (await SummaryAsync(person.Client, "?view=month&date=2026-08-10")).GetProperty("totalHours").GetDouble());
            Assert.Equal(7.0, (await SummaryAsync(person.Client, "?from=2026-08-01&to=2026-09-30")).GetProperty("totalHours").GetDouble());
            Assert.Equal("2026-09-14T00:00:00", (await SummaryAsync(person.Client)).GetProperty("from").GetString());
        }

        [Theory]
        [InlineData("?view=year")]
        [InlineData("?from=2026-09-20&to=2026-09-14")]
        [InlineData("?from=2026-09-14")]
        [InlineData("?to=2026-09-14")]
        [InlineData("?from=2024-01-01&to=2026-09-30")]
        public async Task InvalidPeriods_AreRejected(string query)
        {
            var person = await PersonAsync();

            Assert.Equal(HttpStatusCode.BadRequest, (await person.Client.GetAsync("/api/v1/reports/summary" + query)).StatusCode);
        }

        // ---------- who sees whose time ----------

        [Fact]
        public async Task ADeveloper_SeesOnlyTheirOwnTime_AndAnAdminSeesEveryone()
        {
            var alice = await PersonAsync();
            var bob = await PersonAsync();
            var admin = await PersonAsync("Admin");
            await _factory.AddEntryAsync(alice.ProfileId, "Shared", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60);
            await _factory.AddEntryAsync(bob.ProfileId, "Shared", "Web", Coding, ClockedFactory.Monday.AddHours(9), 120);

            var aliceSees = await SummaryAsync(alice.Client);
            var adminSees = await SummaryAsync(admin.Client);
            var adminForBob = await SummaryAsync(admin.Client, $"?userId={bob.ProfileId}");

            Assert.Equal(1.0, aliceSees.GetProperty("totalHours").GetDouble());
            Assert.Equal(new[] { alice.Email }, Labels(aliceSees.GetProperty("byPerson")));
            //the admin sees both, the larger (Bob, 2h) before the smaller (Alice, 1h)
            var people = Labels(adminSees.GetProperty("byPerson")).ToList();
            Assert.True(people.IndexOf(bob.Email) >= 0 && people.IndexOf(alice.Email) > people.IndexOf(bob.Email));
            Assert.True(adminSees.GetProperty("totalHours").GetDouble() >= 3.0);
            Assert.Equal(2.0, adminForBob.GetProperty("totalHours").GetDouble());
        }

        [Fact]
        public async Task ADeveloper_CannotAskForSomeoneElse_AndAnonymousOrUnlinkedCallersAreRefused()
        {
            var alice = await PersonAsync();
            var bob = await PersonAsync();
            var (email, password) = await _factory.CreateUserAsync();
            var unlinked = await _factory.LoginClientAsync(email, password);

            Assert.Equal(HttpStatusCode.Forbidden, (await alice.Client.GetAsync($"/api/v1/reports/summary?userId={bob.ProfileId}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync($"/api/v1/reports/summary?userId={alice.ProfileId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await unlinked.GetAsync("/api/v1/reports/summary")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.NewClient().GetAsync("/api/v1/reports/summary")).StatusCode);
        }
    }
}
