using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Dashboard.Services.Overview;
using TimePlanner.Dashboard.Services.Reports;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //tests for excel timesheet exports past periods and layout rules
    [Collection("Api")]
    public class ExcelExportTests
    {
        private const int Coding = 2, Learning = 7;
        private const string InternalCompany = "Leading Edge (Internal)";
        private static readonly DateTime Day = DateTime.Today.AddDays(-10);

        private readonly ApiFactory _factory;

        public ExcelExportTests(ApiFactory factory) => _factory = factory;

        private static string Tag() => Guid.NewGuid().ToString("N")[..8];
        private static string D(DateTime d) => d.ToString("yyyy-MM-dd");

        private static string ExportUrl(DateTime from, DateTime to, int? userId = null, string? format = null) =>
            $"/Report/Export?from={D(from)}&to={D(to)}" + (userId == null ? "" : $"&userId={userId}") + (format == null ? "" : $"&format={format}");

        private async Task<(HttpClient Client, string Email, string Password, int UserId)> NewUserAsync(string role = "Developer")
        {
            var user = await _factory.CreateLinkedUserAsync(role);
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, user.Email, user.Password);
            return (browser, user.Email, user.Password, user.AppUserId);
        }

        //-----------------------------
        //default export returns excel workbook with correct sheet title headers and formatted values
        [Fact]
        public async Task Export_DefaultFormat_ReturnsXlsxWorkbook()
        {
            var dev = await NewUserAsync();
            var acme = "Acme " + Tag();
            await _factory.AddEntryAsync(dev.UserId, acme, "Web", Coding, Day.AddHours(9), 30, "Built login");

            var response = await dev.Client.GetAsync(ExportUrl(Day, Day));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);

            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var stream = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(stream);

            var sheet = Assert.Single(workbook.Worksheets);
            Assert.Equal(Day.ToString("MMMM"), sheet.Name);
            Assert.Contains("Time Log", sheet.Cell("B1").GetString());

            Assert.Equal("Date", sheet.Cell("B2").GetString());
            Assert.Equal("Activity/Task", sheet.Cell("C2").GetString());
            Assert.Equal("Client / Project", sheet.Cell("D2").GetString());
            Assert.Equal("Start Time", sheet.Cell("E2").GetString());
            Assert.Equal("End Time", sheet.Cell("F2").GetString());
            Assert.Equal("Duration (hours)", sheet.Cell("G2").GetString());
            Assert.Equal("Notes", sheet.Cell("H2").GetString());
            Assert.Equal("Billable", sheet.Cell("I2").GetString());

            Assert.Equal(Day.Date, sheet.Cell("B3").GetDateTime().Date);
            Assert.Equal("Built login", sheet.Cell("C3").GetString());
            static TimeSpan ReadTime(XLCellValue v) =>
                v.IsTimeSpan ? v.GetTimeSpan() : v.IsDateTime ? v.GetDateTime().TimeOfDay : TimeSpan.FromDays(v.GetNumber());

            Assert.Equal(new TimeSpan(9, 0, 0), ReadTime(sheet.Cell("E3").Value));
            Assert.Equal(new TimeSpan(9, 30, 0), ReadTime(sheet.Cell("F3").Value));
            Assert.Equal(new TimeSpan(0, 30, 0), ReadTime(sheet.Cell("G3").Value));
        }

        //-----------------------------
        //format csv returns identical bytes to csv export method
        [Fact]
        public async Task Export_FormatCsv_ReturnsOriginalCsvBytes()
        {
            var dev = await NewUserAsync();
            var acme = "Acme " + Tag();
            await _factory.AddEntryAsync(dev.UserId, acme, "Web", Coding, Day.AddHours(9), 60);

            var response = await dev.Client.GetAsync(ExportUrl(Day, Day, format: "csv"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);

            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var scope = _factory.Services.CreateScope();
            var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
            var expectedBytes = await reports.ExportTimesheetCsvAsync(dev.UserId, Day, Day);

            Assert.Equal(expectedBytes, bytes);
        }

        //-----------------------------
        //billable labels and background fills correspond to client company and activity
        [Fact]
        public async Task Export_BillableLabelsAndFills_MatchCompanyAndActivity()
        {
            var dev = await NewUserAsync();
            var client = "Client " + Tag();
            await _factory.AddEntryAsync(dev.UserId, InternalCompany, "Internal", Coding, Day.AddHours(8), 60);
            await _factory.AddEntryAsync(dev.UserId, client, "Portal", Coding, Day.AddHours(9), 60);
            await _factory.AddEntryAsync(dev.UserId, client, "Portal", Learning, Day.AddHours(10), 60);

            var response = await dev.Client.GetAsync(ExportUrl(Day, Day));
            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var stream = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.First();

            Assert.Equal("Internal", sheet.Cell("I3").GetString());
            Assert.Equal(XLFillPatternValues.None, sheet.Cell("I3").Style.Fill.PatternType);

            Assert.Equal("Yes", sheet.Cell("I4").GetString());
            Assert.Equal(XLFillPatternValues.Solid, sheet.Cell("I4").Style.Fill.PatternType);

            Assert.Equal("No", sheet.Cell("I5").GetString());

            Assert.Equal("Legend", sheet.Cell("K1").GetString());
            Assert.Equal(InternalCompany, sheet.Cell("K2").GetString());
            Assert.Equal(client, sheet.Cell("K3").GetString());
        }

        //-----------------------------
        //export across two calendar months creates two worksheet tabs
        [Fact]
        public async Task Export_RangeAcrossTwoMonths_CreatesTwoSheets()
        {
            var dev = await NewUserAsync();
            var month1 = new DateTime(2026, 8, 25);
            var month2 = new DateTime(2026, 9, 5);
            await _factory.AddEntryAsync(dev.UserId, "Acme " + Tag(), "Web", Coding, month1, 60);
            await _factory.AddEntryAsync(dev.UserId, "Acme " + Tag(), "Web", Coding, month2, 60);

            var response = await dev.Client.GetAsync(ExportUrl(month1, month2));
            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var stream = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(stream);

            Assert.Equal(2, workbook.Worksheets.Count);
            Assert.Equal("August", workbook.Worksheets.First().Name);
            Assert.Equal("September", workbook.Worksheets.Last().Name);
        }

        //-----------------------------
        //formula text like sum is stored as plain text string
        [Fact]
        public async Task Export_FormulaText_StoredAsStringNotFormula()
        {
            var dev = await NewUserAsync();
            var formulaNote = "=SUM(A1)";
            await _factory.AddEntryAsync(dev.UserId, "Acme " + Tag(), "Web", Coding, Day.AddHours(9), 60, formulaNote);

            var response = await dev.Client.GetAsync(ExportUrl(Day, Day));
            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var stream = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.First();

            var cell = sheet.Cell("C3");
            Assert.False(cell.HasFormula);
            Assert.Equal(formulaNote, cell.GetString());
        }

        //-----------------------------
        //developer requesting export for another user receives forbidden status
        [Fact]
        public async Task Export_DeveloperRequestsOtherUser_ReturnsForbidden()
        {
            var alice = await NewUserAsync();
            var bob = await NewUserAsync();

            var xlsxRes = await alice.Client.GetAsync(ExportUrl(Day, Day, userId: bob.UserId, format: "xlsx"));
            Assert.True(xlsxRes.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);

            var csvRes = await alice.Client.GetAsync(ExportUrl(Day, Day, userId: bob.UserId, format: "csv"));
            Assert.True(csvRes.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
        }

        //-----------------------------
        //admin zip export contains valid xlsx workbooks for all logging users
        [Fact]
        public async Task AdminExport_Everyone_ContainsValidXlsxWorkbooks()
        {
            var admin = await NewUserAsync("Admin");
            var dev = await NewUserAsync();
            await _factory.AddEntryAsync(dev.UserId, "Acme " + Tag(), "Web", Coding, Day.AddHours(9), 60);

            using var scope = _factory.Services.CreateScope();
            var adminService = scope.ServiceProvider.GetRequiredService<AdminService>();
            Period.TryResolve("week", Day, DateTime.Today, out var period);

            var file = await adminService.ExportAsync(period, null, "xlsx");
            Assert.NotNull(file);
            Assert.Equal("application/zip", file.ContentType);

            using var zipStream = new MemoryStream(file.Bytes);
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
            Assert.NotEmpty(zip.Entries);

            foreach (var entry in zip.Entries)
            {
                Assert.EndsWith(".xlsx", entry.Name);
                using var entryStream = entry.Open();
                using var memStream = new MemoryStream();
                await entryStream.CopyToAsync(memStream);
                using var workbook = new XLWorkbook(memStream);
                Assert.NotEmpty(workbook.Worksheets);
            }
        }

        //-----------------------------
        //past periods list populates 8 past weeks and each link returns ok
        [Fact]
        public async Task PastPeriods_ListsPastWeeksAndLinksReturnOk()
        {
            var dev = await NewUserAsync();
            await _factory.AddEntryAsync(dev.UserId, "Acme " + Tag(), "Web", Coding, Day.AddHours(9), 60);

            var response = await dev.Client.GetAsync("/Report?view=week");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("Past weekly downloads", html);

            var pastWeekUrl = ExportUrl(Day.AddDays(-14), Day.AddDays(-8));
            var pastExportRes = await dev.Client.GetAsync(pastWeekUrl);
            Assert.Equal(HttpStatusCode.OK, pastExportRes.StatusCode);
        }
    }
}
//------------------------------EOF-----------------------------\\
