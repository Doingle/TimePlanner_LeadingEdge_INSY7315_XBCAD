using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the admin team view: who may use it, the overview and submission rates, the submissions grid, and the single and zipped exports.
    //each test starts its own host because the numbers depend on exactly who is on the team. The clock is Wednesday 16 September 2026, 14:30 in South Africa
    public class AdministrationTests
    {
        private const int Coding = 2, Email = 4;

        private static async Task<HttpClient> AdminAsync(ClockedFactory f) => await f.LoginClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        private static async Task<(string Email, int ProfileId, string IdentityId)> DeveloperAsync(ClockedFactory f, HttpClient admin, string role = "Developer")
        {
            var user = await f.CreateLinkedUserAsync(role);
            var list = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/users"))!;
            var id = list.Single(u => u.GetProperty("email").GetString() == user.Email).GetProperty("id").GetString()!;
            return (user.Email, user.AppUserId, id);
        }

        private static async Task<JsonElement> GetAsync(HttpClient client, string url) => await client.GetFromJsonAsync<JsonElement>(url);

        private static string[] Names(JsonElement rows) => rows.EnumerateArray().Select(r => r.GetProperty("name").GetString()!).ToArray();

        // ---------- access ----------

        [Theory]
        [InlineData("/api/v1/administration/overview")]
        [InlineData("/api/v1/administration/submissions")]
        [InlineData("/api/v1/administration/exports/timesheets")]
        public async Task OnlyAdminsMayUseTheTeamView(string url)
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var dev = await f.CreateLinkedUserAsync();
            var developer = await f.LoginClientAsync(dev.Email, dev.Password);
            await f.AddEntryAsync(dev.AppUserId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60);

            Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await developer.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
        }

        [Theory]
        [InlineData("/api/v1/administration/overview?view=year")]
        [InlineData("/api/v1/administration/submissions?view=year")]
        [InlineData("/api/v1/administration/exports/timesheets?view=year")]
        public async Task AnInvalidViewIsRejected(string url)
        {
            using var f = new ClockedFactory();

            Assert.Equal(HttpStatusCode.BadRequest, (await (await AdminAsync(f)).GetAsync(url)).StatusCode);
        }

        // ---------- the overview ----------

        [Fact]
        public async Task TheOverview_CombinesTheWholeTeam()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var alice = await DeveloperAsync(f, admin);
            var bob = await DeveloperAsync(f, admin);
            await f.AddEntryAsync(alice.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 180);
            await f.AddEntryAsync(bob.ProfileId, "Leading Edge (Internal)", "Internal", Email, ClockedFactory.Tuesday.AddHours(9), 60);

            var o = await GetAsync(admin, "/api/v1/administration/overview");
            var summary = o.GetProperty("summary");

            Assert.Equal("week", o.GetProperty("view").GetString());
            Assert.Equal("14 Sep to 20 Sep 2026", o.GetProperty("label").GetString());
            Assert.Equal(4.0, summary.GetProperty("totalHours").GetDouble());
            Assert.Equal(3.0, summary.GetProperty("billableHours").GetDouble());
            Assert.Equal(new[] { alice.Email, bob.Email }, summary.GetProperty("byPerson").EnumerateArray().Select(r => r.GetProperty("label").GetString()).ToArray());
            Assert.Equal(new[] { "Coding", "Email" }, summary.GetProperty("byCategory").EnumerateArray().Select(r => r.GetProperty("label").GetString()).ToArray());
        }

        [Fact]
        public async Task TheMonthView_CoversTheMonth()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var alice = await DeveloperAsync(f, admin);
            await f.AddEntryAsync(alice.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddDays(-9).AddHours(9), 60);   // 5 Sep

            var o = await GetAsync(admin, "/api/v1/administration/overview?view=month");

            Assert.Equal("September 2026", o.GetProperty("label").GetString());
            Assert.Equal(1.0, o.GetProperty("summary").GetProperty("totalHours").GetDouble());
        }

        // ---------- submission rates ----------

        [Fact]
        public async Task TheTodayRate_IsPeopleWhoSubmittedOverPeopleExpected_IgnoringDeactivatedDevelopers()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var a = await DeveloperAsync(f, admin);
            await DeveloperAsync(f, admin);
            await DeveloperAsync(f, admin);
            var gone = await DeveloperAsync(f, admin);
            await admin.PostAsync($"/api/v1/users/{gone.IdentityId}/deactivate", null);
            await f.SubmitAsync(a.ProfileId, ClockedFactory.Wednesday);

            var today = (await GetAsync(admin, "/api/v1/administration/overview")).GetProperty("today");

            Assert.Equal(1, today.GetProperty("submitted").GetInt32());
            Assert.Equal(3, today.GetProperty("expected").GetInt32());
            Assert.Equal(33.3, today.GetProperty("percent").GetDouble());
        }

        [Fact]
        public async Task ThePeriodRate_CountsEveryWorkingDayUpToToday()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var a = await DeveloperAsync(f, admin);
            var b = await DeveloperAsync(f, admin);
            foreach (var day in new[] { ClockedFactory.Monday, ClockedFactory.Tuesday, ClockedFactory.Wednesday })
                await f.SubmitAsync(a.ProfileId, day);
            await f.SubmitAsync(b.ProfileId, ClockedFactory.Monday);

            var period = (await GetAsync(admin, "/api/v1/administration/overview")).GetProperty("period");

            //two people, Monday to Wednesday: 6 expected, 4 sent
            Assert.Equal(4, period.GetProperty("submitted").GetInt32());
            Assert.Equal(6, period.GetProperty("expected").GetInt32());
            Assert.Equal(66.7, period.GetProperty("percent").GetDouble());
        }

        [Fact]
        public async Task AnAdminOnlyCounts_WhenTheySubmittedSomething()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            await DeveloperAsync(f, admin);
            var loggingAdmin = await DeveloperAsync(f, admin, "Admin");
            await DeveloperAsync(f, admin, "Admin");
            await f.SubmitAsync(loggingAdmin.ProfileId, ClockedFactory.Wednesday);

            var today = (await GetAsync(admin, "/api/v1/administration/overview")).GetProperty("today");

            //the developer, plus the admin who submitted; the admin who did not is not expected to
            Assert.Equal(1, today.GetProperty("submitted").GetInt32());
            Assert.Equal(2, today.GetProperty("expected").GetInt32());
        }

        [Fact]
        public async Task ADeactivatedDeveloper_StillCountsForDaysTheySubmittedInThePeriod()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var gone = await DeveloperAsync(f, admin);
            await f.SubmitAsync(gone.ProfileId, ClockedFactory.Monday);
            await admin.PostAsync($"/api/v1/users/{gone.IdentityId}/deactivate", null);

            var grid = await GetAsync(admin, "/api/v1/administration/submissions");

            Assert.Contains(gone.Email, grid.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("email").GetString()));
        }

        [Fact]
        public async Task OnAWeekend_NobodyIsExpectedToday_ButThePeriodStillHasWorkingDays()
        {
            //Saturday 19 September, 10:00 in South Africa
            using var f = new ClockedFactory(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero));
            var admin = await AdminAsync(f);
            var a = await DeveloperAsync(f, admin);
            await f.SubmitAsync(a.ProfileId, ClockedFactory.Monday);

            var o = await GetAsync(admin, "/api/v1/administration/overview");

            Assert.Equal(0, o.GetProperty("today").GetProperty("expected").GetInt32());
            Assert.Equal(JsonValueKind.Null, o.GetProperty("today").GetProperty("percent").ValueKind);
            Assert.Equal(5, o.GetProperty("period").GetProperty("expected").GetInt32());
            Assert.Equal(1, o.GetProperty("period").GetProperty("submitted").GetInt32());
        }

        [Fact]
        public async Task WithNoTeam_TheRatesAreEmptyNotZeroPercent()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);

            var o = await GetAsync(admin, "/api/v1/administration/overview");

            Assert.Equal(0, o.GetProperty("today").GetProperty("expected").GetInt32());
            Assert.Equal(JsonValueKind.Null, o.GetProperty("today").GetProperty("percent").ValueKind);
            Assert.Equal(JsonValueKind.Null, o.GetProperty("period").GetProperty("percent").ValueKind);
        }

        // ---------- the submissions grid ----------

        [Fact]
        public async Task TheGrid_HasAColumnPerWorkingDay_AndACellPerPersonPerDay()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var a = await DeveloperAsync(f, admin);
            await f.SubmitAsync(a.ProfileId, ClockedFactory.Monday);

            var grid = await GetAsync(admin, "/api/v1/administration/submissions");
            var row = grid.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("email").GetString() == a.Email);
            var cells = row.GetProperty("cells").EnumerateArray().ToArray();

            Assert.Equal(5, grid.GetProperty("days").GetArrayLength());
            Assert.Equal("2026-09-14T00:00:00", grid.GetProperty("days")[0].GetString());
            Assert.Equal("2026-09-18T00:00:00", grid.GetProperty("days")[4].GetString());
            //Monday sent, Tuesday missing, today (Wednesday) pending, Thursday and Friday not due yet
            Assert.Equal(new[] { "Submitted", "Missing", "Pending", "NotDue", "NotDue" }, cells.Select(c => c.GetProperty("status").GetString()).ToArray());
            Assert.Equal("2026-09-14T17:05:00+02:00", cells[0].GetProperty("submittedAt").GetString());
            Assert.Equal(JsonValueKind.Null, cells[1].GetProperty("submittedAt").ValueKind);
            Assert.Equal(1, row.GetProperty("submitted").GetInt32());
            Assert.Equal(1, row.GetProperty("missing").GetInt32());
            Assert.Equal(a.ProfileId, row.GetProperty("appUserId").GetInt32());
        }

        [Fact]
        public async Task TheMonthGrid_HasEveryWorkingDayOfTheMonth()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            await DeveloperAsync(f, admin);

            var grid = await GetAsync(admin, "/api/v1/administration/submissions?view=month");

            //September 2026 has 22 weekdays
            Assert.Equal(22, grid.GetProperty("days").GetArrayLength());
            Assert.Equal("September 2026", grid.GetProperty("label").GetString());
        }

        [Fact]
        public async Task TheGrid_ListsPeopleByName_AndLeavesOutDeactivatedPeopleWhoNeverSubmitted()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var a = await DeveloperAsync(f, admin);
            var b = await DeveloperAsync(f, admin);
            var gone = await DeveloperAsync(f, admin);
            await admin.PostAsync($"/api/v1/users/{gone.IdentityId}/deactivate", null);

            var rows = (await GetAsync(admin, "/api/v1/administration/submissions")).GetProperty("rows");

            Assert.Equal(new[] { a.Email, b.Email }.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), Names(rows));
        }

        // ---------- exports ----------

        private static async Task<(string Name, string Text)[]> UnzipAsync(HttpResponseMessage response)
        {
            using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
            return zip.Entries.Select(e =>
            {
                using var reader = new StreamReader(e.Open(), Encoding.UTF8);
                return (e.FullName, reader.ReadToEnd().TrimStart('﻿'));
            }).ToArray();
        }

        [Fact]
        public async Task ExportingOnePerson_GivesTheirCsv_NamedAfterThem()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var a = await DeveloperAsync(f, admin);
            var b = await DeveloperAsync(f, admin);
            await f.AddEntryAsync(a.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60, "alice only");
            await f.AddEntryAsync(b.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60, "bob only");

            var response = await admin.GetAsync($"/api/v1/administration/exports/timesheets?userId={a.ProfileId}");
            var text = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
            //the name part is the person's name made safe and cut to 40 characters
            Assert.Matches(@"^timesheet_developer-[0-9a-f]+_20260914_20260920\.csv$", response.Content.Headers.ContentDisposition!.FileName);
            Assert.Contains("alice only", text);
            Assert.DoesNotContain("bob only", text);
        }

        [Fact]
        public async Task ExportingEveryone_GivesAZipWithOneCsvPerPersonWhoLoggedTime()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var a = await DeveloperAsync(f, admin);
            var b = await DeveloperAsync(f, admin);
            var idle = await DeveloperAsync(f, admin);
            await f.AddEntryAsync(a.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60, "alice only");
            await f.AddEntryAsync(b.ProfileId, "Acme", "Web", Coding, ClockedFactory.Tuesday.AddHours(9), 60, "bob only");

            var response = await admin.GetAsync("/api/v1/administration/exports/timesheets");
            var files = await UnzipAsync(response);

            Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal("timesheets_20260914_20260920.zip", response.Content.Headers.ContentDisposition!.FileName);
            Assert.Equal(2, files.Length);
            Assert.Contains(files, x => x.Name.EndsWith($"-{a.ProfileId}_20260914_20260920.csv") && x.Text.Contains("alice only") && !x.Text.Contains("bob only"));
            Assert.Contains(files, x => x.Name.EndsWith($"-{b.ProfileId}_20260914_20260920.csv") && x.Text.Contains("bob only") && !x.Text.Contains("alice only"));
            Assert.DoesNotContain(files, x => x.Name.Contains($"-{idle.ProfileId}_"));
            Assert.All(files, x => Assert.StartsWith("Date,Activity/Task,Client / Project", x.Text));
        }

        [Fact]
        public async Task TwoPeopleWithTheSameName_NeverOverwriteEachOtherInTheZip()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var ids = new List<int>();
            foreach (var n in new[] { "one", "two" })
            {
                var created = await (await admin.PostAsJsonAsync("/api/v1/users", new { name = "Sam Smith", email = $"sam-{n}-{Guid.NewGuid():N}@test.local", role = "Developer" })).Content.ReadFromJsonAsync<JsonElement>();
                var id = created.GetProperty("user").GetProperty("appUserId").GetInt32();
                ids.Add(id);
                await f.AddEntryAsync(id, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60, $"note {n}");
            }

            var files = await UnzipAsync(await admin.GetAsync("/api/v1/administration/exports/timesheets"));

            Assert.Equal(2, files.Length);
            Assert.Equal(2, files.Select(x => x.Name).Distinct().Count());
            Assert.All(files, x => Assert.StartsWith("sam-smith-", x.Name));
        }

        [Fact]
        public async Task FileNames_AreMadeSafe_WhateverANameContains()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var created = await (await admin.PostAsJsonAsync("/api/v1/users", new { name = "../../Evil <b>Name</b> & Co\"", email = $"evil-{Guid.NewGuid():N}@test.local", role = "Developer" })).Content.ReadFromJsonAsync<JsonElement>();
            var id = created.GetProperty("user").GetProperty("appUserId").GetInt32();
            await f.AddEntryAsync(id, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60);

            var zipName = (await UnzipAsync(await admin.GetAsync("/api/v1/administration/exports/timesheets"))).Single().Name;
            var single = (await admin.GetAsync($"/api/v1/administration/exports/timesheets?userId={id}")).Content.Headers.ContentDisposition!.FileName!;

            Assert.Matches("^[a-z0-9_-]+\\.csv$", zipName);
            Assert.Matches("^[a-z0-9_-]+\\.csv$", single);
            Assert.DoesNotContain("..", zipName + single);
        }

        [Fact]
        public async Task NothingToExport_Is404_ForAnUnknownPersonAnAnEmptyPeriodAndAnEmptyTeam()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var idle = await DeveloperAsync(f, admin);
            await f.AddEntryAsync(idle.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60);

            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/administration/exports/timesheets?userId=999999")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/administration/exports/timesheets?date=2025-01-06")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/administration/exports/timesheets")).StatusCode);
        }

        [Fact]
        public async Task TheMonthExport_CoversTheWholeMonth()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var a = await DeveloperAsync(f, admin);
            await f.AddEntryAsync(a.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddDays(-9).AddHours(9), 60, "early september");

            var response = await admin.GetAsync($"/api/v1/administration/exports/timesheets?view=month&userId={a.ProfileId}");

            Assert.Contains("early september", await response.Content.ReadAsStringAsync());
            Assert.EndsWith("_20260901_20260930.csv", response.Content.Headers.ContentDisposition!.FileName);
        }

        [Fact]
        public async Task EveryExportedPerson_IsAudited()
        {
            using var f = new ClockedFactory();
            var admin = await AdminAsync(f);
            var a = await DeveloperAsync(f, admin);
            var b = await DeveloperAsync(f, admin);
            await f.AddEntryAsync(a.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60);
            await f.AddEntryAsync(b.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60);

            await admin.GetAsync("/api/v1/administration/exports/timesheets");

            var exports = (await f.AuditEventsAsync()).Where(e => e.Action == AuditActions.TimesheetExported).ToList();
            Assert.Equal(2, exports.Count);
            Assert.All(exports, e => Assert.Equal(ApiFactory.AdminEmail, e.Email));
        }
    }
}
