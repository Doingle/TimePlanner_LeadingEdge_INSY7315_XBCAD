using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers billable status rules in reports and timesheet export
    [Collection("Api")]
    public class BillableRuleTests
    {
        private const int Coding = 2, Learning = 7;
        private const string InternalCompany = "Leading Edge (Internal)";
        private static readonly DateTime Day = DateTime.Today.AddDays(-5);

        private readonly ApiFactory _factory;

        public BillableRuleTests(ApiFactory factory) => _factory = factory;

        private static string Tag() => Guid.NewGuid().ToString("N")[..8];
        private static string D(DateTime d) => d.ToString("yyyy-MM-dd");

        private static string HoursUrl(DateTime from, DateTime to) => $"/api/v1/reports/hours?from={D(from)}&to={D(to)}";
        private static string CsvUrl(DateTime from, DateTime to) => $"/api/v1/reports/timesheet.csv?from={D(from)}&to={D(to)}";

        private async Task<(HttpClient Client, int UserId)> NewUserAsync()
        {
            var user = await _factory.CreateLinkedUserAsync("Developer");
            var client = await _factory.LoginClientAsync(user.Email, user.Password);
            return (client, user.AppUserId);
        }

        private static async Task<string[]> LinesAsync(HttpResponseMessage response) =>
            (await response.Content.ReadAsStringAsync()).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        //-----------------------------
        //coding on client work is billable learning is non billable
        [Fact]
        public async Task ClientCodingIsBillable_LearningIsNot()
        {
            var (client, userId) = await NewUserAsync();
            var acme = "Acme " + Tag();

            await _factory.AddEntryAsync(userId, acme, "Portal", Coding, Day.AddHours(9), 60);
            await _factory.AddEntryAsync(userId, acme, "Portal", Learning, Day.AddHours(10), 60);

            var response = await client.GetAsync(HoursUrl(Day, Day));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var report = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2.0, report.GetProperty("totalHours").GetDouble());
            Assert.Equal(1.0, report.GetProperty("billableHours").GetDouble());
        }

        //-----------------------------
        //internal company work gives zero billable hours
        [Fact]
        public async Task InternalWorkIsNotBillable()
        {
            var (client, userId) = await NewUserAsync();

            await _factory.AddEntryAsync(userId, InternalCompany, "Internal", Coding, Day.AddHours(9), 60);

            var response = await client.GetAsync(HoursUrl(Day, Day));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var report = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1.0, report.GetProperty("totalHours").GetDouble());
            Assert.Equal(0.0, report.GetProperty("billableHours").GetDouble());
        }

        //-----------------------------
        //timesheet csv marks learning as no coding as yes and internal as internal
        [Fact]
        public async Task TimesheetCsv_LabelsLearningNo()
        {
            var (client, userId) = await NewUserAsync();
            var acme = "Acme " + Tag();

            await _factory.AddEntryAsync(userId, acme, "Portal", Coding, Day.AddHours(9), 60, "Coding work");
            await _factory.AddEntryAsync(userId, acme, "Portal", Learning, Day.AddHours(10), 60, "Learning work");
            await _factory.AddEntryAsync(userId, InternalCompany, "Internal", Coding, Day.AddHours(11), 60, "Internal work");

            var response = await client.GetAsync(CsvUrl(Day, Day));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var lines = await LinesAsync(response);
            Assert.Equal(4, lines.Length);

            var codingLine = lines.First(l => l.Contains("Coding work"));
            var learningLine = lines.First(l => l.Contains("Learning work"));
            var internalLine = lines.First(l => l.Contains("Internal work"));

            Assert.EndsWith(",Yes", codingLine);
            Assert.EndsWith(",No", learningLine);
            Assert.EndsWith(",Internal", internalLine);
        }
    }
}
//------------------------------EOF-----------------------------\\
