using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Services.Reports;
using TimePlanner.Dashboard.Services.Submissions;

namespace TimePlanner.Dashboard.Services.Overview
{
    //-----------------------------
    //Submitted people over people expected, for today or for a period. Percent is null when nobody was expected (a weekend)
    public record SubmissionRate(int Submitted, int Expected, double? Percent);

    public record AdminOverview(string View, string Label, DateTime From, DateTime To, ReportSummary Summary, SubmissionRate Today, SubmissionRate Period);

    public record GridCell(DateTime Date, string Status, DateTimeOffset? SubmittedAt);

    public record GridRow(int AppUserId, string Name, string Email, IReadOnlyList<GridCell> Cells, int Submitted, int Missing);

    //-----------------------------
    //every person against every working day of the period, marked submitted, pending or missing. A record only, there is nothing to approve or chase
    public record SubmissionGrid(string View, string Label, DateTime From, DateTime To, IReadOnlyList<DateTime> Days, IReadOnlyList<GridRow> Rows, SubmissionRate Period);

    public record ExportFile(byte[] Bytes, string ContentType, string FileName);

    //-----------------------------
    //the team view for administrators: overview numbers, the submissions grid and the exports. Callers must already have checked the caller is an admin.
    //the people included are the active developers, plus anyone else who submitted during the period
    public class AdminService
    {
        private readonly AuthDbContext _auth;
        private readonly AppDbContext _app;
        private readonly ReportService _reports;
        private readonly SubmissionService _submissions;
        private readonly CompanyClock _clock;

        public AdminService(AuthDbContext auth, AppDbContext app, ReportService reports, SubmissionService submissions, CompanyClock clock)
        {
            _auth = auth;
            _app = app;
            _reports = reports;
            _submissions = submissions;
            _clock = clock;
        }

        private record Member(int AppUserId, string Name, string Email, string Role, bool IsActive);

        public async Task<AdminOverview> OverviewAsync(Period period)
        {
            var summary = await _reports.GetSummaryAsync(period.From, period.To, null);
            var (members, submissions) = await RosterAsync(period);
            var today = _clock.Today;

            return new AdminOverview(period.View, period.Label, period.From, period.To, summary, TodayRate(members, submissions, today), PeriodRate(members, submissions, period, today));
        }

        public async Task<SubmissionGrid> GridAsync(Period period)
        {
            var (members, submissions) = await RosterAsync(period);
            var today = _clock.Today;
            var days = period.Days().Where(Period.IsWorkingDay).ToList();

            var rows = members.Select(m =>
            {
                var cells = days.Select(day =>
                {
                    var hit = submissions.FirstOrDefault(s => s.AppUserId == m.AppUserId && s.Date == day);
                    return new GridCell(day, TimesheetViewService.StatusOf(day, today, hit != null), hit == null ? null : _clock.ToCompanyTime(hit.SubmittedAtUtc));
                }).ToList();
                return new GridRow(m.AppUserId, m.Name, m.Email, cells, cells.Count(c => c.Status == TimesheetViewService.Submitted), cells.Count(c => c.Status == TimesheetViewService.Missing));
            }).ToList();

            return new SubmissionGrid(period.View, period.Label, period.From, period.To, days, rows, PeriodRate(members, submissions, period, today));
        }

        //-----------------------------
        //one person's timesheet as an excel or csv file, or with no person chosen a zip with one file per person who logged time in the period.
        //null when the person does not exist or nothing was logged in the period
        public async Task<ExportFile?> ExportAsync(Period period, int? appUserId, string format = "xlsx")
        {
            var isCsv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
            var ext = isCsv ? "csv" : "xlsx";
            var mime = isCsv ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            if (appUserId != null)
            {
                var profile = await _app.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == appUserId);
                if (profile == null)
                    return null;

                var singleBytes = isCsv
                    ? await _reports.ExportTimesheetCsvAsync(appUserId.Value, period.From, period.To)
                    : await _reports.ExportTimesheetXlsxAsync(appUserId.Value, period.From, period.To);

                return new ExportFile(singleBytes, mime, $"timesheet_{Slug(profile.Name)}_{period.From:yyyyMMdd}_{period.To:yyyyMMdd}.{ext}");
            }

            var people = (await _reports.LoadEntriesAsync(period.From, period.To, null, null))
                .GroupBy(e => e.UserId)
                .Select(g => (Id: g.Key, Name: g.First().UserName))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (people.Count == 0)
                return null;

            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var person in people)
                {
                    //the id is part of the name, so two people called the same never overwrite each other
                    var file = zip.CreateEntry($"{Slug(person.Name)}-{person.Id}_{period.From:yyyyMMdd}_{period.To:yyyyMMdd}.{ext}", CompressionLevel.Optimal);
                    await using var stream = file.Open();
                    var bytes = isCsv
                        ? await _reports.ExportTimesheetCsvAsync(person.Id, period.From, period.To)
                        : await _reports.ExportTimesheetXlsxAsync(person.Id, period.From, period.To);
                    await stream.WriteAsync(bytes);
                }
            }
            return new ExportFile(memory.ToArray(), "application/zip", $"timesheets_{period.From:yyyyMMdd}_{period.To:yyyyMMdd}.zip");
        }

        //-----------------------------
        //lowercase letters and digits only, so a person's name can never inject a path or odd characters into a file name
        public static string Slug(string name)
        {
            var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return slug.Length == 0 ? "person" : slug[..Math.Min(slug.Length, 40)];
        }

        //-----------------------------
        //who is expected to submit in the period, and every submission in it
        private async Task<(List<Member> Members, List<DaySubmission> Submissions)> RosterAsync(Period period)
        {
            var submissions = await _submissions.InPeriodAsync(period.From, period.To);

            var accounts = await _auth.Users.AsNoTracking().Where(u => u.AppUserId != null)
                .Select(u => new { u.Id, AppUserId = u.AppUserId!.Value, u.Email, u.IsActive }).ToListAsync();
            var roles = await (from ur in _auth.UserRoles join r in _auth.Roles on ur.RoleId equals r.Id select new { ur.UserId, r.Name }).ToListAsync();
            var names = (await _app.Users.AsNoTracking().Select(u => new { u.UserId, u.Name }).ToListAsync()).ToDictionary(u => u.UserId, u => u.Name);
            var submitters = submissions.Select(s => s.AppUserId).ToHashSet();

            var members = accounts
                .Select(a => new Member(a.AppUserId, names.GetValueOrDefault(a.AppUserId, a.Email!), a.Email ?? "", roles.FirstOrDefault(r => r.UserId == a.Id)?.Name ?? "", a.IsActive))
                .Where(m => (m.IsActive && m.Role == nameof(UserRole.Developer)) || submitters.Contains(m.AppUserId))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return (members, submissions);
        }

        private static SubmissionRate TodayRate(List<Member> members, List<DaySubmission> submissions, DateTime today)
        {
            if (!Period.IsWorkingDay(today))
                return new SubmissionRate(0, 0, null);
            var done = members.Count(m => submissions.Any(s => s.AppUserId == m.AppUserId && s.Date == today));
            return Rate(done, members.Count);
        }

        //every member against every working day of the period up to and including today
        private static SubmissionRate PeriodRate(List<Member> members, List<DaySubmission> submissions, Period period, DateTime today)
        {
            var due = period.Days().Where(d => Period.IsWorkingDay(d) && d <= today).ToList();
            var done = members.Sum(m => due.Count(d => submissions.Any(s => s.AppUserId == m.AppUserId && s.Date == d)));
            return Rate(done, members.Count * due.Count);
        }

        private static SubmissionRate Rate(int submitted, int expected) =>
            new(submitted, expected, expected == 0 ? null : Math.Round(submitted * 100.0 / expected, 1));
    }
}
//------------------------------EOF-----------------------------\\
