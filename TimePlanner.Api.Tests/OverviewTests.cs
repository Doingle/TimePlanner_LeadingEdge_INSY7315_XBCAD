using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the home screen data: today and the week, the goal, the timeline and its gaps, the breakdowns and the last submission.
    //the host's clock is fixed at Wednesday 16 September 2026, 14:30 in South Africa
    public class OverviewTests : IClassFixture<ClockedFactory>
    {
        private const int Meeting = 1, Coding = 2, Email = 4;
        private const string InternalCompany = "Leading Edge (Internal)";

        private static readonly DateTime Today = ClockedFactory.Wednesday;

        private readonly ClockedFactory _factory;

        public OverviewTests(ClockedFactory factory) => _factory = factory;

        private async Task<(HttpClient Client, int ProfileId, string Email)> PersonAsync()
        {
            var user = await _factory.CreateLinkedUserAsync();
            return (await _factory.LoginClientAsync(user.Email, user.Password), user.AppUserId, user.Email);
        }

        private static Task Add(ClockedFactory f, int profile, DateTime start, int minutes, int category = Coding, string company = "Acme", string project = "Web", string? note = null) =>
            f.AddEntryAsync(profile, company, project, category, start, minutes, note);

        private static async Task<JsonElement> GetAsync(HttpClient client, string? period = null) =>
            await client.GetFromJsonAsync<JsonElement>("/api/v1/overview" + (period == null ? "" : $"?period={period}"));

        // ---------- access ----------

        [Fact]
        public async Task TheOverview_RequiresALoginWithAProfile_AndAValidPeriod()
        {
            var (email, password) = await _factory.CreateUserAsync();
            var unlinked = await _factory.LoginClientAsync(email, password);
            var person = await PersonAsync();

            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.NewClient().GetAsync("/api/v1/overview")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await unlinked.GetAsync("/api/v1/overview")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await person.Client.GetAsync("/api/v1/overview?period=year")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await person.Client.GetAsync("/api/v1/overview?period=week")).StatusCode);
        }

        // ---------- an empty day ----------

        [Fact]
        public async Task ANewPerson_HasAnEmptyDay_WithTheDefaultWorkingHoursAndGoal()
        {
            var person = await PersonAsync();

            var o = await GetAsync(person.Client);

            Assert.Equal("2026-09-16T00:00:00", o.GetProperty("today").GetString());
            Assert.Equal("today", o.GetProperty("period").GetString());
            Assert.Equal("08:00", o.GetProperty("dayStart").GetString());
            Assert.Equal("17:00", o.GetProperty("dayEnd").GetString());
            Assert.Equal(0, o.GetProperty("todayMinutes").GetInt32());
            Assert.Equal(0, o.GetProperty("todayEntries").GetInt32());
            Assert.Equal(8.0, o.GetProperty("goalHours").GetDouble());
            Assert.Equal(0.0, o.GetProperty("goalProgressPercent").GetDouble());
            Assert.False(o.GetProperty("submittedToday").GetBoolean());
            Assert.Equal(JsonValueKind.Null, o.GetProperty("lastSubmission").ValueKind);
            Assert.Empty(o.GetProperty("timeline").EnumerateArray());
            Assert.Empty(o.GetProperty("unlogged").EnumerateArray());
            Assert.Empty(o.GetProperty("breakdown").GetProperty("byCategory").EnumerateArray());
        }

        // ---------- today and the week ----------

        [Fact]
        public async Task TodayAndTheWeek_AreCountedSeparately_AndOtherWeeksAndPeopleAreLeftOut()
        {
            var person = await PersonAsync();
            var other = await PersonAsync();
            await Add(_factory, person.ProfileId, Today.AddHours(9), 90);                       // today
            await Add(_factory, person.ProfileId, Today.AddHours(11), 30, Email);               // today
            await Add(_factory, person.ProfileId, ClockedFactory.Monday.AddHours(9), 60);       // earlier this week
            await Add(_factory, person.ProfileId, ClockedFactory.Monday.AddDays(-1).AddHours(9), 600); // last Sunday: not this week
            await Add(_factory, person.ProfileId, ClockedFactory.Sunday.AddDays(1).AddHours(9), 600);  // next Monday: not this week
            await Add(_factory, other.ProfileId, Today.AddHours(9), 600);                       // someone else

            var o = await GetAsync(person.Client);

            Assert.Equal(120, o.GetProperty("todayMinutes").GetInt32());
            Assert.Equal(2, o.GetProperty("todayEntries").GetInt32());
            Assert.Equal(180, o.GetProperty("weekMinutes").GetInt32());
            Assert.Equal(3, o.GetProperty("weekEntries").GetInt32());
        }

        [Fact]
        public async Task GoalProgress_UsesTheDefaultOrThePersonsOwnGoal()
        {
            var person = await PersonAsync();
            var custom = await PersonAsync();
            await _factory.SetGoalAsync(custom.ProfileId, 6);
            await Add(_factory, person.ProfileId, Today.AddHours(9), 120);
            await Add(_factory, custom.ProfileId, Today.AddHours(9), 120);

            var byDefault = await GetAsync(person.Client);
            var byOwn = await GetAsync(custom.Client);

            Assert.Equal(25.0, byDefault.GetProperty("goalProgressPercent").GetDouble());
            Assert.Equal(6.0, byOwn.GetProperty("goalHours").GetDouble());
            Assert.Equal(33.3, byOwn.GetProperty("goalProgressPercent").GetDouble());
        }

        [Fact]
        public async Task GoalProgress_CanPassOneHundredPercent()
        {
            var person = await PersonAsync();
            await Add(_factory, person.ProfileId, Today.AddHours(8), 600);

            Assert.Equal(125.0, (await GetAsync(person.Client)).GetProperty("goalProgressPercent").GetDouble());
        }

        // ---------- the timeline ----------

        [Fact]
        public async Task TheTimeline_HasABlockPerEntry_WithTheCategoryColourAndLabel()
        {
            var person = await PersonAsync();
            await Add(_factory, person.ProfileId, Today.AddHours(9), 90, Coding, "Acme", "Web", "login page");
            await Add(_factory, person.ProfileId, Today.AddHours(10).AddMinutes(45), 30, Email, "Acme", "Web");

            var timeline = (await GetAsync(person.Client)).GetProperty("timeline").EnumerateArray().ToArray();

            Assert.Equal(2, timeline.Length);
            Assert.Equal("09:00", timeline[0].GetProperty("start").GetString());
            Assert.Equal("10:30", timeline[0].GetProperty("end").GetString());
            Assert.Equal(90, timeline[0].GetProperty("minutes").GetInt32());
            Assert.Equal("#7C3AED", timeline[0].GetProperty("colour").GetString());
            Assert.Equal("Coding", timeline[0].GetProperty("label").GetString());
            Assert.Equal("Acme / Web: login page", timeline[0].GetProperty("title").GetString());
            Assert.Equal("Email", timeline[1].GetProperty("label").GetString());
            Assert.Equal("Acme / Web", timeline[1].GetProperty("title").GetString());
        }

        [Fact]
        public async Task SubActivities_ShowUnderTheirTopLevelCategory()
        {
            var person = await PersonAsync();
            var import = await person.Client.PostAsJsonAsync("/api/v1/timesheets/import", new
            {
                entries = new[] { new { company = "Acme", project = "Web", activity = "Coding > Frontend", start = Today.AddHours(9).ToString("s"), end = Today.AddHours(10).ToString("s") } }
            });
            Assert.Equal(HttpStatusCode.OK, import.StatusCode);

            var o = await GetAsync(person.Client);

            Assert.Equal("Coding", o.GetProperty("timeline")[0].GetProperty("label").GetString());
            Assert.Equal("Coding", o.GetProperty("breakdown").GetProperty("byCategory")[0].GetProperty("label").GetString());
        }

        // ---------- time nobody logged ----------

        [Fact]
        public async Task GapsBetweenEntries_AreReported_ButNotLunchOrShortOnes()
        {
            var person = await PersonAsync();
            await Add(_factory, person.ProfileId, Today.AddHours(8), 60);                          // 08:00-09:00
            await Add(_factory, person.ProfileId, Today.AddHours(10), 60);                         // gap 09:00-10:00 (60)
            await Add(_factory, person.ProfileId, Today.AddHours(11).AddMinutes(10), 50);          // 11:10-12:00, gap 11:00-11:10 is too short
            await Add(_factory, person.ProfileId, Today.AddHours(14), 60);                         // gap 12:00-14:00 is lunch 12-13 plus 13:00-14:00 (60)

            var gaps = (await GetAsync(person.Client)).GetProperty("unlogged").EnumerateArray().Select(g => (g.GetProperty("start").GetString()!, g.GetProperty("end").GetString()!, g.GetProperty("minutes").GetInt32())).ToArray();

            Assert.Equal(new[] { ("09:00", "10:00", 60), ("13:00", "14:00", 60) }, gaps);
        }

        [Fact]
        public async Task OverlappingEntries_LeaveNoGap_AndALunchOnlyGapIsIgnored()
        {
            var person = await PersonAsync();
            await Add(_factory, person.ProfileId, Today.AddHours(9), 180);                         // 09:00-12:00
            await Add(_factory, person.ProfileId, Today.AddHours(10), 60);                         // inside the first
            await Add(_factory, person.ProfileId, Today.AddHours(13), 60);                         // the only gap is exactly lunch

            Assert.Empty((await GetAsync(person.Client)).GetProperty("unlogged").EnumerateArray());
        }

        // ---------- the working day on the timeline ----------

        [Fact]
        public async Task TheTimelineWindow_StretchesForEarlyAndLateWork()
        {
            var early = await PersonAsync();
            var late = await PersonAsync();
            await Add(_factory, early.ProfileId, Today.AddHours(7).AddMinutes(20), 50);            // 07:20-08:10
            await Add(_factory, late.ProfileId, Today.AddHours(16), 140);                          // 16:00-18:20

            var earlyDay = await GetAsync(early.Client);
            var lateDay = await GetAsync(late.Client);

            Assert.Equal("07:00", earlyDay.GetProperty("dayStart").GetString());
            Assert.Equal("17:00", earlyDay.GetProperty("dayEnd").GetString());
            Assert.Equal("08:00", lateDay.GetProperty("dayStart").GetString());
            Assert.Equal("19:00", lateDay.GetProperty("dayEnd").GetString());
        }

        // ---------- the breakdown and its toggle ----------

        [Fact]
        public async Task TheBreakdown_FollowsThePeriod_TodayByDefault()
        {
            var person = await PersonAsync();
            await Add(_factory, person.ProfileId, Today.AddHours(9), 120);                          // today, Coding
            await Add(_factory, person.ProfileId, ClockedFactory.Monday.AddHours(9), 60, Email);    // earlier this week, Email

            var today = await GetAsync(person.Client);
            var week = await GetAsync(person.Client, "week");

            Assert.Equal(new[] { "Coding" }, today.GetProperty("breakdown").GetProperty("byCategory").EnumerateArray().Select(r => r.GetProperty("label").GetString()).ToArray());
            Assert.Equal(new[] { "Coding", "Email" }, week.GetProperty("breakdown").GetProperty("byCategory").EnumerateArray().Select(r => r.GetProperty("label").GetString()).ToArray());
            Assert.Equal("week", week.GetProperty("period").GetString());
            Assert.Equal("14 Sep to 20 Sep 2026", week.GetProperty("periodLabel").GetString());
            Assert.Equal("Wed 16 Sep", today.GetProperty("periodLabel").GetString());
            //the totals for both periods are always present whichever one is shown
            Assert.Equal(120, today.GetProperty("todayMinutes").GetInt32());
            Assert.Equal(180, today.GetProperty("weekMinutes").GetInt32());
        }

        [Fact]
        public async Task TheBreakdownRows_HaveSharesColoursAndBillableFlags_LargestFirst()
        {
            var person = await PersonAsync();
            await Add(_factory, person.ProfileId, Today.AddHours(8), 180, Coding, "Acme", "Web");            // 3h
            await Add(_factory, person.ProfileId, Today.AddHours(11), 60, Email, InternalCompany, "Internal"); // 1h internal

            var breakdown = (await GetAsync(person.Client)).GetProperty("breakdown");
            var categories = breakdown.GetProperty("byCategory").EnumerateArray().ToArray();
            var projects = breakdown.GetProperty("byProject").EnumerateArray().ToArray();

            Assert.Equal(new[] { "Coding", "Email" }, categories.Select(c => c.GetProperty("label").GetString()).ToArray());
            Assert.Equal(75.0, categories[0].GetProperty("percent").GetDouble());
            Assert.Equal(25.0, categories[1].GetProperty("percent").GetDouble());
            Assert.Equal("#7C3AED", categories[0].GetProperty("colour").GetString());
            Assert.Equal(180, categories[0].GetProperty("minutes").GetInt32());
            Assert.Equal("Acme / Web", projects[0].GetProperty("label").GetString());
            Assert.True(projects[0].GetProperty("billable").GetBoolean());
            Assert.Equal($"{InternalCompany} / Internal", projects[1].GetProperty("label").GetString());
            Assert.False(projects[1].GetProperty("billable").GetBoolean());
            Assert.Equal(3.0, breakdown.GetProperty("billableHours").GetDouble());
            Assert.Equal(1.0, breakdown.GetProperty("nonBillableHours").GetDouble());
        }

        // ---------- submissions ----------

        [Fact]
        public async Task TheLastSubmission_IsTheLatestDay_OnTheCompanysClock()
        {
            var person = await PersonAsync();
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Monday);
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Tuesday);

            var o = await GetAsync(person.Client);
            var last = o.GetProperty("lastSubmission");

            Assert.Equal("2026-09-15T00:00:00", last.GetProperty("date").GetString());
            //submitted at 15:05 UTC, which is 17:05 in South Africa
            Assert.Equal("2026-09-15T17:05:00+02:00", last.GetProperty("submittedAt").GetString());
            Assert.False(o.GetProperty("submittedToday").GetBoolean());
        }

        [Fact]
        public async Task SubmittedToday_IsTrueOnceTodayWasSent()
        {
            var person = await PersonAsync();
            await _factory.SubmitAsync(person.ProfileId, Today);

            Assert.True((await GetAsync(person.Client)).GetProperty("submittedToday").GetBoolean());
        }

        // ---------- the latest submitted day ----------

        [Fact]
        public async Task TheLatestDay_IsTheLastSubmittedDayInFull_EvenWhenItIsNotToday()
        {
            var person = await PersonAsync();
            await Add(_factory, person.ProfileId, ClockedFactory.Monday.AddHours(9), 60, Meeting);
            await Add(_factory, person.ProfileId, ClockedFactory.Tuesday.AddHours(9), 60);
            await Add(_factory, person.ProfileId, ClockedFactory.Tuesday.AddHours(11), 90, Email);
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Monday);
            await _factory.SubmitAsync(person.ProfileId, ClockedFactory.Tuesday);

            var o = await GetAsync(person.Client);
            var day = o.GetProperty("latestDay");

            Assert.Equal("2026-09-15T00:00:00", day.GetProperty("date").GetString());
            Assert.Equal("2026-09-15T17:05:00+02:00", day.GetProperty("submittedAt").GetString());
            Assert.Equal(150, day.GetProperty("minutes").GetInt32());
            Assert.Equal("08:00", day.GetProperty("dayStart").GetString());
            Assert.Equal("17:00", day.GetProperty("dayEnd").GetString());
            Assert.Equal(new[] { "09:00", "11:00" }, day.GetProperty("timeline").EnumerateArray().Select(b => b.GetProperty("start").GetString()));
            var gap = Assert.Single(day.GetProperty("unlogged").EnumerateArray());
            Assert.Equal("10:00", gap.GetProperty("start").GetString());
            Assert.Equal("11:00", gap.GetProperty("end").GetString());
            //only Tuesday's entries: Monday's meeting is left out of its breakdown
            Assert.Equal(new[] { "Email", "Coding" }, day.GetProperty("breakdown").GetProperty("byCategory").EnumerateArray().Select(r => r.GetProperty("label").GetString()));
            //today was not sent, so today's own timeline stays empty
            Assert.Empty(o.GetProperty("timeline").EnumerateArray());
            Assert.Equal(2, o.GetProperty("weekSubmittedDays").GetInt32());
        }

        [Fact]
        public async Task TheLatestDay_IsNullBeforeTheFirstSubmission_AndCanBeInAnEarlierWeek()
        {
            var newcomer = await PersonAsync();
            var lastWeek = await PersonAsync();
            var today = await PersonAsync();
            await _factory.SubmitAsync(lastWeek.ProfileId, ClockedFactory.Monday.AddDays(-3));
            await Add(_factory, today.ProfileId, Today.AddHours(9), 30);
            await _factory.SubmitAsync(today.ProfileId, Today);

            var none = await GetAsync(newcomer.Client);
            var friday = await GetAsync(lastWeek.Client);
            var sent = await GetAsync(today.Client);

            Assert.Equal(JsonValueKind.Null, none.GetProperty("latestDay").ValueKind);
            Assert.Equal(0, none.GetProperty("weekSubmittedDays").GetInt32());
            Assert.Equal("2026-09-11T00:00:00", friday.GetProperty("latestDay").GetProperty("date").GetString());
            Assert.Equal(0, friday.GetProperty("latestDay").GetProperty("minutes").GetInt32());
            Assert.Equal(0, friday.GetProperty("weekSubmittedDays").GetInt32());
            Assert.Equal("2026-09-16T00:00:00", sent.GetProperty("latestDay").GetProperty("date").GetString());
            Assert.Equal(30, sent.GetProperty("latestDay").GetProperty("minutes").GetInt32());
            Assert.Equal(1, sent.GetProperty("weekSubmittedDays").GetInt32());
        }

        // ---------- the company's day, not the server's ----------

        [Fact]
        public async Task Today_IsTheCompanysToday_EvenWhenItIsStillYesterdayOnTheServer()
        {
            //22:30 UTC on the 15th is 00:30 on the 16th in South Africa
            using var factory = new ClockedFactory(new DateTimeOffset(2026, 9, 15, 22, 30, 0, TimeSpan.Zero));
            var user = await factory.CreateLinkedUserAsync();
            var client = await factory.LoginClientAsync(user.Email, user.Password);
            await factory.AddEntryAsync(user.AppUserId, "Acme", "Web", Coding, new DateTime(2026, 9, 16, 0, 5, 0), 20);

            var o = await GetAsync(client);

            Assert.Equal("2026-09-16T00:00:00", o.GetProperty("today").GetString());
            Assert.Equal(20, o.GetProperty("todayMinutes").GetInt32());
        }
    }
}
