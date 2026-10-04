using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the administrators' pages: Team overview, Submissions and Exports. Each test starts its own host because the numbers depend on who is on the team.
    //the clock is Wednesday 16 September 2026, 14:30 in South Africa
    public class AdminPageTests
    {
        private const int Coding = 2, Email = 4;

        //the page as a person reads it: tags removed, HTML entities decoded and runs of white space collapsed
        private static string Text(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ");

        private static async Task<HttpClient> AdminBrowserAsync(ClockedFactory f)
        {
            var browser = f.NewClient();
            await ApiFactory.PostLoginAsync(browser, ApiFactory.AdminEmail, ApiFactory.AdminPassword);
            return browser;
        }

        private static async Task<(string Email, int ProfileId)> DeveloperAsync(ClockedFactory f)
        {
            var user = await f.CreateLinkedUserAsync();
            return (user.Email, user.AppUserId);
        }

        // ---------- access ----------

        [Theory]
        [InlineData("/Admin")]
        [InlineData("/Admin/Submissions")]
        [InlineData("/Admin/Exports")]
        [InlineData("/Admin/Export")]
        public async Task TheAdminPages_AreForAdminsOnly(string url)
        {
            using var f = new ClockedFactory();
            var user = await f.CreateLinkedUserAsync();
            var developer = f.NewClient();
            await ApiFactory.PostLoginAsync(developer, user.Email, user.Password);
            var admin = await AdminBrowserAsync(f);

            var anonymous = await f.NewClient().GetAsync(url);
            var denied = await developer.GetAsync(url);

            Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
            Assert.Contains("/Account/Login", anonymous.Headers.Location!.OriginalString);
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            Assert.Contains("AccessDenied", denied.Headers.Location!.OriginalString);
            //an admin is never sent to sign in or to the access denied page (the export with nothing to export goes back to the exports page instead)
            var allowed = (await admin.GetAsync(url)).Headers.Location?.OriginalString ?? "";
            Assert.DoesNotContain("AccessDenied", allowed);
            Assert.DoesNotContain("/Account/Login", allowed);
        }

        // ---------- the overview ----------

        [Fact]
        public async Task TheOverview_ShowsTeamHours_SubmissionRates_AndTheBreakdowns()
        {
            using var f = new ClockedFactory();
            var a = await DeveloperAsync(f);
            var b = await DeveloperAsync(f);
            await f.AddEntryAsync(a.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 180);
            await f.AddEntryAsync(b.ProfileId, "Leading Edge (Internal)", "Internal", Email, ClockedFactory.Tuesday.AddHours(9), 60);
            await f.SubmitAsync(a.ProfileId, ClockedFactory.Wednesday);
            await f.SubmitAsync(a.ProfileId, ClockedFactory.Monday);
            var admin = await AdminBrowserAsync(f);

            var raw = await admin.GetStringAsync("/Admin");
            var html = Text(raw);

            Assert.Contains("<span class=\"tm-hero__num\">4:00</span> <span class=\"tm-hero__unit\">logged by the team this week</span>", raw);
            Assert.Contains("From 2 entries.", html);
            Assert.Contains("Submitted today 1 of 2 50%", html);
            //two people, Monday to Wednesday: 6 expected, a sent Monday and Wednesday
            Assert.Contains("Submitted this week 2 of 6 33%", html);
            //the billable split, with a part of the bar each
            Assert.Contains("Billable 3:00 75%", html);
            Assert.Contains("Non-billable 1:00 25%", html);
            Assert.Contains("<span class=\"tm-seg\" data-percent=\"75\"></span>", raw);
            Assert.Contains("<span class=\"tm-seg tm-seg--nb tm-seg--apart\" data-percent=\"25\"></span>", raw);
            Assert.Contains("Where the team's time went", html);
            Assert.Contains("By category", html);
            Assert.Contains("By project", html);
            Assert.Contains("By person", html);
            Assert.Contains("Acme / Web", html);
            Assert.Contains(a.Email, html);
            Assert.Contains("14 Sep to 20 Sep 2026", html);
        }

        [Fact]
        public async Task TheOverview_OnAWeekendWithNothingLogged_SaysSo()
        {
            //Saturday 19 September
            using var f = new ClockedFactory(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero));
            await DeveloperAsync(f);
            var admin = await AdminBrowserAsync(f);

            var raw = await admin.GetStringAsync("/Admin");
            var html = Text(raw);

            Assert.Contains("<span class=\"tm-hero__num\">0:00</span>", raw);
            Assert.Contains("Nothing logged in this period.", html);
            Assert.Contains("Submitted today — Weekend", html);
            Assert.DoesNotContain("tm-billing", raw);
            Assert.Contains("Nothing logged.", html);
        }

        [Fact]
        public async Task TheOverview_LinksToTheOtherAdminPages_AndMovesBetweenPeriods()
        {
            using var f = new ClockedFactory();
            var admin = await AdminBrowserAsync(f);

            var html = await admin.GetStringAsync("/Admin");
            var lastWeek = await admin.GetStringAsync("/Admin?view=week&date=2026-09-07");

            //the Team pages' own tabs, keeping the week
            Assert.Contains("aria-current=\"page\" href=\"/Admin?view=week&amp;date=2026-09-14\">Overview", html);
            Assert.Contains("href=\"/Admin/Submissions?view=week&amp;date=2026-09-14\"", html);
            Assert.Contains("href=\"/Admin/Exports?view=week&amp;date=2026-09-14\"", html);
            Assert.Contains("href=\"/Users\"", html);
            //the previous week opens, the next one does not exist yet. An earlier week can step forward
            Assert.Contains("href=\"/Admin?view=week&amp;date=2026-09-07\"", html);
            Assert.DoesNotContain("date=2026-09-21", html);
            Assert.Contains("aria-disabled=\"true\"", html);
            Assert.Contains("href=\"/Admin?view=week&amp;date=2026-09-14\"", lastWeek);
            Assert.Contains("logged by the team that week", lastWeek);
            //week or month as links, the current one marked
            Assert.Contains("aria-current=\"true\" href=\"/Admin?view=week\">Week", html);
            Assert.Contains("href=\"/Admin?view=month\">Month", html);
        }

        [Fact]
        public async Task TheMonthView_AndAnUnknownView_AreHandled()
        {
            using var f = new ClockedFactory();
            var admin = await AdminBrowserAsync(f);

            var month = await admin.GetStringAsync("/Admin?view=month");
            var bad = await admin.GetStringAsync("/Admin?view=year");

            Assert.Contains("September 2026", month);
            Assert.Contains("Submitted this month", Text(month));
            Assert.Contains("view must be week or month", bad);
            Assert.Contains("14 Sep to 20 Sep 2026", bad);
        }

        // ---------- the submissions grid ----------

        [Fact]
        public async Task TheSubmissionsPage_MarksEachPersonAndDay()
        {
            using var f = new ClockedFactory();
            var a = await DeveloperAsync(f);
            await f.SubmitAsync(a.ProfileId, ClockedFactory.Monday);
            var admin = await AdminBrowserAsync(f);

            var raw = await admin.GetStringAsync("/Admin/Submissions");
            var html = Text(raw);

            foreach (var day in new[] { "Mon 14 September", "Tue 15 September", "Wed 16 September Today", "Thu 17 September", "Fri 18 September" })
                Assert.Contains(day, html);
            //working days only
            Assert.DoesNotContain("Sat 19", html);
            Assert.DoesNotContain("Sun 20", html);
            Assert.Contains(a.Email, html);
            //each day's status as a class and a tooltip, the words for screen readers, and the time a day was sent
            Assert.Contains("tm-grid__cell--submitted", raw);
            Assert.Contains("tm-grid__cell--missing", raw);
            Assert.Contains("tm-grid__cell--pending tm-grid__cell--today", raw);
            Assert.Contains("tm-grid__cell--notdue", raw);
            Assert.Contains("title=\"Submitted at 17:05\"", raw);
            Assert.Contains("title=\"Missing\"", raw);
            Assert.Contains("title=\"Not submitted yet\"", raw);
            Assert.Contains("Submitted at 17:05", html);
            Assert.Contains("Not submitted yet", html);
            Assert.Equal(5, Regex.Matches(raw, "<td class=\"tm-grid__cell ").Count);
            //the week's sent and missing days, in the table and in the phone's block
            Assert.Contains("<td class=\"tm-grid__num tm-grid__num--first\">1</td>", raw);
            Assert.Contains("<td class=\"tm-grid__num tm-grid__num--missing\">1</td>", raw);
            Assert.Contains("Sent 1 Missing 1", html);
            Assert.Contains("Mon 14 September: Submitted at 17:05", html);
            Assert.Contains("Submitted, with the time it was sent", html);
        }

        [Fact]
        public async Task TheSubmissionsMonth_ShowsTheMarksOnly_AndAPhoneListsTheMissingDates()
        {
            using var f = new ClockedFactory();
            var a = await DeveloperAsync(f);
            await f.SubmitAsync(a.ProfileId, ClockedFactory.Monday);
            var admin = await AdminBrowserAsync(f);

            var raw = await admin.GetStringAsync("/Admin/Submissions?view=month");
            var html = Text(raw);

            Assert.Contains("tm-grid tm-grid--month", raw);
            //the 22 working days of September, each read out in full
            Assert.Equal(22, Regex.Matches(raw, "<td class=\"tm-grid__cell ").Count);
            Assert.Contains("Tue 1 September", html);
            Assert.Contains("Wed 16 September, today", html);
            Assert.Contains("title=\"Wed 16 Sep, today\"", raw);
            Assert.Contains("Submitted (hover for the time)", html);
            //Monday the 14th was sent and today is not over, every working day before that is missing
            Assert.Contains("<td class=\"tm-grid__num tm-grid__num--missing\">10</td>", raw);
            Assert.Contains("Missing 1, 2, 3, 4, 7, 8, 9, 10, 11 and 15 Sep", html);
            //the dates are listed, so the phone's block leaves out the count
            Assert.DoesNotContain("tm-pcard__miss\"", raw);
        }

        [Fact]
        public async Task TheSubmissionsPage_KeepsTheTeamTabs_AndMovesBetweenPeriods()
        {
            using var f = new ClockedFactory();
            var admin = await AdminBrowserAsync(f);

            var html = await admin.GetStringAsync("/Admin/Submissions");

            Assert.Contains("aria-current=\"page\" href=\"/Admin/Submissions?view=week&amp;date=2026-09-14\">Submissions", html);
            Assert.Contains("href=\"/Admin?view=week&amp;date=2026-09-14\">Overview", html);
            //the previous week opens, the next one does not exist yet
            Assert.Contains("href=\"/Admin/Submissions?view=week&amp;date=2026-09-07\"", html);
            Assert.DoesNotContain("date=2026-09-21", html);
            Assert.Contains("aria-current=\"true\" href=\"/Admin/Submissions?view=week\">Week", html);
            Assert.Contains("href=\"/Admin/Submissions?view=month\">Month", html);
        }

        [Fact]
        public async Task TheSubmissionsPage_HasNoWorkflow_JustARecord()
        {
            using var f = new ClockedFactory();
            var a = await DeveloperAsync(f);
            await f.AddEntryAsync(a.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60);
            var admin = await AdminBrowserAsync(f);

            foreach (var url in new[] { "/Admin", "/Admin/Submissions", "/Admin/Exports" })
            {
                var html = await admin.GetStringAsync(url);
                foreach (var word in new[] { "Approve", "Send back", "Reject", "Remind" })
                    Assert.DoesNotContain(word, html);
            }
        }

        [Fact]
        public async Task TheSubmissionsPage_SaysWhenThereIsNobody()
        {
            using var f = new ClockedFactory();
            var admin = await AdminBrowserAsync(f);

            var html = await admin.GetStringAsync("/Admin/Submissions");

            Assert.Contains("nobody to show yet", html);
            Assert.Contains("Create accounts on the <a href=\"/Users\">Users</a> page", html);
            Assert.DoesNotContain("tm-key", html);
        }

        // ---------- the detailed report, under the submissions grid ----------

        [Fact]
        public async Task TheDetailedReport_IsUnderTheGrid_AndStartsOnTheGridsWeekForEveryone()
        {
            using var f = new ClockedFactory();
            var a = await DeveloperAsync(f);
            await f.AddEntryAsync(a.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 90);
            var admin = await AdminBrowserAsync(f);

            var html = await admin.GetStringAsync("/Admin/Submissions");

            Assert.True(html.IndexOf("tm-grid", StringComparison.Ordinal) < html.IndexOf("id=\"detailed-report\"", StringComparison.Ordinal));
            Assert.Contains("id=\"from\" name=\"from\" value=\"2026-09-14\"", html);
            Assert.Contains("id=\"to\" name=\"to\" value=\"2026-09-20\"", html);
            Assert.Contains("<option value=\"\">Everyone</option>", html);
            Assert.Contains(a.Email, html);
            Assert.Contains("<strong>Everyone</strong>, 14 Sep to 20 Sep 2026, by project", html);
            //the totals in the table's last row: hours, billable, entries
            Assert.Contains("Total 1:30 1:30 1", Text(html));
            Assert.Contains("<th scope=\"row\">Acme / Web</th>", html);
            //a timesheet is one person's, so the download waits for a person to be picked
            Assert.Contains("Pick a person to download their timesheet.", html);
            Assert.DoesNotContain("/Report/Export", html);
        }

        [Fact]
        public async Task TheDetailedReport_GroupsAnyRangeForOnePerson_AndTheirTimesheetDownloads()
        {
            using var f = new ClockedFactory();
            var a = await DeveloperAsync(f);
            var b = await DeveloperAsync(f);
            await f.AddEntryAsync(a.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60, "devs work");
            await f.AddEntryAsync(a.ProfileId, "Acme", "Web", Email, ClockedFactory.Tuesday.AddHours(9), 30);
            await f.AddEntryAsync(b.ProfileId, "BobsClient", "Secret", Coding, ClockedFactory.Monday.AddHours(9), 600);
            var admin = await AdminBrowserAsync(f);

            var html = await admin.GetStringAsync($"/Admin/Submissions?view=week&date=2026-09-14&from=2026-09-14&to=2026-09-15&groupBy=Day&userId={a.ProfileId}");
            var export = await admin.GetAsync($"/Report/Export?from=2026-09-14&to=2026-09-15&userId={a.ProfileId}&format=csv");

            Assert.Contains("<option value=\"Day\" selected=\"selected\">Day</option>", html);
            Assert.Contains($"<option value=\"{a.ProfileId}\" selected=\"selected\">", html);
            Assert.Contains("<th scope=\"col\">Day</th>", html);
            Assert.Equal(2, Regex.Matches(html, "<th scope=\"row\">").Count);
            Assert.Contains($"<strong>{a.Email}</strong>, 14 Sep to 15 Sep 2026, by day", html);
            Assert.Contains("Total 1:30 1:30 2", Text(html));
            Assert.DoesNotContain("BobsClient", Regex.Match(html, "id=\"detailed-report\".*", RegexOptions.Singleline).Value);
            Assert.Contains($"href=\"/Report/Export?from=2026-09-14&amp;to=2026-09-15&amp;userId={a.ProfileId}\"", html);
            Assert.Contains("devs work", await export.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task TheDetailedReport_ExplainsAnInvalidRange_AndTheGridStillShows()
        {
            using var f = new ClockedFactory();
            var a = await DeveloperAsync(f);
            var admin = await AdminBrowserAsync(f);

            var html = await admin.GetStringAsync("/Admin/Submissions?from=2026-09-15&to=2026-09-10");

            Assert.Contains("must not be after", html);
            Assert.Contains(a.Email, html);
            Assert.Contains("tm-grid__cell--missing", html);
        }

        // ---------- exports ----------

        [Fact]
        public async Task TheExportsPage_OffersEveryoneAndEachPerson()
        {
            using var f = new ClockedFactory();
            var a = await DeveloperAsync(f);
            var admin = await AdminBrowserAsync(f);

            var html = await admin.GetStringAsync("/Admin/Exports");

            Assert.Contains("<option value=\"\">Everyone</option>", html);
            Assert.Contains($"<option value=\"{a.ProfileId}\">{a.Email}</option>", html);
            Assert.Contains("name=\"view\" value=\"week\"", html);
            Assert.Contains("name=\"date\" value=\"2026-09-14\"", html);
            Assert.Contains("action=\"/Admin/Export\"", html);
            //what the button gives: a zip for everyone to begin with, one file once a person is picked
            Assert.Contains("A .zip file with one Excel or CSV file per person who logged time 14 Sep to 20 Sep 2026", Text(html));
            Assert.Contains("One file", html);
            Assert.Contains("Download .zip", html);
            Assert.Contains("/js/exports.js", html);
        }

        [Fact]
        public async Task TheExportsPage_KeepsTheTeamTabs_AndExplainsABadLink()
        {
            using var f = new ClockedFactory();
            var admin = await AdminBrowserAsync(f);

            var html = await admin.GetStringAsync("/Admin/Exports");
            var bad = await admin.GetStringAsync("/Admin/Exports?view=year");

            Assert.Contains("aria-current=\"page\" href=\"/Admin/Exports?view=week&amp;date=2026-09-14\">Exports", html);
            Assert.Contains("href=\"/Admin/Exports?view=week&amp;date=2026-09-07\"", html);
            Assert.DoesNotContain("date=2026-09-21", html);
            Assert.Contains("aria-current=\"true\" href=\"/Admin/Exports?view=week\">Week", html);
            //a bad link shows on the header and falls back to the current week, the form has no notice
            Assert.Contains("<div class=\"tm-banner\" role=\"alert\">", bad);
            Assert.Contains("view must be week or month", bad);
            Assert.Contains("14 Sep to 20 Sep 2026", bad);
            Assert.DoesNotContain("tm-notice", bad);
        }

        [Fact]
        public async Task TheDownloadForm_ReturnsOneCsv_OrAZipForEveryone()
        {
            using var f = new ClockedFactory();
            var a = await DeveloperAsync(f);
            var b = await DeveloperAsync(f);
            await f.AddEntryAsync(a.ProfileId, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60, "alice note");
            await f.AddEntryAsync(b.ProfileId, "Acme", "Web", Coding, ClockedFactory.Tuesday.AddHours(9), 60, "bob note");
            var admin = await AdminBrowserAsync(f);

            //what the form sends when "Everyone" is chosen: a blank userId
            var zip = await admin.GetAsync("/Admin/Export?view=week&date=2026-09-14&userId=");
            var csv = await admin.GetAsync($"/Admin/Export?view=week&date=2026-09-14&userId={a.ProfileId}&format=csv");

            Assert.Equal("application/zip", zip.Content.Headers.ContentType!.MediaType);
            using (var archive = new ZipArchive(new MemoryStream(await zip.Content.ReadAsByteArrayAsync())))
                Assert.Equal(2, archive.Entries.Count);
            Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
            var text = await csv.Content.ReadAsStringAsync();
            Assert.Contains("alice note", text);
            Assert.DoesNotContain("bob note", text);
        }

        [Fact]
        public async Task NothingToExport_SendsTheAdminBackWithAMessage_AndABadViewIsRefused()
        {
            using var f = new ClockedFactory();
            await DeveloperAsync(f);
            var admin = await AdminBrowserAsync(f);

            var empty = await admin.GetAsync("/Admin/Export?view=week&date=2026-09-14");
            var page = await admin.GetStringAsync(empty.Headers.Location!.OriginalString);
            var bad = await admin.GetAsync("/Admin/Export?view=year");

            Assert.Equal(HttpStatusCode.Redirect, empty.StatusCode);
            Assert.Contains("/Admin/Exports", empty.Headers.Location!.OriginalString);
            Assert.Contains("nothing to export", Text(page));
            //the message sits with the form, not on the header
            Assert.Contains("<p class=\"tm-notice\" role=\"alert\" data-export-notice>", page);
            Assert.DoesNotContain("tm-banner", page);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        // ---------- safe output ----------

        [Fact]
        public async Task AdminPages_EncodeNames()
        {
            using var f = new ClockedFactory();
            var admin = await f.LoginClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
            var created = await (await admin.PostAsJsonAsync("/api/v1/users", new { name = "<script>alert(1)</script>", email = $"x-{Guid.NewGuid():N}@test.local", role = "Developer" })).Content.ReadFromJsonAsync<JsonElement>();
            var id = created.GetProperty("user").GetProperty("appUserId").GetInt32();
            await f.AddEntryAsync(id, "Acme", "Web", Coding, ClockedFactory.Monday.AddHours(9), 60);
            await f.SubmitAsync(id, ClockedFactory.Monday);
            var browser = await AdminBrowserAsync(f);

            foreach (var url in new[] { "/Admin", "/Admin/Submissions", "/Admin/Exports" })
            {
                var html = await browser.GetStringAsync(url);
                Assert.DoesNotContain("<script>alert(1)</script>", html);
                Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
            }
        }
    }
}
