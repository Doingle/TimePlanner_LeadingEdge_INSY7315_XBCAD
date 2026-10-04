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
        public async Task Home_BeforeTheFirstSubmission_ShowsEverythingInPlaceButEmpty()
        {
            var person = await SignedInAsync();

            var html = await person.Browser.GetStringAsync("/");

            Assert.Contains("<p class=\"home-hero__date\">Wednesday 16 September</p>", html);
            Assert.Contains("<span class=\"home-hero__num\">0:00</span>", html);
            Assert.Contains("0% of your 8 h goal", html);
            Assert.Contains("Nothing submitted yet. At the end of the day, submit it from the widget.", html);
            //the empty track with its axis over today's usual working day
            Assert.Contains("data-day-start=\"08:00\" data-day-end=\"17:00\"", html);
            Assert.Contains("<p class=\"home-ribbon__empty\">Nothing logged yet.", html);
            Assert.DoesNotContain("class=\"home-block", html);
            Assert.Equal(6, Regex.Matches(html, "class=\"home-axis__tick\"").Count);
            //and the breakdown with its empty states
            Assert.Contains("Where today went", html);
            Assert.Contains("Nothing logged today yet", html);
            Assert.Contains("value=\"today\" aria-pressed=\"true\">Today</button>", html);
            Assert.Contains("Categories appear here", html);
            Assert.Contains("Projects appear here", html);
            Assert.DoesNotContain("home-alert", html);
        }

        [Fact]
        public async Task Home_ShowsTheLatestSubmittedDay_AndWhetherTodayWasSent()
        {
            var person = await SignedInAsync();
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Tuesday);

            var before = await person.Browser.GetStringAsync("/");
            await _factory.SubmitAsync(person.ProfileId, Today);
            var after = await person.Browser.GetStringAsync("/");

            Assert.Contains("<p class=\"home-hero__date\">Tuesday 15 September, submitted at 17:05</p>", before);
            Assert.Contains("Today isn't submitted yet. It shows up here once you submit it from the widget.", before);
            //Tuesday was sent with nothing on it, so its track is empty
            Assert.Contains("<p class=\"home-ribbon__empty\">Nothing was logged on Tuesday 15 September.</p>", before);
            Assert.Contains("Nothing logged on Tue 15 Sep", before);
            Assert.Contains("<p class=\"home-hero__date\">Wednesday 16 September (today), submitted at 17:05</p>", after);
            Assert.DoesNotContain("Today isn't submitted yet", after);
        }

        [Fact]
        public async Task Home_ShowsEachEntryAsABlock_WithItsColourLabelAndTooltip()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, ClockedFactory.Tuesday.AddHours(9), 90, Coding, "Acme", "Web", "login page");
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Tuesday);

            var html = await person.Browser.GetStringAsync("/");

            Assert.Contains("<li class=\"home-block\" data-start=\"09:00\" data-end=\"10:30\" data-tip=\"Coding, 09:00 to 10:30, 1:30 · Acme / Web: login page\">", html);
            Assert.Contains("<span class=\"home-block__bar\" data-colour=\"#7C3AED\"></span>", html);
            Assert.Contains("<span class=\"home-block__name\">Coding</span>", html);
            Assert.Contains("<span class=\"home-block__detail home-block__time\">09:00–10:30</span>", html);
            Assert.Contains("<span class=\"home-hero__num\">1:30</span> <span class=\"home-hero__unit\">logged</span>", html);
            Assert.Contains("19% of your 8 h goal", html);
            Assert.Contains("data-day-start=\"08:00\" data-day-end=\"17:00\"", html);
            //an axis label every two hours, and one at the end of the day
            Assert.Equal(new[] { "08:00", "10:00", "12:00", "14:00", "16:00", "17:00" },
                Regex.Matches(html, "class=\"home-axis__tick\" data-at=\"([0-9:]+)\"").Select(m => m.Groups[1].Value));
        }

        [Fact]
        public async Task Home_PointsOutUnloggedTime_WithAHatchedBlockAndANote()
        {
            var earlier = await SignedInAsync();
            var today = await SignedInAsync();
            foreach (var (person, day) in new[] { (earlier, ClockedFactory.Tuesday), (today, Today) })
            {
                await Add(person.ProfileId, day.AddHours(9), 60);
                await Add(person.ProfileId, day.AddHours(11), 60);
                await Add(person.ProfileId, day.AddHours(14), 60);
                await _factory.SubmitAsync(person.ProfileId, day);
            }

            var earlierHtml = await earlier.Browser.GetStringAsync("/");
            var todayHtml = await today.Browser.GetStringAsync("/");

            Assert.Contains("<li class=\"home-block home-block--gap\" data-start=\"10:00\" data-end=\"11:00\" data-tip=\"Not logged, 10:00 to 11:00, 1:00\">", earlierHtml);
            Assert.Contains("<span class=\"home-block__name\">Not logged</span>", earlierHtml);
            Assert.Contains("<strong>2:00 wasn't logged, 10:00 to 11:00 and 13:00 to 14:00.</strong> To fill it, add the time in the widget and submit that day again.", Text(earlierHtml));
            Assert.Contains("3:00 logged, plus 2:00 not logged", earlierHtml);
            Assert.Contains("To fill it, add the time in the widget and submit today again.", Text(todayHtml));
        }

        [Fact]
        public async Task Home_ListsCategoriesAndProjects_WithBarsSharesAndBillingNotes()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, Today.AddHours(8), 180, Coding, "Acme", "Web");
            await Add(person.ProfileId, Today.AddHours(11), 60, Email, InternalCompany, "Internal");
            await _factory.SubmitAsync(person.ProfileId, Today);

            var html = await person.Browser.GetStringAsync("/");

            //each bar is measured against the largest row
            Assert.Contains("<span class=\"home-bar-row__fill\" data-percent=\"100\" data-colour=\"#7C3AED\"></span>", html);
            Assert.Matches("<span class=\"home-bar-row__fill\" data-percent=\"33.3\" data-colour=\"#[0-9A-Fa-f]{6}\"></span>", html);
            Assert.Contains("<span class=\"home-bar-row__time\">3:00</span>", html);
            Assert.Contains("<span class=\"home-bar-row__pct\">75%</span>", html);
            Assert.Contains("Acme / Web <small>Billable</small>", html);
            Assert.Contains("home-bar-row__fill home-bar-row__fill--billable\" data-percent=\"100\"", html);
            Assert.Contains($"{InternalCompany} / Internal <small>Non-billable</small>", html);
            Assert.Contains("home-bar-row__fill home-bar-row__fill--non-billable\" data-percent=\"33.3\"", html);
            Assert.Contains("Where today went", html);
            Assert.Contains("4:00 logged today", html);
        }

        [Fact]
        public async Task Home_TheToggleShowsTheWeek_AndMarksTheChosenPeriod()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, ClockedFactory.Monday.AddHours(9), 60, Meeting);
            await Add(person.ProfileId, ClockedFactory.Tuesday.AddHours(9), 120, Coding);
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Monday);
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Tuesday);

            var day = await person.Browser.GetStringAsync("/");
            var week = await person.Browser.GetStringAsync("/?period=week");

            //the day option is the latest submitted day, named by its date
            Assert.Contains("value=\"today\" aria-pressed=\"true\">Tue 15 Sep</button>", day);
            Assert.Contains("value=\"week\" aria-pressed=\"false\"", day);
            Assert.Contains("Where Tuesday went", day);
            Assert.Contains("2:00 logged on Tue 15 Sep", day);
            Assert.DoesNotContain("Meeting", Regex.Match(day, "By category.*", RegexOptions.Singleline).Value);
            Assert.Contains("value=\"week\" aria-pressed=\"true\"", week);
            Assert.Contains("Where this week went", week);
            Assert.Contains("3:00 logged from 2 submitted days", week);
            Assert.Contains("Meeting", Regex.Match(week, "By category.*", RegexOptions.Singleline).Value);
            //the ribbon is always the latest day's, whichever period the breakdown shows
            Assert.Single(Regex.Matches(week, "class=\"home-block\""));
        }

        [Fact]
        public async Task Home_NeverWritesInlineStyles_BecauseThePolicyBlocksThem_AndEncodesWhatPeopleTyped()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, Today.AddHours(9), 60, Coding, "<script>alert(1)</script>", "Web", "\"><img src=x>");
            await _factory.SubmitAsync(person.ProfileId, Today);

            var html = await person.Browser.GetStringAsync("/");

            Assert.Contains("home-block__more", html);
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
            var lastWeek = await person.Browser.GetStringAsync("/Timesheet?date=2026-09-07");

            Assert.Contains("14 Sep to 20 Sep 2026", html);
            Assert.Contains("<span class=\"ts-hero__num\">1:30</span> <span class=\"ts-hero__unit\">submitted this week</span>", html);
            Assert.Contains("From 1 submitted day. Today joins once you submit it from the widget.", Text(html));
            //the missed day is named and links to its place below
            Assert.Contains("<a class=\"ts-hero__link\" href=\"#day-2026-09-15\" data-select=\"2026-09-15\">Tue 15 Sep</a>", html);
            Assert.Contains("wasn't submitted.", html);

            //a calendar tile for each day of the week: done, missed, today, then days to come
            Assert.Contains("<a class=\"ts-tile ts-tile--done\" href=\"#day-2026-09-14\" aria-label=\"Monday 14 September, 1:30, submitted\">", html);
            Assert.Contains("<a class=\"ts-tile ts-tile--missed\" href=\"#day-2026-09-15\" aria-label=\"Tuesday 15 September, not submitted\">", html);
            Assert.Contains("<a class=\"ts-tile ts-tile--today\" href=\"#day-2026-09-16\" aria-label=\"Wednesday 16 September, today, not submitted yet\" aria-current=\"date\">", html);
            Assert.Contains("<div class=\"ts-tile ts-tile--future\" role=\"img\" aria-label=\"Thursday 17 September\">", html);

            //each day that has entries or still needs submitting, oldest first. Days still to come with nothing logged are left out
            Assert.Contains("ts-day--submitted", html);
            Assert.Contains("Submitted at 17:05", html);
            Assert.Contains("ts-day--missing", html);
            Assert.Contains("ts-day--pending", html);
            Assert.Contains("Today, not submitted yet", html);
            Assert.DoesNotContain("ts-day--notdue", html);
            Assert.True(html.IndexOf("id=\"day-2026-09-14-h\"", StringComparison.Ordinal) < html.IndexOf("id=\"day-2026-09-16-h\"", StringComparison.Ordinal));

            //the entry is one row: times, category in its colour, project with company and billing, note and length
            Assert.Matches(@"<span class=""ts-entry__time"" role=""cell"">09:00–10:30</span>\s*<span class=""ts-entry__cat"" role=""cell""><span class=""ts-entry__swatch"" data-colour=""#7C3AED""></span>Coding</span>\s*" +
                           @"<span class=""ts-entry__proj"" role=""cell"">Web <small>Acme · Billable</small></span>\s*<span class=""ts-entry__note"" role=""cell"">login page</span>\s*<span class=""ts-entry__dur"" role=""cell"">1:30</span>", html);
            Assert.Contains("Entries can't be edited here.", html);

            //the previous week opens, the next one does not exist yet. An earlier week can step forward
            Assert.Contains("href=\"/Timesheet?view=week&amp;date=2026-09-07\"", html);
            Assert.DoesNotContain("date=2026-09-21", html);
            Assert.Contains("aria-disabled=\"true\"", html);
            Assert.Contains("href=\"/Timesheet?view=week&amp;date=2026-09-14\"", lastWeek);
            Assert.Contains("submitted that week", lastWeek);
        }

        [Fact]
        public async Task Timesheet_AMonthIsACalendar_ThatOpensTheLatestSubmittedDayFirst()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, ClockedFactory.Monday.AddHours(9), 90);
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Monday);

            var html = await person.Browser.GetStringAsync("/Timesheet?view=month");

            Assert.Contains("ts-cal ts-cal--month", html);
            Assert.Contains("submitted in September", html);
            //1 September 2026 is a Tuesday, so the grid starts with one blank Monday and ends with four blanks after Wednesday the 30th
            Assert.Equal(5, Regex.Matches(html, "ts-tile ts-tile--blank").Count);
            Assert.Contains("1 of 11 working days so far.", html);
            //more than a few missed days are counted rather than listed
            Assert.Contains("10 days weren't submitted. They're ringed in amber on the calendar.", Text(html));
            Assert.DoesNotContain("ts-hero__link", html);
            //the days that can be opened are buttons, the latest submitted one starts chosen
            Assert.Contains("<button class=\"ts-tile ts-tile--done\" type=\"button\" data-date=\"2026-09-14\" aria-pressed=\"true\" tabindex=\"0\"", html);
            Assert.Contains("<button class=\"ts-tile ts-tile--missed\" type=\"button\" data-date=\"2026-09-15\" aria-pressed=\"false\" tabindex=\"-1\"", html);
            Assert.Contains("data-selected=\"2026-09-14\"", html);
            Assert.Contains("src=\"/js/timesheet.js", html);
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

            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Monday);
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Tuesday);

            var week = await person.Browser.GetStringAsync("/Report?view=week");
            var month = await person.Browser.GetStringAsync("/Report?view=month&date=2026-09-16");

            Assert.Contains("14 Sep to 20 Sep 2026", week);
            Assert.Contains("<span class=\"rp-hero__num\">3:00</span> <span class=\"rp-hero__unit\">submitted this week</span>", week);
            Assert.Contains("From 2 submitted days.", week);
            //the billable split: the hours, their shares, and a part of the bar each
            Assert.Matches(@"<dt>Billable</dt>\s*<dd><span class=""rp-billing__hours"">2:00</span><span class=""rp-billing__pct"">67%</span></dd>", week);
            Assert.Matches(@"<dt>Non-billable</dt>\s*<dd><span class=""rp-billing__hours"">1:00</span><span class=""rp-billing__pct"">33%</span></dd>", week);
            Assert.Contains("<span class=\"rp-seg\" data-percent=\"66.7\"></span>", week);
            Assert.Contains("<span class=\"rp-seg rp-seg--nb rp-seg--apart\" data-percent=\"33.3\"></span>", week);
            Assert.Contains("Where this week went", week);
            Assert.Contains("By category", week);
            Assert.Contains("Acme / Web <small>Billable</small>", week);
            //the download is this week's timesheet, uploading opens the csv upload
            Assert.Contains("href=\"/Report/Export?from=2026-09-14&amp;to=2026-09-20\"", week);
            Assert.Contains("href=\"/CsvUpload\"", week);
            //the previous week opens, the next one does not exist yet
            Assert.Contains("href=\"/Report?view=week&amp;date=2026-09-07\"", week);
            Assert.DoesNotContain("date=2026-09-21", week);
            Assert.Contains("September 2026", month);
            Assert.Contains("submitted in September", month);
            Assert.Contains("Where September went", month);
        }

        [Fact]
        public async Task Reports_AnEmptyWeek_HasNothingToDownload()
        {
            var person = await SignedInAsync();

            var html = await person.Browser.GetStringAsync("/Report?view=week&date=2026-09-07");

            Assert.Contains("<span class=\"rp-hero__num\">0:00</span> <span class=\"rp-hero__unit\">submitted that week</span>", html);
            Assert.Contains("Nothing was submitted that week.", html);
            Assert.Contains("<a class=\"rp-button rp-button--primary\" role=\"link\" aria-disabled=\"true\" aria-describedby=\"rp-download-why\">", html);
            Assert.Contains("Nothing to download for 7 Sep to 13 Sep 2026", html);
            Assert.Contains("Categories appear here", html);
        }

        [Fact]
        public async Task Reports_AnUnknownViewIsExplained_AndACustomRangeStillWorks()
        {
            var person = await SignedInAsync();
            await Add(person.ProfileId, ClockedFactory.Monday.AddHours(9), 60);

            var bad = await person.Browser.GetStringAsync("/Report?view=year");
            var range = await person.Browser.GetStringAsync("/Report?from=2026-09-01&to=2026-09-30");

            Assert.Contains("view must be week or month", bad);
            Assert.Contains("1 Sep 2026 to 30 Sep 2026", range);
            Assert.Contains("<span class=\"rp-hero__num\">1:00</span> <span class=\"rp-hero__unit\">submitted in this range</span>", range);
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
