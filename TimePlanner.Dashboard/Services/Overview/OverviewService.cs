using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Dashboard.Services.Reports;
using TimePlanner.Dashboard.Services.Submissions;

namespace TimePlanner.Dashboard.Services.Overview
{
    //-----------------------------
    //one block of the day's timeline. Start and End are "HH:mm" so a page can place them on the track, Colour is the category colour
    public record TimelineBlock(string Start, string End, int Minutes, string? Colour, string Label, string Title);

    //time between two entries that nobody logged
    public record UnloggedSpan(string Start, string End, int Minutes);

    //the most recent day the person sent in, and when (on the company's clock)
    public record SubmissionInfo(DateTime Date, DateTimeOffset SubmittedAt);

    //-----------------------------
    //the signed in person's home screen: today's timeline, their goal, last submission and the breakdown of today or this week.
    //Breakdown is for the chosen Period ("today" or "week"), TodayMinutes and WeekMinutes are always both given
    public record MyOverview(DateTime Today, string Period, string PeriodLabel, string DayStart, string DayEnd,
        int TodayMinutes, int TodayEntries, double GoalHours, double GoalProgressPercent, int WeekMinutes, int WeekEntries,
        bool SubmittedToday, SubmissionInfo? LastSubmission,
        IReadOnlyList<TimelineBlock> Timeline, IReadOnlyList<UnloggedSpan> Unlogged, ReportSummary Breakdown);

    //-----------------------------
    //builds the home screen for one person from their entries, their submissions and their daily goal
    public class OverviewService
    {
        //the working day shown on the timeline, widened when a person logged outside it
        public const string DefaultDayStart = "08:00";
        public const string DefaultDayEnd = "17:00";

        //gaps shorter than this are not worth pointing out
        private static readonly TimeSpan MinimumGap = TimeSpan.FromMinutes(15);

        private readonly ReportService _reports;
        private readonly SubmissionService _submissions;
        private readonly CompanyClock _clock;
        private readonly AppDbContext _app;
        private readonly IConfiguration _config;

        public OverviewService(ReportService reports, SubmissionService submissions, CompanyClock clock, AppDbContext app, IConfiguration config)
        {
            _reports = reports;
            _submissions = submissions;
            _clock = clock;
            _app = app;
            _config = config;
        }

        public static bool IsValidPeriod(string? period) => period is null or "today" or "week";

        public async Task<MyOverview> GetAsync(int appUserId, string? period)
        {
            var chosen = string.Equals(period, "week", StringComparison.OrdinalIgnoreCase) ? "week" : "today";
            var today = _clock.Today;
            Period.TryResolve("week", today, today, out var week);

            var entries = await _reports.LoadEntriesAsync(week.From, week.To, appUserId, null);
            var activities = await ActivityLookup.LoadAsync(_app);
            var todays = entries.Where(e => e.Start.Date == today).ToList();

            var goalHours = (await _app.Users.AsNoTracking().Include(u => u.Settings).FirstOrDefaultAsync(u => u.UserId == appUserId))?.Settings?.DailyGoalHours
                ?? _config.GetValue("Goals:DailyHours", 8.0);
            var todayMinutes = (int)Math.Round(todays.Sum(e => (e.End - e.Start).TotalMinutes));

            var latest = await _submissions.LatestForUserAsync(appUserId);
            var submittedToday = (await _submissions.ForUserAsync(appUserId, today, today)).ContainsKey(today);

            var shown = chosen == "week" ? entries : todays;
            var (dayStart, dayEnd) = Window(todays);

            return new MyOverview(today, chosen, chosen == "week" ? week.Label : today.ToString("ddd d MMM", CultureInfo.InvariantCulture), dayStart, dayEnd,
                todayMinutes, todays.Count, goalHours, goalHours <= 0 ? 0 : Math.Round(todayMinutes / (goalHours * 60) * 100, 1),
                (int)Math.Round(entries.Sum(e => (e.End - e.Start).TotalMinutes)), entries.Count,
                submittedToday, latest == null ? null : new SubmissionInfo(latest.Date, _clock.ToCompanyTime(latest.SubmittedAtUtc)),
                todays.Select(e => Block(e, activities)).ToList(), Gaps(todays),
                ReportService.Summarise(chosen == "week" ? week.From : today, chosen == "week" ? week.To : today, shown, activities));
        }

        private static TimelineBlock Block(ReportService.ReportEntry e, ActivityLookup activities) =>
            new(Clock(e.Start), Clock(e.End), (int)Math.Round((e.End - e.Start).TotalMinutes), activities.RootColour(e.CategoryId),
                activities.RootName(e.CategoryId), $"{e.Company} / {e.Project}{(string.IsNullOrWhiteSpace(e.Note) ? "" : ": " + e.Note)}");

        private static string Clock(DateTime time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

        //-----------------------------
        //the usual working day, stretched to whole hours so an early start or a late finish still fits on the track
        private (string Start, string End) Window(List<ReportService.ReportEntry> todays)
        {
            var start = TimeOnly.ParseExact(_config["Home:DayStart"] ?? DefaultDayStart, "HH:mm", CultureInfo.InvariantCulture);
            var end = TimeOnly.ParseExact(_config["Home:DayEnd"] ?? DefaultDayEnd, "HH:mm", CultureInfo.InvariantCulture);
            if (todays.Count > 0)
            {
                var first = TimeOnly.FromDateTime(todays.Min(e => e.Start));
                var last = todays.Max(e => e.End);
                if (first < start)
                    start = new TimeOnly(first.Hour, 0);
                //an entry that runs past midnight (or just reaches the next day) stops the track at the end of the day
                if (last.Date > todays[0].Start.Date)
                    end = new TimeOnly(23, 59);
                else if (TimeOnly.FromDateTime(last) > end)
                    end = new TimeOnly(Math.Min(23, TimeOnly.FromDateTime(last).Hour + (last.Minute > 0 ? 1 : 0)), 0);
            }
            return (start.ToString("HH:mm", CultureInfo.InvariantCulture), end.ToString("HH:mm", CultureInfo.InvariantCulture));
        }

        //-----------------------------
        //time between one entry ending and the next starting, minus the lunch break, when it is long enough to matter
        private IReadOnlyList<UnloggedSpan> Gaps(List<ReportService.ReportEntry> todays)
        {
            var lunchStart = TimeOnly.ParseExact(_config["Home:LunchStart"] ?? "12:00", "HH:mm", CultureInfo.InvariantCulture);
            var lunchEnd = TimeOnly.ParseExact(_config["Home:LunchEnd"] ?? "13:00", "HH:mm", CultureInfo.InvariantCulture);
            var gaps = new List<UnloggedSpan>();
            var coveredUntil = (DateTime?)null;

            foreach (var e in todays.OrderBy(e => e.Start))
            {
                if (coveredUntil != null && e.Start - coveredUntil > MinimumGap)
                    AddGap(gaps, coveredUntil.Value, e.Start, lunchStart, lunchEnd);
                if (coveredUntil == null || e.End > coveredUntil)
                    coveredUntil = e.End;
            }
            return gaps;
        }

        //a gap that overlaps lunch is split around it, and each piece is only kept if it is still long enough
        private static void AddGap(List<UnloggedSpan> gaps, DateTime from, DateTime to, TimeOnly lunchStart, TimeOnly lunchEnd)
        {
            var lunchFrom = from.Date + lunchStart.ToTimeSpan();
            var lunchTo = from.Date + lunchEnd.ToTimeSpan();
            var pieces = new List<(DateTime From, DateTime To)>();

            if (to <= lunchFrom || from >= lunchTo)
                pieces.Add((from, to));
            else
            {
                if (from < lunchFrom)
                    pieces.Add((from, lunchFrom));
                if (to > lunchTo)
                    pieces.Add((lunchTo, to));
            }

            foreach (var (a, b) in pieces.Where(p => p.To - p.From > MinimumGap))
                gaps.Add(new UnloggedSpan(Clock(a), Clock(b), (int)Math.Round((b - a).TotalMinutes)));
        }
    }
}
//------------------------------EOF-----------------------------\\
