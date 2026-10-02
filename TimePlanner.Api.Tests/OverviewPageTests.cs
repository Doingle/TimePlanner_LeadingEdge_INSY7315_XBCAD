using System.Net;
using System.Text.RegularExpressions;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the pages built on the overview data: Home (against the markup the design expects), My Timesheet, the My Reports shortcuts and the navigation.
    //the host's clock is fixed at Wednesday 16 September 2026, 14:30 in South Africa
    public class OverviewPageTests : IClassFixture<ClockedFactory>
    {
        private const int Meeting = 1, Coding = 2, Email = 4;
        private const string InternalCompany = "Leading Edge (Internal)";

        private static readonly DateTime Today = ClockedFactory.Wednesday;

        private readonly ClockedFactory _factory;

        public OverviewPageTests(ClockedFactory factory) => _factory = factory;

        private async Task<(HttpClient Browser, int ProfileId)> SignedInAsync(string role = "Developer")
        {
            var user = await _factory.CreateLinkedUserAsync(role);
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, user.Email, user.Password);
            return (browser, user.AppUserId);
        }

        //wording is compared as a person reads it: HTML entities decoded and runs of white space collapsed
        private static string Text(string html) => Regex.Replace(WebUtility.HtmlDecode(html), @"\s+", " ");

        private Task Add(int profile, DateTime start, int minutes, int category = Coding, string company = "Acme", string project = "Web", string? note = null) =>
            _factory.AddEntryAsync(profile, company, project, category, start, minutes, note);

        // ---------- Home ----------

        [Fact]
        public async Task Home_RequiresALogin_AndExplainsAnUnlinkedAccount()
        {
            var (email, password) = await _factory.CreateUserAsync();
            var unlinked = _factory.NewClient();
            await ApiFactory.PostLoginAsync(unlinked, email, password);

            Assert.Equal(HttpStatusCode.Redirect, (await _factory.NewClient().GetAsync("/")).StatusCode);
            Assert.Contains("not linked to a time tracking profile", await unlinked.GetStringAsync("/"));
        }

        [Fact]
        public async Task Home_ForAnEmptyDay_ShowsTheDesignsEmptyStates()
        {
            var person = await SignedInAsync();

            var html = await person.Browser.GetStringAsync("/");

            Assert.Contains("Today, Wed 16 Sep", html);
            Assert.Contains("Last submission: <strong>—</strong>", html);
            Assert.Contains("Today isn't submitted yet", html);
            Assert.Contains("08:00–17:00", html);
            Assert.Contains("data-day-start=\"08:00\" data-day-end=\"17:00\"", html);
            Assert.Contains("Nothing logged yet", html);
            Assert.Contains("Nothing logged today yet", html);
            Assert.Contains("<strong>0:00</strong> logged (0% of your 8 h goal)", html);
            Assert.Contains("Categories appear here", html);
            Assert.Contains("Projects appear here", html);
            Assert.DoesNotContain("home-alert", html);
            //an axis tick for every hour of the working day
            Assert.Equal(10, Regex.Matches(html, "home-axis__tick\"").Count);
        }

        [Fact]
        public async Task Home_ShowsEachEntryAsABlock_WithItsColourAndLabel()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, Today.AddHours(9), 90, Coding, "Acme", "Web", "login page");

            var html = await person.Browser.GetStringAsync("/");

            Assert.Contains("<div class=\"home-block\" data-start=\"09:00\" data-end=\"10:30\" data-colour=\"#7C3AED\" title=\"Acme / Web: login page\"><span class=\"home-block__label\">Coding</span></div>", html);
            Assert.DoesNotContain("Nothing logged yet", html);
            Assert.Contains("<strong>1:30</strong> logged (18.8% of your 8 h goal)".Replace("18.8%", "19%"), html);
        }

        [Fact]
        public async Task Home_PointsOutUnloggedTime_WithABlockAndAnAlert()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, Today.AddHours(9), 60);
            await Add(person.ProfileId, Today.AddHours(11), 60);
            await Add(person.ProfileId, Today.AddHours(14), 60);

            var html = await person.Browser.GetStringAsync("/");

            Assert.Contains("home-block home-block--unlogged\" data-start=\"10:00\" data-end=\"11:00\"", html);
            Assert.Contains("Not logged · 10:00–11:00", html);
            Assert.Contains("<strong>2:00 isn't logged, 10:00–11:00, 13:00–14:00.</strong> Add this time in the widget before you submit today.", Text(html));
        }

        [Fact]
        public async Task Home_ShowsTheLastSubmission_AndWhetherTodayWasSent()
        {
            var person = await SignedInAsync();
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Tuesday);

            var before = await person.Browser.GetStringAsync("/");
            await _factory.SubmitAsync(person.ProfileId, Today);
            var after = await person.Browser.GetStringAsync("/");

            Assert.Contains("home-status__item--done\">Last submission: <strong>Tue 15 Sep, submitted 17:05</strong>", before);
            Assert.Contains("Today isn't submitted yet", before);
            Assert.Contains("Last submission: <strong>Wed 16 Sep, submitted 17:05</strong>", after);
            Assert.Contains("Today is submitted.", after);
            Assert.DoesNotContain("Today isn't submitted yet", after);
        }

        [Fact]
        public async Task Home_ListsCategoriesAndProjects_WithBarsSharesAndBillingNotes()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, Today.AddHours(8), 180, Coding, "Acme", "Web");
            await Add(person.ProfileId, Today.AddHours(11), 60, Email, InternalCompany, "Internal");

            var html = await person.Browser.GetStringAsync("/");

            Assert.Contains("<span class=\"home-bar__fill\" data-percent=\"75\" data-colour=\"#7C3AED\"></span>", html);
            Assert.Contains("<span class=\"home-breakdown__value\"><strong>3:00</strong> · 75%</span>", html);
            Assert.Contains("Acme / Web <small class=\"home-breakdown__note\">Billable</small>", html);
            Assert.Contains("home-bar__fill home-bar__fill--billable\" data-percent=\"75\"", html);
            Assert.Contains($"{InternalCompany} / Internal <small class=\"home-breakdown__note\">Non-billable</small>", html);
            Assert.Contains("home-bar__fill home-bar__fill--non-billable\" data-percent=\"25\"", html);
            Assert.Contains("4:00 logged today", html);
        }

        [Fact]
        public async Task Home_TheToggleShowsTheWeek_AndMarksTheChosenPeriod()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, Today.AddHours(9), 120, Coding);
            await Add(person.ProfileId, ClockedFactory.Monday.AddHours(9), 60, Meeting);

            var today = await person.Browser.GetStringAsync("/");
            var week = await person.Browser.GetStringAsync("/?period=week");

            Assert.Contains("value=\"today\" aria-pressed=\"true\"", today);
            Assert.Contains("value=\"week\" aria-pressed=\"false\"", today);
            Assert.DoesNotContain("Meeting", Regex.Match(today, "By category.*", RegexOptions.Singleline).Value);
            Assert.Contains("value=\"week\" aria-pressed=\"true\"", week);
            Assert.Contains("3:00 logged this week", week);
            Assert.Contains("Meeting", Regex.Match(week, "By category.*", RegexOptions.Singleline).Value);
            //the timeline is always today's, whichever period the breakdown shows
            Assert.Single(Regex.Matches(week, "class=\"home-block\""));
        }

        [Fact]
        public async Task Home_NeverWritesInlineStyles_BecauseThePolicyBlocksThem_AndEncodesWhatPeopleTyped()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, Today.AddHours(9), 60, Coding, "<script>alert(1)</script>", "Web", "\"><img src=x>");

            var html = await person.Browser.GetStringAsync("/");

            Assert.DoesNotMatch(@"\sstyle\s*=", html);
            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.DoesNotContain("\"><img src=x>", html);
        }

        // ---------- My Timesheet ----------

        [Fact]
        public async Task Timesheet_RequiresALogin_AndExplainsAnUnlinkedAccount()
        {
            var (email, password) = await _factory.CreateUserAsync();
            var unlinked = _factory.NewClient();
            await ApiFactory.PostLoginAsync(unlinked, email, password);

            Assert.Equal(HttpStatusCode.Redirect, (await _factory.NewClient().GetAsync("/Timesheet")).StatusCode);
            Assert.Contains("not linked to a time tracking profile", await unlinked.GetStringAsync("/Timesheet"));
        }

        [Fact]
        public async Task Timesheet_ShowsTheWeekDayByDay()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, ClockedFactory.Monday.AddHours(9), 90, Coding, "Acme", "Web", "login page");
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Monday);

            var html = await person.Browser.GetStringAsync("/Timesheet");

            Assert.Contains("14 Sep to 20 Sep 2026", html);
            Assert.Contains("ts-day--submitted", html);
            Assert.Contains("Submitted at 17:05", html);
            Assert.Contains("ts-day--missing", html);
            Assert.Contains("ts-day--pending", html);
            Assert.Contains("Not submitted yet", html);
            //days still to come with nothing logged are left out
            Assert.DoesNotContain("ts-day--notdue", html);
            Assert.Contains("1:30</strong> logged, 1 day submitted, 1 missing", Text(html));
            //the entry is one row: activity, company with project, note, times and length
            Assert.Matches(@"<span class=""ts-dot""></span>Coding</span>\s*<span class=""ts-entry__company"">Acme <small>Web · Billable</small></span>\s*" +
                           @"<span class=""ts-entry__description"">login page</span>\s*<span class=""ts-entry__time"">09:00 – 10:30</span>\s*<span class=""ts-entry__total"">1:30</span>", html);
            Assert.Contains("Read only", html);
            Assert.Contains("href=\"/Timesheet?view=week&amp;date=2026-09-07\"", html);
            Assert.Contains("href=\"/Timesheet?view=week&amp;date=2026-09-21\"", html);
        }

        [Fact]
        public async Task Timesheet_CanShowAMonth_AndFallsBackToTheWeekForAnUnknownView()
        {
            var person = await SignedInAsync();

            var month = await person.Browser.GetStringAsync("/Timesheet?view=month");
            var bad = await person.Browser.GetStringAsync("/Timesheet?view=year");

            Assert.Contains("September 2026", month);
            Assert.Contains("aria-pressed=\"true\">Month", month);
            Assert.Contains("href=\"/Timesheet?view=month&amp;date=2026-08-01\"", month);
            Assert.Contains("view must be week or month", bad);
            Assert.Contains("14 Sep to 20 Sep 2026", bad);
        }

        [Fact]
        public async Task Timesheet_ShowsOnlyThePersonsOwnDays()
        {
            var alice = await SignedInAsync();
            var bob = await SignedInAsync();
            await Add(bob.ProfileId, ClockedFactory.Monday.AddHours(9), 60, Coding, "BobsClient", "Secret", "bobs note");

            var html = await alice.Browser.GetStringAsync("/Timesheet");

            Assert.DoesNotContain("BobsClient", html);
            Assert.DoesNotContain("bobs note", html);
        }

        // ---------- My Reports shortcuts ----------

        [Fact]
        public async Task Reports_OpensOnTheWeekOrMonth_WithTheSummary()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, ClockedFactory.Monday.AddHours(9), 120, Coding, "Acme", "Web");
            await Add(person.ProfileId, ClockedFactory.Tuesday.AddHours(9), 60, Email, InternalCompany, "Internal");

            var week = await person.Browser.GetStringAsync("/Report?view=week");
            var month = await person.Browser.GetStringAsync("/Report?view=month&date=2026-09-16");

            Assert.Contains("14 Sep to 20 Sep 2026", week);
            Assert.Contains("Billable <strong>2.00</strong> h", week);
            Assert.Contains("non-billable <strong>1.00</strong> h", week);
            Assert.Contains("By category", week);
            Assert.Contains("By project", week);
            Assert.Contains("Acme / Web", week);
            Assert.Contains("September 2026", month);
            Assert.Contains("href=\"/Report?view=week&amp;date=2026-09-07&amp;groupBy=Project\"", week);
            Assert.Contains("Download timesheet", week);
        }

        [Fact]
        public async Task Reports_AnUnknownViewIsExplained_AndACustomRangeStillWorks()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, ClockedFactory.Monday.AddHours(9), 60);

            var bad = await person.Browser.GetStringAsync("/Report?view=year");
            var range = await person.Browser.GetStringAsync("/Report?from=2026-09-01&to=2026-09-30");

            Assert.Contains("view must be week or month", bad);
            Assert.Contains("1.00 hours", range);
            Assert.Contains("By category", range);
        }

        // ---------- the navigation ----------

        [Fact]
        public async Task TheNavigation_LinksToEachPage_AndOnlyAdminsSeeTeam()
        {
            var developer = await SignedInAsync();
            var admin = await SignedInAsync("Admin");

            var developerHtml = await developer.Browser.GetStringAsync("/");
            var adminHtml = await admin.Browser.GetStringAsync("/");

            foreach (var html in new[] { developerHtml, adminHtml })
            {
                Assert.Contains("aria-current=\"page\" href=\"/\">Home", html);
                Assert.Contains("href=\"/Timesheet\"", html);
                Assert.Contains("href=\"/Report?view=week\"", html);
                Assert.Contains("href=\"/Settings\"", html);
            }
            Assert.DoesNotContain("href=\"/Admin\"", developerHtml);
            Assert.Contains("href=\"/Admin\"", adminHtml);
        }

        [Fact]
        public async Task TheCurrentPage_IsMarkedInTheNavigation()
        {
            var person = await SignedInAsync();

            Assert.Contains("aria-current=\"page\" href=\"/Timesheet\">My Timesheet", await person.Browser.GetStringAsync("/Timesheet"));
            Assert.Contains("aria-current=\"page\" href=\"/Report?view=week\">My Reports", await person.Browser.GetStringAsync("/Report?view=week"));
        }
    }
}
