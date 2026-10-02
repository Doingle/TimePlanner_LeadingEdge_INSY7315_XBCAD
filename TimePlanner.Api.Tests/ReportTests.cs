using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CsvHelper;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the hours report and the timesheet export: correct totals, who may see whose time, date handling, and a safe csv in the company's layout
    [Collection("Api")]
    public class ReportTests
    {
        //the seeded top level activities
        private const int Meeting = 1, Coding = 2, Learning = 7;
        private const string InternalCompany = "Leading Edge (Internal)";

        private static readonly DateTime Day = DateTime.Today.AddDays(-5);

        private readonly ApiFactory _factory;

        public ReportTests(ApiFactory factory) => _factory = factory;

        private static string Tag() => Guid.NewGuid().ToString("N")[..8];
        private static string D(DateTime d) => d.ToString("yyyy-MM-dd");

        private static string HoursUrl(DateTime from, DateTime to, string? extra = null) => $"/api/v1/reports/hours?from={D(from)}&to={D(to)}{extra}";
        private static string CsvUrl(DateTime from, DateTime to, int? userId = null) =>
            $"/api/v1/reports/timesheet.csv?from={D(from)}&to={D(to)}" + (userId == null ? "" : $"&userId={userId}");

        private async Task<(HttpClient Client, string Email, string Password, int UserId)> NewUserAsync(string role = "Developer")
        {
            var user = await _factory.CreateLinkedUserAsync(role);
            return (await _factory.LoginClientAsync(user.Email, user.Password), user.Email, user.Password, user.AppUserId);
        }

        private static async Task<JsonElement> ReportAsync(HttpClient client, string url)
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        private static Dictionary<string, double> HoursByLabel(JsonElement report) =>
            report.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("label").GetString()!, r => r.GetProperty("hours").GetDouble());

        // ---------- authentication and validation ----------

        [Theory]
        [InlineData("/api/v1/reports/hours?from=2026-09-01&to=2026-09-30")]
        [InlineData("/api/v1/reports/timesheet.csv?from=2026-09-01&to=2026-09-30")]
        public async Task Reports_RequireAToken(string url)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.NewClient().GetAsync(url)).StatusCode);
        }

        [Theory]
        [InlineData("/api/v1/reports/hours")]
        [InlineData("/api/v1/reports/hours?from=2026-09-01")]
        [InlineData("/api/v1/reports/hours?from=2026-09-30&to=2026-09-01")]
        [InlineData("/api/v1/reports/hours?from=2025-01-01&to=2026-06-01")]
        [InlineData("/api/v1/reports/hours?from=2026-09-01&to=2026-09-30&groupBy=Nonsense")]
        [InlineData("/api/v1/reports/timesheet.csv")]
        [InlineData("/api/v1/reports/timesheet.csv?from=2026-09-30&to=2026-09-01")]
        public async Task Reports_RejectInvalidRequests(string url)
        {
            var dev = await NewUserAsync();

            Assert.Equal(HttpStatusCode.BadRequest, (await dev.Client.GetAsync(url)).StatusCode);
        }

        [Fact]
        public async Task Reports_RejectALoginWithoutAProfile()
        {
            var (email, password) = await _factory.CreateUserAsync();
            var client = await _factory.LoginClientAsync(email, password);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(HoursUrl(Day, Day))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(CsvUrl(Day, Day))).StatusCode);
        }

        // ---------- hours ----------

        [Fact]
        public async Task Hours_GroupedByProject_SumsTheTime()
        {
            var dev = await NewUserAsync();
            var acme = "Acme " + Tag();
            await _factory.AddEntryAsync(dev.UserId, acme, "Web", Coding, Day.AddHours(9), 90);
            await _factory.AddEntryAsync(dev.UserId, acme, "Web", Meeting, Day.AddHours(11), 30);
            await _factory.AddEntryAsync(dev.UserId, acme, "Docs", Coding, Day.AddHours(13), 60);

            var report = await ReportAsync(dev.Client, HoursUrl(Day, Day));

            Assert.Equal(3.0, report.GetProperty("totalHours").GetDouble());
            Assert.Equal(3, report.GetProperty("entries").GetInt32());
            var rows = HoursByLabel(report);
            Assert.Equal(2.0, rows[$"{acme} / Web"]);
            Assert.Equal(1.0, rows[$"{acme} / Docs"]);
            Assert.Equal($"{acme} / Web", report.GetProperty("rows")[0].GetProperty("label").GetString());
        }

        [Fact]
        public async Task Hours_BillableMeansAnyCompanyExceptTheInternalOne()
        {
            var dev = await NewUserAsync();
            await _factory.AddEntryAsync(dev.UserId, "Acme " + Tag(), "Web", Coding, Day.AddHours(9), 60);
            await _factory.AddEntryAsync(dev.UserId, "Acme " + Tag(), "Web", Learning, Day.AddHours(10), 60);          // the activity makes no difference
            await _factory.AddEntryAsync(dev.UserId, InternalCompany, "Internal", Coding, Day.AddHours(11), 60);

            var report = await ReportAsync(dev.Client, HoursUrl(Day, Day));

            Assert.Equal(3.0, report.GetProperty("totalHours").GetDouble());
            Assert.Equal(2.0, report.GetProperty("billableHours").GetDouble());
        }

        [Fact]
        public async Task Hours_CanBeGroupedByCompanyActivityDayAndUser()
        {
            var dev = await NewUserAsync();
            var acme = "Acme " + Tag();
            await _factory.AddEntryAsync(dev.UserId, acme, "Web", Coding, Day.AddHours(9), 60);
            await _factory.AddEntryAsync(dev.UserId, acme, "Web", Meeting, Day.AddHours(10), 30);
            await _factory.AddEntryAsync(dev.UserId, acme, "Web", Coding, Day.AddDays(1).AddHours(9), 30);

            var byCompany = HoursByLabel(await ReportAsync(dev.Client, HoursUrl(Day, Day.AddDays(1), "&groupBy=Company")));
            var byActivity = HoursByLabel(await ReportAsync(dev.Client, HoursUrl(Day, Day.AddDays(1), "&groupBy=Activity")));
            var byDay = await ReportAsync(dev.Client, HoursUrl(Day, Day.AddDays(1), "&groupBy=Day"));
            var byUser = HoursByLabel(await ReportAsync(dev.Client, HoursUrl(Day, Day.AddDays(1), "&groupBy=User")));

            Assert.Equal(2.0, byCompany[acme]);
            Assert.Equal(1.5, byActivity["Coding"]);
            Assert.Equal(0.5, byActivity["Meeting"]);
            Assert.Equal(new[] { D(Day), D(Day.AddDays(1)) }, byDay.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("label").GetString()).ToArray());
            Assert.Equal(2.0, Assert.Single(byUser).Value);
        }

        [Fact]
        public async Task Hours_IncludeBothEndDays_AndNothingOutsideThem()
        {
            var dev = await NewUserAsync();
            var acme = "Acme " + Tag();
            await _factory.AddEntryAsync(dev.UserId, acme, "Edge", Coding, Day.AddMinutes(-1), 30);                         // the day before from
            await _factory.AddEntryAsync(dev.UserId, acme, "Edge", Coding, Day, 60);                                        // midnight on from
            await _factory.AddEntryAsync(dev.UserId, acme, "Edge", Coding, Day.AddDays(1).AddHours(23).AddMinutes(30), 20); // 23:30 on to
            await _factory.AddEntryAsync(dev.UserId, acme, "Edge", Coding, Day.AddDays(2), 45);                             // midnight after to

            var report = await ReportAsync(dev.Client, HoursUrl(Day, Day.AddDays(1)));

            Assert.Equal(2, report.GetProperty("entries").GetInt32());
            Assert.Equal(Math.Round(80 / 60.0, 2), report.GetProperty("totalHours").GetDouble());
        }

        [Fact]
        public async Task Hours_AnEmptyPeriodGivesAnEmptyReport()
        {
            var dev = await NewUserAsync();

            var report = await ReportAsync(dev.Client, HoursUrl(Day, Day));

            Assert.Equal(0, report.GetProperty("entries").GetInt32());
            Assert.Empty(report.GetProperty("rows").EnumerateArray());
        }

        // ---------- who may see whose hours ----------

        [Fact]
        public async Task Hours_DeveloperSeesOnlyTheirOwnTime()
        {
            var alice = await NewUserAsync();
            var bob = await NewUserAsync();
            var shared = "Shared " + Tag();
            await _factory.AddEntryAsync(alice.UserId, shared, "Web", Coding, Day.AddHours(9), 60);
            await _factory.AddEntryAsync(bob.UserId, shared, "Web", Coding, Day.AddHours(9), 120);

            var report = await ReportAsync(alice.Client, HoursUrl(Day, Day));

            Assert.Equal(1.0, report.GetProperty("totalHours").GetDouble());
        }

        [Fact]
        public async Task Hours_DeveloperCannotAskForAnotherUser()
        {
            var alice = await NewUserAsync();
            var bob = await NewUserAsync();

            Assert.Equal(HttpStatusCode.Forbidden, (await alice.Client.GetAsync(HoursUrl(Day, Day, $"&userId={bob.UserId}"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await alice.Client.GetAsync(HoursUrl(Day, Day, $"&userId={alice.UserId}"))).StatusCode);
        }

        [Fact]
        public async Task Hours_AnAdminSeesEveryoneOrOnePerson()
        {
            var alice = await NewUserAsync();
            var bob = await NewUserAsync();
            var viewer = await NewUserAsync("Admin");
            var shared = "Shared " + Tag();
            var project = await _factory.AddEntryAsync(alice.UserId, shared, "Web", Coding, Day.AddHours(9), 60);
            await _factory.AddEntryAsync(bob.UserId, shared, "Web", Coding, Day.AddHours(9), 120);

            var everyone = await ReportAsync(viewer.Client, HoursUrl(Day, Day, $"&companyId={project.CompanyId}&groupBy=User"));
            var onlyBob = await ReportAsync(viewer.Client, HoursUrl(Day, Day, $"&companyId={project.CompanyId}&userId={bob.UserId}"));

            Assert.Equal(new[] { alice.Email, bob.Email }.OrderBy(x => x), HoursByLabel(everyone).Keys.OrderBy(x => x));
            Assert.Equal(3.0, everyone.GetProperty("totalHours").GetDouble());
            Assert.Equal(2.0, onlyBob.GetProperty("totalHours").GetDouble());
        }

        // ---------- the timesheet export ----------

        private static async Task<string[]> LinesAsync(HttpResponseMessage response) =>
            (await response.Content.ReadAsStringAsync()).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        [Fact]
        public async Task Csv_UsesTheCompanyLayout()
        {
            var dev = await NewUserAsync();
            var acme = "Acme " + Tag();
            await _factory.AddEntryAsync(dev.UserId, acme, "Web", Coding, Day.AddHours(9).AddMinutes(30), 90, "Built the login page");

            var response = await dev.Client.GetAsync(CsvUrl(Day, Day));
            var lines = await LinesAsync(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Date,Activity/Task,Client / Project,Start Time,End Time,Duration (hours),Notes,Billable", lines[0]);
            Assert.Equal($"{D(Day)},Built the login page,{acme} / Web,09:30,11:00,1.50,Coding,Yes", lines[1]);
            Assert.Equal(2, lines.Length);
        }

        [Fact]
        public async Task Csv_IsADownload_WithAByteOrderMarkForExcel()
        {
            var dev = await NewUserAsync();

            var response = await dev.Client.GetAsync(CsvUrl(Day, Day.AddDays(2)));
            var bytes = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal($"timesheet_{Day:yyyyMMdd}_{Day.AddDays(2):yyyyMMdd}.csv", response.Content.Headers.ContentDisposition!.FileName);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        }

        [Fact]
        public async Task Csv_FillsTheBlanksTheCompanyWay()
        {
            var dev = await NewUserAsync();
            var acme = "Acme " + Tag();
            await _factory.AddEntryAsync(dev.UserId, acme, "Web", Learning, Day.AddHours(8), 60);                    // no note
            await _factory.AddEntryAsync(dev.UserId, InternalCompany, "Internal", Meeting, Day.AddHours(9), 30, "Stand-up");  // internal
            await _factory.AddEntryAsync(dev.UserId, acme, acme, Coding, Day.AddHours(10), 60, "Same name");         // project named like the client

            var lines = await LinesAsync(await dev.Client.GetAsync(CsvUrl(Day, Day)));

            Assert.Equal($"{D(Day)},Learning,{acme} / Web,08:00,09:00,1.00,Learning,Yes", lines[1]);
            Assert.Equal($"{D(Day)},Stand-up,{InternalCompany},09:00,09:30,0.50,Meeting,Internal", lines[2]);
            Assert.Equal($"{D(Day)},Same name,{acme},10:00,11:00,1.00,Coding,Yes", lines[3]);
        }

        [Theory]
        [InlineData("=HYPERLINK(\"http://evil\",\"x\")")]
        [InlineData("+1+1")]
        [InlineData("-2+3")]
        [InlineData("@SUM(A1)")]
        public async Task Csv_NeutralisesFormulas(string note)
        {
            var dev = await NewUserAsync();
            await _factory.AddEntryAsync(dev.UserId, "Acme " + Tag(), "Web", Coding, Day.AddHours(9), 60, note);

            var text = (await (await dev.Client.GetAsync(CsvUrl(Day, Day))).Content.ReadAsStringAsync()).TrimStart('﻿');
            using var csv = new CsvReader(new StringReader(text), CultureInfo.InvariantCulture);
            csv.Read();
            csv.ReadHeader();
            csv.Read();

            Assert.Equal("'" + note, csv.GetField("Activity/Task"));
        }

        [Fact]
        public async Task Csv_QuotesCommasQuotesAndNewlines()
        {
            var dev = await NewUserAsync();
            var note = "fixed \"it\", then\r\ntested";
            await _factory.AddEntryAsync(dev.UserId, "Acme " + Tag(), "Web", Coding, Day.AddHours(9), 60, note);

            var text = (await (await dev.Client.GetAsync(CsvUrl(Day, Day))).Content.ReadAsStringAsync()).TrimStart('﻿');
            using var csv = new CsvReader(new StringReader(text), CultureInfo.InvariantCulture);
            csv.Read();
            csv.ReadHeader();
            csv.Read();

            Assert.Equal(note, csv.GetField("Activity/Task"));
            Assert.Equal(8, csv.HeaderRecord!.Length);
        }

        [Fact]
        public async Task Csv_HasOnlyTheHeaderWhenNothingWasLogged()
        {
            var dev = await NewUserAsync();

            var lines = await LinesAsync(await dev.Client.GetAsync(CsvUrl(Day, Day)));

            Assert.Single(lines);
        }

        [Fact]
        public async Task Csv_ContainsOnlyTheRequestedPeopleAndDays()
        {
            var alice = await NewUserAsync();
            var bob = await NewUserAsync();
            await _factory.AddEntryAsync(alice.UserId, "Acme " + Tag(), "Web", Coding, Day.AddHours(9), 60, "alice");
            await _factory.AddEntryAsync(bob.UserId, "Acme " + Tag(), "Web", Coding, Day.AddHours(9), 60, "bob");
            await _factory.AddEntryAsync(alice.UserId, "Acme " + Tag(), "Web", Coding, Day.AddDays(3).AddHours(9), 60, "alice later");

            var text = await (await alice.Client.GetAsync(CsvUrl(Day, Day.AddDays(1)))).Content.ReadAsStringAsync();

            Assert.Contains("alice", text);
            Assert.DoesNotContain("bob", text);
            Assert.DoesNotContain("alice later", text);
        }

        [Fact]
        public async Task Csv_DeveloperCannotExportAnotherUser_ButAnAdminCan()
        {
            var alice = await NewUserAsync();
            var bob = await NewUserAsync();
            var admin = await NewUserAsync("Admin");
            await _factory.AddEntryAsync(bob.UserId, "Acme " + Tag(), "Web", Coding, Day.AddHours(9), 60, "bobs work");

            Assert.Equal(HttpStatusCode.Forbidden, (await alice.Client.GetAsync(CsvUrl(Day, Day, bob.UserId))).StatusCode);
            var asAdmin = await admin.Client.GetAsync(CsvUrl(Day, Day, bob.UserId));
            Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
            Assert.Contains("bobs work", await asAdmin.Content.ReadAsStringAsync());
        }
    }
}
