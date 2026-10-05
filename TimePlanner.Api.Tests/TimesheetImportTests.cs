using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers importing worked time over json, csv and the dashboard upload page: stored for the caller only, all or nothing,
    //safe to repeat, and strict about what it accepts
    [Collection("Api")]
    public class TimesheetImportTests
    {
        private const string JsonUrl = "/api/v1/timesheets/import";
        private const string CsvUrl = "/api/v1/timesheets/import/csv";

        //recent enough to pass the no future dates rule on any machine running the tests
        private static readonly DateTime Day = DateTime.Today.AddDays(-3);

        private readonly ApiFactory _factory;

        public TimesheetImportTests(ApiFactory factory) => _factory = factory;

        private static string Tag() => Guid.NewGuid().ToString("N")[..8];
        private static string Json(DateTime time) => time.ToString("s");
        private static string Csv(DateTime time) => time.ToString("yyyy-MM-dd HH:mm");

        //a valid entry, any field can be overridden to make it invalid
        private static Dictionary<string, object?> Entry(string? company = null, string project = "Web", string? activity = "Coding",
            DateTime? start = null, DateTime? end = null, string? note = "work", string? method = null)
        {
            var s = start ?? Day.AddHours(9);
            return new Dictionary<string, object?>
            {
                ["company"] = company ?? "Acme " + Tag(),
                ["project"] = project,
                ["activity"] = activity,
                ["start"] = Json(s),
                ["end"] = Json(end ?? s.AddHours(1)),
                ["note"] = note,
                ["method"] = method
            };
        }

        private static Task<HttpResponseMessage> PostJson(HttpClient client, params object[] entries) =>
            client.PostAsJsonAsync(JsonUrl, new { entries });

        private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
            await response.Content.ReadFromJsonAsync<JsonElement>();

        private static async Task<JsonElement[]> EntriesOfAsync(HttpClient client) =>
            (await client.GetFromJsonAsync<JsonElement[]>($"/api/v1/timeentries?from={Day.Date:s}&to={Day.Date.AddDays(2):s}"))!;

        private async Task<(HttpClient Client, string Email, string Password, int UserId)> NewDeveloperAsync()
        {
            var dev = await _factory.CreateLinkedUserAsync();
            return (await _factory.LoginClientAsync(dev.Email, dev.Password), dev.Email, dev.Password, dev.AppUserId);
        }

        private static MultipartFormDataContent CsvContent(string csv, string fileName = "timesheet.csv", bool bom = false, string? antiForgery = null)
        {
            var bytes = (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new("text/csv");
            var content = new MultipartFormDataContent { { file, "file", fileName } };
            if (antiForgery != null)
                content.Add(new StringContent(antiForgery), "__RequestVerificationToken");
            return content;
        }

        private static string CsvRow(string company, string project = "Web", string activity = "Coding", DateTime? start = null, string note = "work", string method = "Manual")
        {
            var s = start ?? Day.AddHours(9);
            return $"{company},{project},{activity},{Csv(s)},{Csv(s.AddHours(1))},{note},{method}\r\n";
        }

        private const string CsvHeader = "Company,Project,Activity,Start,End,Note,Method\r\n";

        // ---------- authentication and ownership ----------

        [Fact]
        public async Task Import_RequiresAToken()
        {
            var client = _factory.NewClient();

            Assert.Equal(HttpStatusCode.Unauthorized, (await PostJson(client, Entry())).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(CsvUrl, CsvContent(CsvHeader + CsvRow("Acme")))).StatusCode);
        }

        [Fact]
        public async Task Import_AnAccountWithoutAProfileIsRejected()
        {
            var (email, password) = await _factory.CreateUserAsync();
            var client = await _factory.LoginClientAsync(email, password);

            Assert.Equal(HttpStatusCode.Forbidden, (await PostJson(client, Entry())).StatusCode);
        }

        [Fact]
        public async Task Import_StoresEntriesForTheCallerOnly()
        {
            var alice = await NewDeveloperAsync();
            var bob = await NewDeveloperAsync();

            var response = await PostJson(alice.Client, Entry());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var mine = Assert.Single(await EntriesOfAsync(alice.Client));
            Assert.Equal(alice.UserId, mine.GetProperty("userId").GetInt32());
            Assert.Empty(await EntriesOfAsync(bob.Client));
        }

        [Fact]
        public async Task Import_IgnoresAUserIdInTheBody()
        {
            var alice = await NewDeveloperAsync();
            var bob = await NewDeveloperAsync();
            var entry = Entry();
            entry["userId"] = bob.UserId;

            await PostJson(alice.Client, entry);

            Assert.Single(await EntriesOfAsync(alice.Client));
            Assert.Empty(await EntriesOfAsync(bob.Client));
        }

        // ---------- what gets created ----------

        [Fact]
        public async Task Import_CreatesTheCompanyProjectAndSubActivity_AndEntriesAppearInTheApi()
        {
            var dev = await NewDeveloperAsync();
            var company = "Acme " + Tag();

            var response = await PostJson(dev.Client,
                Entry(company, "Website", "Coding > Frontend", Day.AddHours(9), note: "login page"),
                Entry(company, "Website", "Meeting", Day.AddHours(11), note: "standup"));
            var result = await BodyAsync(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, result.GetProperty("created").GetInt32());

            var projects = await dev.Client.GetFromJsonAsync<JsonElement[]>("/api/v1/projects");
            Assert.Contains(projects!, p => p.GetProperty("companyName").GetString() == company && p.GetProperty("name").GetString() == "Website");

            var categories = await dev.Client.GetFromJsonAsync<JsonElement[]>("/api/v1/categories");
            var coding = categories!.Single(c => c.GetProperty("name").GetString() == "Coding" && c.GetProperty("parentId").ValueKind == JsonValueKind.Null);
            Assert.Contains(categories!, c => c.GetProperty("name").GetString() == "Frontend" && c.GetProperty("parentId").GetInt32() == coding.GetProperty("id").GetInt32());

            var entries = await EntriesOfAsync(dev.Client);
            Assert.Equal(2, entries.Length);
            Assert.Equal(new[] { "login page", "standup" }, entries.Select(e => e.GetProperty("note").GetString()).ToArray());
            Assert.All(entries, e => Assert.Equal(60, e.GetProperty("durationMinutes").GetDouble()));
        }

        [Fact]
        public async Task Import_ReusesAnExistingProject_IgnoringCase()
        {
            var dev = await NewDeveloperAsync();
            var seeded = await _factory.SeedWorkAsync(dev.UserId, Day.AddDays(-30));
            var existing = await dev.Client.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{seeded.ProjectId}");
            var company = existing.GetProperty("companyName").GetString()!.ToUpperInvariant();
            var project = existing.GetProperty("name").GetString()!.ToLowerInvariant();

            await PostJson(dev.Client, Entry(company, project));

            var sameCompany = await dev.Client.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects?companyId={seeded.CompanyId}");
            Assert.Single(sameCompany!);
        }

        [Fact]
        public async Task Import_Twice_SkipsEntriesAlreadyStored()
        {
            var dev = await NewDeveloperAsync();
            var entry = Entry();

            await PostJson(dev.Client, entry);
            var second = await BodyAsync(await PostJson(dev.Client, entry));

            Assert.Equal(0, second.GetProperty("created").GetInt32());
            Assert.Equal(1, second.GetProperty("skipped").GetInt32());
            Assert.Single(await EntriesOfAsync(dev.Client));
        }

        // ---------- validation, nothing is stored when anything is wrong ----------

        public static IEnumerable<object[]> InvalidEntries()
        {
            yield return new object[] { "end before start", Entry(start: Day.AddHours(10), end: Day.AddHours(9)), "End must be after Start" };
            yield return new object[] { "too short", Entry(start: Day.AddHours(9), end: Day.AddHours(9).AddSeconds(30)), "between 1 minute and 24 hours" };
            yield return new object[] { "too long", Entry(start: Day.AddHours(1), end: Day.AddHours(1).AddHours(25)), "between 1 minute and 24 hours" };
            yield return new object[] { "in the future", Entry(start: DateTime.Today.AddDays(5), end: DateTime.Today.AddDays(5).AddHours(1)), "future" };
            yield return new object[] { "unknown activity", Entry(activity: "Napping"), "Unknown activity" };
            yield return new object[] { "activity too deep", Entry(activity: "Coding > A > B > C"), "levels deep" };
            yield return new object[] { "blank activity", Entry(activity: " "), "Activity is required" };
            yield return new object[] { "formula company", Entry(company: "=HYPERLINK(\"http://evil\")"), "cannot start with" };
            yield return new object[] { "formula sub activity", Entry(activity: "Coding > @cmd"), "cannot start with" };
            yield return new object[] { "blank project", Entry(project: " "), "Project must be" };
            yield return new object[] { "long company", Entry(company: new string('x', 101)), "Company must be" };
            yield return new object[] { "bad method name", Entry(method: "Robot"), "Method must be" };
            yield return new object[] { "numeric method", Entry(method: "1"), "Method must be" };
            yield return new object[] { "long note", Entry(note: new string('n', 2001)), "Note is longer" };
            yield return new object[] { "utc time", new Dictionary<string, object?>(Entry()) { ["start"] = Day.AddHours(9).ToString("s") + "Z" }, "without a time zone" };
            yield return new object[] { "missing start", new Dictionary<string, object?>(Entry()) { ["start"] = null }, "Start and End are required" };
        }

        [Theory]
        [MemberData(nameof(InvalidEntries))]
        public async Task Import_RejectsInvalidEntries_AndStoresNothing(string name, Dictionary<string, object?> bad, string expected)
        {
            var dev = await NewDeveloperAsync();

            var response = await PostJson(dev.Client, bad);
            var body = await BodyAsync(response);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, name);
            Assert.Contains(expected, body.GetProperty("errors")[0].GetProperty("message").GetString());
            Assert.Equal(1, body.GetProperty("errors")[0].GetProperty("row").GetInt32());
            Assert.Empty(await EntriesOfAsync(dev.Client));
        }

        [Fact]
        public async Task Import_IsAllOrNothing_AndReportsEveryBadRow()
        {
            var dev = await NewDeveloperAsync();

            var response = await PostJson(dev.Client,
                Entry(),
                Entry(activity: "Napping"),
                Entry(start: Day.AddHours(14), end: Day.AddHours(13)));
            var errors = (await BodyAsync(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("row").GetInt32()).ToArray();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(new[] { 2, 3 }, errors);
            Assert.Empty(await EntriesOfAsync(dev.Client));
        }

        [Fact]
        public async Task Import_RejectsOverlappingEntriesInTheSameUpload()
        {
            var dev = await NewDeveloperAsync();
            var company = "Acme " + Tag();

            var response = await PostJson(dev.Client,
                Entry(company, "A", "Coding", Day.AddHours(9), Day.AddHours(11)),
                Entry(company, "B", "Meeting", Day.AddHours(10), Day.AddHours(12)));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("overlaps the entry on row 1", (await BodyAsync(response)).GetProperty("errors")[0].GetProperty("message").GetString());
            Assert.Empty(await EntriesOfAsync(dev.Client));
        }

        [Fact]
        public async Task Import_RejectsAClosedProject()
        {
            var dev = await NewDeveloperAsync();
            var seeded = await _factory.SeedWorkAsync(dev.UserId, Day.AddDays(-30), TimePlanner.Core.Domain.Enums.ProjectStatus.Closed);
            var closed = await dev.Client.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{seeded.ProjectId}");

            var response = await PostJson(dev.Client, Entry(closed.GetProperty("companyName").GetString(), closed.GetProperty("name").GetString()!));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("is closed", (await BodyAsync(response)).GetProperty("errors")[0].GetProperty("message").GetString());
        }

        [Fact]
        public async Task Import_RejectsAnEmptyRequest_AndTooManyRows()
        {
            var dev = await NewDeveloperAsync();

            Assert.Equal(HttpStatusCode.BadRequest, (await dev.Client.PostAsJsonAsync(JsonUrl, new { entries = Array.Empty<object>() })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await dev.Client.PostAsJsonAsync(JsonUrl, new { })).StatusCode);

            var tooMany = Enumerable.Range(0, 5001).Select(_ => Entry()).ToArray();
            var response = await PostJson(dev.Client, tooMany);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("more than 5000", (await BodyAsync(response)).GetProperty("errors")[0].GetProperty("message").GetString());
        }

        // ---------- csv through the api ----------

        [Fact]
        public async Task Csv_ImportsRows_KeepingQuotedNotesAndHandlingABom()
        {
            var dev = await NewDeveloperAsync();
            var company = "Acme " + Tag();
            var csv = CsvHeader + CsvRow(company, note: "\"fixed bug, then tested\r\nsecond line\"");

            var response = await dev.Client.PostAsync(CsvUrl, CsvContent(csv, bom: true));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var entry = Assert.Single(await EntriesOfAsync(dev.Client));
            Assert.Equal("fixed bug, then tested\r\nsecond line", entry.GetProperty("note").GetString());
        }

        [Fact]
        public async Task Csv_HeadersAreCaseInsensitive_AndOptionalColumnsMayBeLeftOut()
        {
            var dev = await NewDeveloperAsync();
            var csv = "company,PROJECT,Activity,start,End\r\n" + $"Acme {Tag()},Web,Coding,{Csv(Day.AddHours(9))},{Csv(Day.AddHours(10))}\r\n";

            var response = await dev.Client.PostAsync(CsvUrl, CsvContent(csv));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Manual", Assert.Single(await EntriesOfAsync(dev.Client)).GetProperty("method").GetString());
        }

        [Theory]
        [InlineData("Company,Project,Activity,Start\r\nAcme,Web,Coding,2026-09-28 09:00\r\n", "Missing column(s): end")]
        [InlineData("Company,Project,Activity,Start,End,Colour\r\n", "Unknown column(s): colour")]
        public async Task Csv_RejectsAWrongHeader(string csv, string expected)
        {
            var dev = await NewDeveloperAsync();

            var response = await dev.Client.PostAsync(CsvUrl, CsvContent(csv));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(expected, (await BodyAsync(response)).GetProperty("errors")[0].GetProperty("message").GetString());
        }

        [Fact]
        public async Task Csv_ReportsTheSpreadsheetRowOfEachProblem()
        {
            var dev = await NewDeveloperAsync();
            var company = "Acme " + Tag();
            var csv = CsvHeader + CsvRow(company) + CsvRow(company, activity: "Napping", start: Day.AddHours(11)) + $"{company},Web,Coding,28/09/2026 09:00,28/09/2026 10:00,x,Manual\r\n";

            var response = await dev.Client.PostAsync(CsvUrl, CsvContent(csv));
            var errors = (await BodyAsync(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("row").GetInt32()).ToArray();

            Assert.Equal(new[] { 3, 4 }, errors);
            Assert.Empty(await EntriesOfAsync(dev.Client));
        }

        [Fact]
        public async Task Csv_RejectsAFormulaInAName()
        {
            var dev = await NewDeveloperAsync();

            var response = await dev.Client.PostAsync(CsvUrl, CsvContent(CsvHeader + CsvRow("=cmd|' /C calc'!A0")));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await EntriesOfAsync(dev.Client));
        }

        [Theory]
        [InlineData("not a csv at all \"unterminated\r\n\"", "timesheet.csv")]
        [InlineData("", "timesheet.csv")]
        [InlineData("Company,Project,Activity,Start,End\r\n", "timesheet.txt")]
        public async Task Csv_RejectsBadFiles(string content, string fileName)
        {
            var dev = await NewDeveloperAsync();

            var response = await dev.Client.PostAsync(CsvUrl, CsvContent(content, fileName));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Csv_RejectsAFileOverOneMegabyte()
        {
            var dev = await NewDeveloperAsync();
            var big = CsvHeader + new string('x', 1_050_000);

            var response = await dev.Client.PostAsync(CsvUrl, CsvContent(big));

            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
        }

        // ---------- the dashboard upload page ----------

        private static async Task<string> TokenFromAsync(HttpClient client, string url)
        {
            var html = await client.GetStringAsync(url);
            return Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        }

        private async Task<(HttpClient Browser, HttpClient Api, int UserId)> SignedInAsync()
        {
            var dev = await _factory.CreateLinkedUserAsync();
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, dev.Email, dev.Password);
            return (browser, await _factory.LoginClientAsync(dev.Email, dev.Password), dev.AppUserId);
        }

        [Fact]
        public async Task UploadPage_RequiresALogin()
        {
            var response = await _factory.NewClient().GetAsync("/CsvUpload");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        [Fact]
        public async Task UploadPage_ShowsTheFormAndAnExampleFile()
        {
            var user = await SignedInAsync();

            Assert.Contains("Upload timesheet", await user.Browser.GetStringAsync("/CsvUpload"));
            var template = await user.Browser.GetAsync("/CsvUpload/Template");
            Assert.Equal("text/csv", template.Content.Headers.ContentType!.MediaType);
            Assert.StartsWith("Company,Project,Activity,Start,End", await template.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task UploadPage_ImportsTheFileForTheSignedInUser()
        {
            var user = await SignedInAsync();
            var token = await TokenFromAsync(user.Browser, "/CsvUpload");

            var response = await user.Browser.PostAsync("/CsvUpload", CsvContent(CsvHeader + CsvRow("Acme " + Tag()), antiForgery: token));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            //the result block the page shows and the dialog lifts out of the answer, with the file it was about
            Assert.Contains("data-upload-result data-kind=\"ok\" data-imported=\"true\"", html);
            Assert.Contains("<span class=\"up-result__num\">1</span><span class=\"up-result__unit\">entry added to your timesheet</span>", html);
            Assert.Contains("timesheet.csv", html);
            Assert.Single(await EntriesOfAsync(user.Api));
        }

        [Fact]
        public async Task UploadPage_SaysWhenEverythingWasAlreadyStored()
        {
            var user = await SignedInAsync();
            var csv = CsvHeader + CsvRow("Acme " + Tag());
            await user.Browser.PostAsync("/CsvUpload", CsvContent(csv, antiForgery: await TokenFromAsync(user.Browser, "/CsvUpload")));

            var html = await (await user.Browser.PostAsync("/CsvUpload", CsvContent(csv, antiForgery: await TokenFromAsync(user.Browser, "/CsvUpload")))).Content.ReadAsStringAsync();

            Assert.Contains("data-upload-result data-kind=\"none\" data-imported=\"false\"", html);
            Assert.Contains("Nothing new", html);
            Assert.Contains("<span class=\"up-result__num\">1</span><span class=\"up-result__unit\">entry already stored</span>", html);
            Assert.Single(await EntriesOfAsync(user.Api));
        }

        [Fact]
        public async Task TheReportsPage_OpensTheUploadDialog_AndStillLinksToTheUploadPage()
        {
            var user = await SignedInAsync();

            var html = await user.Browser.GetStringAsync("/Report?view=week");

            Assert.Contains("aria-haspopup=\"dialog\" data-upload-open href=\"/CsvUpload\"", html);
            Assert.Contains("<dialog class=\"up-modal\" id=\"up-dialog\" aria-labelledby=\"up-title\" data-upload-dialog>", html);
            //the dialog posts to the same handler as the page, with the anti forgery token
            var dialog = Regex.Match(html, "<dialog.*?</dialog>", RegexOptions.Singleline).Value;
            Assert.Contains("<form class=\"up-modal__form\" method=\"post\" enctype=\"multipart/form-data\" novalidate data-upload-form action=\"/CsvUpload\">", dialog);
            Assert.Contains("name=\"__RequestVerificationToken\"", dialog);
            Assert.Contains("name=\"file\" accept=\".csv,text/csv\"", html);
            Assert.Contains("href=\"/CsvUpload/Template\"", html);
            Assert.Contains("/js/upload.js", html);
        }

        [Fact]
        public async Task UploadPage_EncodesTheFileName()
        {
            var user = await SignedInAsync();
            var token = await TokenFromAsync(user.Browser, "/CsvUpload");

            var html = await (await user.Browser.PostAsync("/CsvUpload", CsvContent(CsvHeader + CsvRow("Acme " + Tag()), fileName: "<b>hours</b>.csv", antiForgery: token))).Content.ReadAsStringAsync();

            Assert.DoesNotContain("<b>hours</b>", html);
            Assert.Contains("&lt;b&gt;hours&lt;/b&gt;.csv", html);
        }

        [Fact]
        public async Task UploadPage_RejectsAPostWithoutTheAntiForgeryToken()
        {
            var user = await SignedInAsync();

            var response = await user.Browser.PostAsync("/CsvUpload", CsvContent(CsvHeader + CsvRow("Acme")));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await EntriesOfAsync(user.Api));
        }

        [Fact]
        public async Task UploadPage_ShowsRowErrors_AndEncodesWhatTheFileContained()
        {
            var user = await SignedInAsync();
            var token = await TokenFromAsync(user.Browser, "/CsvUpload");
            var csv = CsvHeader + CsvRow("Acme", activity: "<script>alert(1)</script>");

            var html = await (await user.Browser.PostAsync("/CsvUpload", CsvContent(csv, antiForgery: token))).Content.ReadAsStringAsync();

            Assert.Contains("Nothing was imported", html);
            Assert.Contains("Row 2", html);
            Assert.DoesNotContain("<script>alert(1)</script>", html);
        }

        [Fact]
        public async Task UploadPage_TellsAnUnlinkedAccountWhy()
        {
            var (email, password) = await _factory.CreateUserAsync();
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, email, password);
            var token = await TokenFromAsync(browser, "/CsvUpload");

            var html = await (await browser.PostAsync("/CsvUpload", CsvContent(CsvHeader + CsvRow("Acme"), antiForgery: token))).Content.ReadAsStringAsync();

            Assert.Contains("not linked to a time tracking profile", html);
        }
    }
}
