using System.Globalization;
using TimePlanner.Core.Data;
using TimePlanner.Dashboard.Services.Reports;
using TimePlanner.Dashboard.Services.Submissions;

namespace TimePlanner.Dashboard.Services.Overview
{
    //-----------------------------
    //one worked period inside a day, as the read-only timesheet lists it
    public record TimesheetItem(string Start, string End, double Hours, string Company, string Project, string Activity, string? Note, bool Billable);

    //-----------------------------
    //one calendar day. Status is Submitted, Pending (today, not sent yet), Missing (a working day in the past that was never sent),
    //NotDue (a day still to come) or NonWorkingDay (a weekend nobody logged on)
    public record TimesheetDay(DateTime Date, string Weekday, bool IsWorkingDay, string Status, double Hours, int Entries, DateTimeOffset? SubmittedAt,
        IReadOnlyList<TimesheetItem> Items);

    public record TimesheetView(string View, string Label, DateTime From, DateTime To, double TotalHours, int SubmittedDays, int MissingDays,
        IReadOnlyList<TimesheetDay> Days);

    //-----------------------------
    //a person's week or month laid out day by day. Read-only: a correction is made in the widget and sent again
    public class TimesheetViewService
    {
        public const string Submitted = "Submitted", Pending = "Pending", Missing = "Missing", NotDue = "NotDue", NonWorkingDay = "NonWorkingDay";

        private readonly ReportService _reports;
        private readonly SubmissionService _submissions;
        private readonly CompanyClock _clock;
        private readonly AppDbContext _app;

        public TimesheetViewService(ReportService reports, SubmissionService submissions, CompanyClock clock, AppDbContext app)
        {
            _reports = reports;
            _submissions = submissions;
            _clock = clock;
            _app = app;
        }

        public async Task<TimesheetView> GetAsync(int appUserId, Period period)
        {
            var today = _clock.Today;
            var entries = await _reports.LoadEntriesAsync(period.From, period.To, appUserId, null);
            var activities = await ActivityLookup.LoadAsync(_app);
            var submitted = await _submissions.ForUserAsync(appUserId, period.From, period.To);

            var days = period.Days().Select(day =>
            {
                var items = entries.Where(e => e.Start.Date == day).ToList();
                DateTimeOffset? at = submitted.TryGetValue(day, out var utc) ? _clock.ToCompanyTime(utc) : null;
                return new TimesheetDay(day, day.ToString("ddd", CultureInfo.InvariantCulture), Period.IsWorkingDay(day), StatusOf(day, today, at != null),
                    Math.Round(items.Sum(ReportService.Hours), 2), items.Count, at,
                    items.Select(e => new TimesheetItem(e.Start.ToString("HH:mm", CultureInfo.InvariantCulture), e.End.ToString("HH:mm", CultureInfo.InvariantCulture),
                        Math.Round(ReportService.Hours(e), 2), e.Company, e.Project, activities.Path(e.CategoryId), e.Note, ReportService.IsBillable(e, activities))).ToList());
            }).ToList();

            return new TimesheetView(period.View, period.Label, period.From, period.To, Math.Round(days.Sum(d => d.Hours), 2),
                days.Count(d => d.Status == Submitted), days.Count(d => d.Status == Missing), days);
        }

        //-----------------------------
        //shared with the admin grid so a day means the same thing everywhere
        public static string StatusOf(DateTime day, DateTime today, bool submitted)
        {
            if (submitted)
                return Submitted;
            if (day > today)
                return NotDue;
            if (!Period.IsWorkingDay(day))
                return NonWorkingDay;
            return day == today ? Pending : Missing;
        }
    }
}
//------------------------------EOF-----------------------------\\
