using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the read-only timesheet (a week or month, day by day) and the record of which days were submitted.
    //the host's clock is fixed at Wednesday 16 September 2026, 14:30 in South Africa
    public class TimesheetViewTests : IClassFixture<ClockedFactory>
    {
        private const int Coding = 2, Email = 4;

        private readonly ClockedFactory _factory;

        public TimesheetViewTests(ClockedFactory factory) => _factory = factory;

        private async Task<(HttpClient Client, int ProfileId, string Email)> PersonAsync(string role = "Developer")
        {
            var user = await _factory.CreateLinkedUserAsync(role);
            return (await _factory.LoginClientAsync(user.Email, user.Password), user.AppUserId, user.Email);
        }

        private static async Task<JsonElement> ViewAsync(HttpClient client, string query = "") =>
            await client.GetFromJsonAsync<JsonElement>("/api/v1/timesheets" + query);

        private static string[] Statuses(JsonElement view) =>
            view.GetProperty("days").EnumerateArray().Select(d => d.GetProperty("status").GetString()!).ToArray();

        // ---------- the week ----------

        [Fact]
        public async Task TheWeek_ShowsEveryDayWithItsStatus()
        {
            var person = await PersonAsync();
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Monday);
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 120);

            var view = await ViewAsync(person.Client);

            //Monday sent, Tuesday a working day that passed with nothing sent, today pending, the rest of the week still to come
            Assert.Equal(new[] { "Submitted", "Missing", "Pending", "NotDue", "NotDue", "NotDue", "NotDue" }, Statuses(view));
            Assert.Equal("week", view.GetProperty("view").GetString());
            Assert.Equal("14 Sep to 20 Sep 2026", view.GetProperty("label").GetString());
            Assert.Equal("2026-09-14T00:00:00", view.GetProperty("from").GetString());
            Assert.Equal("2026-09-20T00:00:00", view.GetProperty("to").GetString());
            Assert.Equal(new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" }, view.GetProperty("days").EnumerateArray().Select(d => d.GetProperty("weekday").GetString()).ToArray());
            Assert.Equal(1, view.GetProperty("submittedDays").GetInt32());
            //pending (today) is not counted as missing
            Assert.Equal(1, view.GetProperty("missingDays").GetInt32());
        }

        [Fact]
        public async Task AWeekThatIsOver_ShowsWeekendsAsNonWorkingDays_UnlessSomethingWasSubmitted()
        {
            var person = await PersonAsync();
            await _factory.SubmitAsync(person.ProfileId, new DateTime(2026, 9, 7));
            await _factory.SubmitAsync(person.ProfileId, new DateTime(2026, 9, 12));   // a Saturday

            var view = await ViewAsync(person.Client, "?date=2026-09-09");

            Assert.Equal(new[] { "Submitted", "Missing", "Missing", "Missing", "Missing", "Submitted", "NonWorkingDay" }, Statuses(view));
            Assert.Equal(4, view.GetProperty("missingDays").GetInt32());
        }

        [Fact]
        public async Task AnyDayInsideTheWeek_GivesTheSameWeek()
        {
            var person = await PersonAsync();

            foreach (var date in new[] { "2026-09-14", "2026-09-17", "2026-09-20" })
                Assert.Equal("2026-09-14T00:00:00", (await ViewAsync(person.Client, $"?date={date}")).GetProperty("from").GetString());
            Assert.Equal("2026-09-21T00:00:00", (await ViewAsync(person.Client, "?date=2026-09-21")).GetProperty("from").GetString());
        }

        [Fact]
        public async Task EachDay_ListsWhatWasLogged_WithHoursAndWhenItWasSubmitted()
        {
            var person = await PersonAsync();
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 90, "login page");
            await _factory.AddEntryAsync(person.ProfileId, "Leading Edge (Internal)", "Internal", Email, ClockedFactory.Monday.AddHours(11), 30);
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Monday);

            var monday = (await ViewAsync(person.Client)).GetProperty("days")[0];
            var items = monday.GetProperty("items").EnumerateArray().ToArray();

            Assert.Equal(2.0, monday.GetProperty("hours").GetDouble());
            Assert.Equal(2, monday.GetProperty("entries").GetInt32());
            Assert.Equal("2026-09-14T17:05:00+02:00", monday.GetProperty("submittedAt").GetString());
            Assert.Equal("09:00", items[0].GetProperty("start").GetString());
            Assert.Equal("10:30", items[0].GetProperty("end").GetString());
            Assert.Equal(1.5, items[0].GetProperty("hours").GetDouble());
            Assert.Equal("Acme", items[0].GetProperty("company").GetString());
            Assert.Equal("Coding", items[0].GetProperty("activity").GetString());
            Assert.Equal("login page", items[0].GetProperty("note").GetString());
            Assert.True(items[0].GetProperty("billable").GetBoolean());
            Assert.False(items[1].GetProperty("billable").GetBoolean());
            //each entry carries its category's colour, the same one Home uses
            Assert.Equal("#7C3AED", items[0].GetProperty("colour").GetString());
            Assert.Equal("#EA580C", items[1].GetProperty("colour").GetString());
        }

        [Fact]
        public async Task TheTotals_AddUpTheWholePeriod()
        {
            var person = await PersonAsync();
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 90);
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Coding, ClockedFactory.Wednesday.AddHours(9), 30);
            await _factory.AddEntryAsync(person.ProfileId, "Acme", "Web", Coding, ClockedFactory.Sunday.AddDays(1).AddHours(9), 600);   // next week

            Assert.Equal(2.0, (await ViewAsync(person.Client)).GetProperty("totalHours").GetDouble());
        }

        // ---------- the month ----------

        [Fact]
        public async Task TheMonth_CoversEveryDayOfTheMonth()
        {
            var person = await PersonAsync();
            await _factory.SubmitAsync(person.ProfileId, new DateTime(2026, 9, 1));

            var view = await ViewAsync(person.Client, "?view=month&date=2026-09-16");

            Assert.Equal("month", view.GetProperty("view").GetString());
            Assert.Equal("September 2026", view.GetProperty("label").GetString());
            Assert.Equal(30, view.GetProperty("days").GetArrayLength());
            Assert.Equal("2026-09-01T00:00:00", view.GetProperty("from").GetString());
            Assert.Equal("2026-09-30T00:00:00", view.GetProperty("to").GetString());
            Assert.Equal(1, view.GetProperty("submittedDays").GetInt32());
            //working days from the 1st to the 15th that were not sent: 11 working days minus the one sent
            Assert.Equal(10, view.GetProperty("missingDays").GetInt32());
            Assert.Equal("Pending", Statuses(view)[15]);
            Assert.Equal("NotDue", Statuses(view)[16]);
        }

        // ---------- access and input ----------

        [Fact]
        public async Task TheTimesheet_IsOnlyForThePersonAsking_UnlessAnAdminChoosesSomeone()
        {
            var alice = await PersonAsync();
            var bob = await PersonAsync();
            var admin = await PersonAsync("Admin");
            await _factory.AddEntryAsync(bob.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60);

            Assert.Equal(HttpStatusCode.Forbidden, (await alice.Client.GetAsync($"/api/v1/timesheets?userId={bob.ProfileId}")).StatusCode);
            Assert.Equal(0.0, (await ViewAsync(alice.Client)).GetProperty("totalHours").GetDouble());
            Assert.Equal(1.0, (await ViewAsync(admin.Client, $"?userId={bob.ProfileId}")).GetProperty("totalHours").GetDouble());
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.NewClient().GetAsync("/api/v1/timesheets")).StatusCode);
        }

        [Theory]
        [InlineData("?view=year")]
        [InlineData("?view=")]
        [InlineData("?view=weeks")]
        public async Task AnInvalidView_IsRejected(string query)
        {
            var person = await PersonAsync();
            var response = await person.Client.GetAsync("/api/v1/timesheets" + query);

            //a blank view means the week, anything else is rejected
            Assert.Equal(query == "?view=" ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
        }

        // ---------- recording submissions ----------

        private static object Entry(DateTime start, int minutes, string company = "Acme") => new
        {
            company, project = "Web", activity = "Coding", start = start.ToString("s"), end = start.AddMinutes(minutes).ToString("s")
        };

        [Fact]
        public async Task AnAcceptedImport_MarksEachDayItCoversAsSubmitted()
        {
            var person = await PersonAsync();

            var response = await person.Client.PostAsJsonAsync("/api/v1/timesheets/import", new
            {
                entries = new[] { Entry(ClockedFactory.Monday.AddHours(9), 60), Entry(ClockedFactory.Monday.AddHours(11), 60), Entry(ClockedFactory.Tuesday.AddHours(9), 60) }
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var rows = await _factory.SubmissionsAsync(person.ProfileId);
            Assert.Equal(new[] { ClockedFactory.Monday, ClockedFactory.Tuesday }, rows.Select(r => r.Date).ToArray());
            Assert.All(rows, r => Assert.Equal(new DateTime(2026, 9, 16, 12, 30, 0), r.SubmittedAtUtc));
            Assert.Equal(new[] { "Submitted", "Submitted", "Pending" }, Statuses(await ViewAsync(person.Client)).Take(3).ToArray());
        }

        [Fact]
        public async Task AnEntryThatRunsPastMidnight_CountsForTheDayItStarted()
        {
            var person = await PersonAsync();

            await person.Client.PostAsJsonAsync("/api/v1/timesheets/import", new { entries = new[] { Entry(ClockedFactory.Monday.AddHours(23), 120) } });

            Assert.Equal(new[] { ClockedFactory.Monday }, (await _factory.SubmissionsAsync(person.ProfileId)).Select(r => r.Date).ToArray());
        }

        [Fact]
        public async Task SendingADayAgain_MovesItsTimeForward_WithoutAddingARow()
        {
            var person = await PersonAsync();
            var payload = new { entries = new[] { Entry(ClockedFactory.Monday.AddHours(9), 60) } };
            await person.Client.PostAsJsonAsync("/api/v1/timesheets/import", payload);
            var first = Assert.Single(await _factory.SubmissionsAsync(person.ProfileId)).SubmittedAtUtc;

            _factory.Clock.Set(new DateTimeOffset(2026, 9, 16, 15, 0, 0, TimeSpan.Zero));
            try
            {
                var again = await person.Client.PostAsJsonAsync("/api/v1/timesheets/import", payload);
                var after = Assert.Single(await _factory.SubmissionsAsync(person.ProfileId)).SubmittedAtUtc;

                Assert.Equal(0, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());
                Assert.Equal(new DateTime(2026, 9, 16, 15, 0, 0), after);
                Assert.True(after > first);
            }
            finally
            {
                _factory.Clock.Set(ClockedFactory.DefaultNow);
            }
        }

        [Fact]
        public async Task AnImportThatOnlyRepeatsStoredEntries_StillRecordsTheDay()
        {
            using var factory = new ClockedFactory();
            var user = await factory.CreateLinkedUserAsync();
            var client = await factory.LoginClientAsync(user.Email, user.Password);
            var payload = new { entries = new[] { Entry(ClockedFactory.Monday.AddHours(9), 60) } };
            await client.PostAsJsonAsync("/api/v1/timesheets/import", payload);
            //as if recording the day had failed after the entries were stored
            await factory.ClearSubmissionsAsync();

            var retry = await client.PostAsJsonAsync("/api/v1/timesheets/import", payload);

            Assert.Equal(1, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("skipped").GetInt32());
            Assert.Single(await factory.SubmissionsAsync(user.AppUserId));
        }

        [Fact]
        public async Task ARejectedImport_RecordsNothing()
        {
            var person = await PersonAsync();

            var response = await person.Client.PostAsJsonAsync("/api/v1/timesheets/import", new
            {
                entries = new[] { Entry(ClockedFactory.Monday.AddHours(9), 60), new { company = "Acme", project = "Web", activity = "Napping", start = ClockedFactory.Monday.AddHours(11).ToString("s"), end = ClockedFactory.Monday.AddHours(12).ToString("s") } }
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await _factory.SubmissionsAsync(person.ProfileId));
        }

        [Fact]
        public async Task AnUploadedCsv_AlsoMarksTheDaysSubmitted()
        {
            var person = await PersonAsync();
            var csv = "Company,Project,Activity,Start,End\r\n" + $"Acme,Web,Coding,{ClockedFactory.Thursday.AddHours(9):yyyy-MM-dd HH:mm},{ClockedFactory.Thursday.AddHours(10):yyyy-MM-dd HH:mm}\r\n";
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
            file.Headers.ContentType = new("text/csv");

            var response = await person.Client.PostAsync("/api/v1/timesheets/import/csv", new MultipartFormDataContent { { file, "file", "week.csv" } });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new[] { ClockedFactory.Thursday }, (await _factory.SubmissionsAsync(person.ProfileId)).Select(r => r.Date).ToArray());
        }

        [Fact]
        public async Task OnePersonsSubmission_NeverShowsOnAnothersTimesheet()
        {
            var alice = await PersonAsync();
            var bob = await PersonAsync();

            await alice.Client.PostAsJsonAsync("/api/v1/timesheets/import", new { entries = new[] { Entry(ClockedFactory.Monday.AddHours(9), 60) } });

            Assert.Equal("Missing", Statuses(await ViewAsync(bob.Client))[0]);
            Assert.Empty(await _factory.SubmissionsAsync(bob.ProfileId));
        }
    }
}
