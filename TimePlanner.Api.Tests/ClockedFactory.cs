using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //a clock that always says the same moment
    public class FixedTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        //a test can move the clock on, for example to show that sending a day again records the later time
        public void Set(DateTimeOffset now) => _now = now;
    }

    //-----------------------------
    //a host whose "now" is fixed, so screens that depend on today and this week give the same answer on any day the tests run.
    //the default moment is Wednesday 16 September 2026, 14:30 in South Africa (12:30 UTC). That week runs Monday 14 to Sunday 20 September.
    //it is in the past so entries on those days pass the import rule that refuses the future
    public class ClockedFactory : ApiFactory
    {
        public static readonly DateTime Monday = new(2026, 9, 14);
        public static readonly DateTime Tuesday = Monday.AddDays(1);
        public static readonly DateTime Wednesday = Monday.AddDays(2);
        public static readonly DateTime Thursday = Monday.AddDays(3);
        public static readonly DateTime Friday = Monday.AddDays(4);
        public static readonly DateTime Saturday = Monday.AddDays(5);
        public static readonly DateTime Sunday = Monday.AddDays(6);

        public static readonly DateTimeOffset DefaultNow = new(2026, 9, 16, 12, 30, 0, TimeSpan.Zero);

        private readonly FixedTimeProvider _clock;

        //the clock this host runs on
        public FixedTimeProvider Clock => _clock;

        //xunit builds a shared fixture with a constructor that takes nothing, a test that needs another moment passes it explicitly
        public ClockedFactory() : this(DefaultNow) { }

        internal ClockedFactory(DateTimeOffset now) => _clock = new FixedTimeProvider(now);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(_clock));
        }

        //-----------------------------
        //records that a person's day was submitted at a given moment (UTC), without going through an import
        public async Task AddSubmissionAsync(int appUserId, DateTime day, DateTime submittedAtUtc)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            db.DaySubmissions.Add(new DaySubmission { AppUserId = appUserId, Date = day.Date, SubmittedAtUtc = DateTime.SpecifyKind(submittedAtUtc, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        }

        //-----------------------------
        //submits a day at 17:05 company time (15:05 UTC) on that same day, the example in the design
        public Task SubmitAsync(int appUserId, DateTime day) => AddSubmissionAsync(appUserId, day, day.Date.AddHours(15).AddMinutes(5));

        public async Task ClearSubmissionsAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            db.DaySubmissions.RemoveRange(db.DaySubmissions);
            await db.SaveChangesAsync();
        }

        public async Task<List<DaySubmission>> SubmissionsAsync(int? appUserId = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            return db.DaySubmissions.Where(s => appUserId == null || s.AppUserId == appUserId).OrderBy(s => s.Date).ToList();
        }

        //-----------------------------
        //gives a person's profile their own daily goal, like the one a widget user sets
        public async Task SetGoalAsync(int appUserId, double hours)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<UserSettings>().Add(new UserSettings { UserId = appUserId, DailyGoalHours = hours });
            await db.SaveChangesAsync();
        }
    }
}
//------------------------------EOF-----------------------------\\
