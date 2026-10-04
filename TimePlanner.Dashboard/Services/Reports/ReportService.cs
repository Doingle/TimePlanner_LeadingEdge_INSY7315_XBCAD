using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Services;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Dashboard.Services.Reports
{
    //-----------------------------
    //what a report can be grouped by
    public enum ReportGrouping
    {
        Project,
        Company,
        User,
        Day,
        Activity
    }

    //-----------------------------
    //one line of a report, Label is what the rows are grouped by
    public record ReportRow(string Label, double Hours, double BillableHours, int Entries);

    public record HoursReport(DateTime From, DateTime To, string GroupBy, IReadOnlyList<ReportRow> Rows, double TotalHours, double BillableHours, int Entries);

    //-----------------------------
    //builds the hours reports and the timesheet export in the company's own spreadsheet layout.
    //the service never decides who may see what, callers pass in the user to restrict to and use TryScope to work that out
    public class ReportService
    {
        public const int MaxRangeDays = 366;

        //the export follows the layout of the timesheet the company already keeps by hand
        public static readonly string[] TimesheetColumns =
            { "Date", "Activity/Task", "Client / Project", "Start Time", "End Time", "Duration (hours)", "Notes", "Billable" };

        private readonly AppDbContext _db;
        private readonly AuditLogger _audit;

        public ReportService(AppDbContext db, AuditLogger audit)
        {
            _db = db;
            _audit = audit;
        }

        //-----------------------------
        //developers may only see their own time, an admin may see anyone's. Returns false when the request asks for someone else's
        //and sets filter to the user to restrict the query to, null meaning everyone
        public static bool TryScope(int? requestedUserId, int callerUserId, bool privileged, out int? filter)
        {
            if (privileged)
            {
                filter = requestedUserId;
                return true;
            }

            filter = callerUserId;
            return requestedUserId == null || requestedUserId == callerUserId;
        }

        //-----------------------------
        //checks a from/to pair, null when it is fine. Both days are included in the report
        public static string? ValidateRange(DateTime? from, DateTime? to)
        {
            if (from == null || to == null)
                return "from and to are required.";
            if (from > to)
                return "'from' must not be after 'to'.";
            if ((to.Value.Date - from.Value.Date).TotalDays >= MaxRangeDays)
                return $"The range may not be longer than {MaxRangeDays} days.";
            return null;
        }

        //-----------------------------
        //a time entry with everything the screens and reports need, read in one query
        public record ReportEntry(int UserId, string UserName, string Company, string Project, int CategoryId, DateTime Start, DateTime End, string? Note);

        //-----------------------------
        //the entries starting on any of the days from..to (both included), oldest first, optionally for one person and/or one company
        public async Task<List<ReportEntry>> LoadEntriesAsync(DateTime from, DateTime to, int? userId, int? companyId)
        {
            //whole days: from the start of the first day to the end of the last
            var start = from.Date;
            var end = to.Date.AddDays(1);

            var query = _db.TimeEntries.AsNoTracking().Where(e => e.StartTime >= start && e.StartTime < end);
            if (userId != null)
                query = query.Where(e => e.UserId == userId);
            if (companyId != null)
                query = query.Where(e => e.Task!.Project!.CompanyId == companyId);

            return await query
                .OrderBy(e => e.StartTime)
                .Select(e => new ReportEntry(e.UserId, e.User!.Name, e.Task!.Project!.Company!.Name, e.Task.Project.Name,
                    e.Task.CategoryId, e.StartTime, e.EndTime, e.Note))
                .ToListAsync();
        }

        //-----------------------------
        //people a privileged user can pick in the report filter
        public async Task<List<(int Id, string Name)>> GetUserOptionsAsync() =>
            (await _db.Users.AsNoTracking().OrderBy(u => u.Name).Select(u => new { u.UserId, u.Name }).ToListAsync())
                .Select(u => (u.UserId, u.Name))
                .ToList();

        //-----------------------------
        //total and billable hours per group. Billable means the work was for a company other than the internal one
        //ponytail: grouped in memory after one query, fine for a few thousand entries (the range is capped at a year). Past that, sum julianday(End)-julianday(Start) in SQL
        public async Task<HoursReport> GetHoursAsync(DateTime from, DateTime to, ReportGrouping groupBy, int? userId, int? companyId)
        {
            var entries = await LoadEntriesAsync(from, to, userId, companyId);
            var activities = await ActivityLookup.LoadAsync(_db);

            var rows = entries
                .GroupBy(e => Label(e, groupBy, activities))
                .Select(g => new ReportRow(
                    g.Key,
                    Round(g.Sum(Hours)),
                    Round(g.Where(e => IsBillable(e, activities)).Sum(Hours)),
                    g.Count()))
                .ToList();

            //a day report reads as a timeline, the others by size
            rows = groupBy == ReportGrouping.Day
                ? rows.OrderBy(r => r.Label, StringComparer.Ordinal).ToList()
                : rows.OrderByDescending(r => r.Hours).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();

            return new HoursReport(from.Date, to.Date, groupBy.ToString(), rows,
                Round(entries.Sum(Hours)), Round(entries.Where(e => IsBillable(e, activities)).Sum(Hours)), entries.Count);
        }

        //-----------------------------
        //total, billable and non-billable time plus the breakdowns by category, project and person, for one person (userId) or everyone (null).
        //category means the top level activity, so sub activities add up under their parent
        public async Task<ReportSummary> GetSummaryAsync(DateTime from, DateTime to, int? userId)
        {
            var entries = await LoadEntriesAsync(from, to, userId, null);
            var activities = await ActivityLookup.LoadAsync(_db);
            return Summarise(from, to, entries, activities);
        }

        //-----------------------------
        //builds a summary from entries already loaded, so a screen that needs several views of the same period reads the database once
        public static ReportSummary Summarise(DateTime from, DateTime to, IReadOnlyList<ReportEntry> entries, ActivityLookup activities)
        {
            var totalMinutes = entries.Sum(e => (e.End - e.Start).TotalMinutes);
            var billableMinutes = entries.Where(e => IsBillable(e, activities)).Sum(e => (e.End - e.Start).TotalMinutes);

            return new ReportSummary(from.Date, to.Date, Round(totalMinutes / 60), (int)Math.Round(totalMinutes),
                Round(billableMinutes / 60), Round((totalMinutes - billableMinutes) / 60), entries.Count,
                Breakdown(entries, totalMinutes, e => activities.RootName(e.CategoryId), (_, e) => (activities.RootColour(e.CategoryId), null)),
                Breakdown(entries, totalMinutes, e => $"{e.Company} / {e.Project}", (_, e) => (null, IsBillable(e, activities))),
                Breakdown(entries, totalMinutes, e => e.UserName, (_, _) => (null, null), personKey: e => e.UserId));
        }

        //one row per group, largest first. The first entry of a group decides its colour or billable flag, which are the same for every entry in it
        private static List<BreakdownRow> Breakdown(IReadOnlyList<ReportEntry> entries, double totalMinutes, Func<ReportEntry, string> label,
            Func<string, ReportEntry, (string? Colour, bool? Billable)> style, Func<ReportEntry, int>? personKey = null) =>
            entries
                .GroupBy(e => personKey == null ? (object)label(e) : personKey(e))
                .Select(g =>
                {
                    var first = g.First();
                    var minutes = g.Sum(e => (e.End - e.Start).TotalMinutes);
                    var (colour, billable) = style(label(first), first);
                    return new BreakdownRow(label(first), Round(minutes / 60), (int)Math.Round(minutes),
                        totalMinutes <= 0 ? 0 : Math.Round(minutes / totalMinutes * 100, 1), colour, billable, g.Count());
                })
                .OrderByDescending(r => r.Minutes)
                .ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

        //-----------------------------
        //one person's entries in the company's timesheet layout, as csv bytes with a byte order mark so Excel reads the characters correctly.
        //text that could be run as a formula is neutralised by CsvHelper's escape option
        public async Task<byte[]> ExportTimesheetCsvAsync(int userId, DateTime from, DateTime to)
        {
            var entries = await LoadEntriesAsync(from, to, userId, null);
            var activities = await ActivityLookup.LoadAsync(_db);
            await _audit.LogAsync(AuditActions.TimesheetExported, $"profile {userId}, {from:yyyy-MM-dd} to {to:yyyy-MM-dd}, {entries.Count} entries");

            using var memory = new MemoryStream();
            await using (var writer = new StreamWriter(memory, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
            await using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { InjectionOptions = InjectionOptions.Escape }))
            {
                foreach (var column in TimesheetColumns)
                    csv.WriteField(column);
                await csv.NextRecordAsync();

                foreach (var e in entries)
                {
                    var path = activities.Path(e.CategoryId);
                    csv.WriteField(e.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    csv.WriteField(string.IsNullOrWhiteSpace(e.Note) ? path : e.Note);
                    csv.WriteField(e.Project == e.Company || e.Project == LocalSetupService.InternalProjectName ? e.Company : $"{e.Company} / {e.Project}");
                    csv.WriteField(e.Start.ToString("HH:mm", CultureInfo.InvariantCulture));
                    csv.WriteField(e.End.ToString("HH:mm", CultureInfo.InvariantCulture));
                    csv.WriteField(Hours(e).ToString("0.00", CultureInfo.InvariantCulture));
                    csv.WriteField(path);
                    csv.WriteField(BillableLabel(e, activities));
                    await csv.NextRecordAsync();
                }
            }

            return memory.ToArray();
        }

        //-----------------------------
        //gets a person's display name or email
        public async Task<string> PersonNameAsync(int appUserId)
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == appUserId);
            return user?.Name ?? user?.Email ?? "Timesheet";
        }

        //-----------------------------
        //one person's entries in the company's timesheet layout as an excel workbook
        public async Task<byte[]> ExportTimesheetXlsxAsync(int userId, DateTime from, DateTime to)
        {
            var entries = await LoadEntriesAsync(from, to, userId, null);
            var activities = await ActivityLookup.LoadAsync(_db);
            var personName = await PersonNameAsync(userId);
            await _audit.LogAsync(AuditActions.TimesheetExported, $"profile {userId}, {from:yyyy-MM-dd} to {to:yyyy-MM-dd}, {entries.Count} entries, xlsx");

            return TimesheetWorkbook.Build(personName, from, to, entries, activities);
        }

        public static double Hours(ReportEntry e) => (e.End - e.Start).TotalHours;

        private static double Round(double hours) => Math.Round(hours, 2);

        public static bool IsInternal(ReportEntry e) => BillingRules.IsInternal(e.Company);

        //billable unless internal work or a non billable activity
        public static bool IsBillable(ReportEntry e, ActivityLookup activities) => BillingRules.IsBillable(e.Company, activities.RootIsBillable(e.CategoryId));

        //client and project display text
        internal static string ClientProjectText(ReportEntry e) => e.Project == e.Company || e.Project == LocalSetupService.InternalProjectName ? e.Company : $"{e.Company} / {e.Project}";

        //the company's own sheet writes "Internal" for the work that is not billed
        internal static string BillableLabel(ReportEntry e, ActivityLookup activities) => IsInternal(e) ? "Internal" : IsBillable(e, activities) ? "Yes" : "No";

        private static string Label(ReportEntry e, ReportGrouping groupBy, ActivityLookup activities) => groupBy switch
        {
            ReportGrouping.Company => e.Company,
            ReportGrouping.User => e.UserName,
            ReportGrouping.Day => e.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ReportGrouping.Activity => activities.RootName(e.CategoryId),
            _ => $"{e.Company} / {e.Project}"
        };
    }
}
//------------------------------EOF-----------------------------\\
