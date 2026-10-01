using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Services;

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

        public ReportService(AppDbContext db) => _db = db;

        //-----------------------------
        //developers may only see their own time, admin and billing may see anyone's. Returns false when the request asks for someone else's
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

        //an entry with everything the reports need, read in one query
        private record RawEntry(int UserId, string UserName, string Company, string Project, int CategoryId, DateTime Start, DateTime End, string? Note);

        private async Task<List<RawEntry>> LoadAsync(DateTime from, DateTime to, int? userId, int? companyId)
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
                .Select(e => new RawEntry(e.UserId, e.User!.Name, e.Task!.Project!.Company!.Name, e.Task.Project.Name,
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
        //total and billable hours per group. Billable means the top level activity is billable and the work was not for the internal company
        //ponytail: grouped in memory after one query, fine for a few thousand entries (the range is capped at a year). Past that, sum julianday(End)-julianday(Start) in SQL
        public async Task<HoursReport> GetHoursAsync(DateTime from, DateTime to, ReportGrouping groupBy, int? userId, int? companyId)
        {
            var entries = await LoadAsync(from, to, userId, companyId);
            var activities = new ActivityLookup(await _db.Categories.AsNoTracking().ToListAsync());

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
        //one person's entries in the company's timesheet layout, as csv bytes with a byte order mark so Excel reads the characters correctly.
        //text that could be run as a formula is neutralised by CsvHelper's escape option
        public async Task<byte[]> ExportTimesheetCsvAsync(int userId, DateTime from, DateTime to)
        {
            var entries = await LoadAsync(from, to, userId, null);
            var activities = new ActivityLookup(await _db.Categories.AsNoTracking().ToListAsync());

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

        private static double Hours(RawEntry e) => (e.End - e.Start).TotalHours;

        private static double Round(double hours) => Math.Round(hours, 2);

        private static bool IsInternal(RawEntry e) => e.Company == LocalSetupService.InternalCompanyName;

        private static bool IsBillable(RawEntry e, ActivityLookup activities) => !IsInternal(e) && activities.RootIsBillable(e.CategoryId);

        //the company's sheet marks internal work "Internal" and otherwise Yes or No
        private static string BillableLabel(RawEntry e, ActivityLookup activities) =>
            IsInternal(e) ? "Internal" : activities.RootIsBillable(e.CategoryId) ? "Yes" : "No";

        private static string Label(RawEntry e, ReportGrouping groupBy, ActivityLookup activities) => groupBy switch
        {
            ReportGrouping.Company => e.Company,
            ReportGrouping.User => e.UserName,
            ReportGrouping.Day => e.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ReportGrouping.Activity => activities.RootName(e.CategoryId),
            _ => $"{e.Company} / {e.Project}"
        };

        //-----------------------------
        //answers questions about the activity tree from one read of the categories
        private class ActivityLookup
        {
            private readonly Dictionary<int, Category> _byId;

            public ActivityLookup(List<Category> all) => _byId = all.ToDictionary(c => c.CategoryId);

            public string Path(int id)
            {
                var names = new List<string>();
                for (var c = Find(id); c != null; c = c.ParentCategoryId == null ? null : Find(c.ParentCategoryId.Value))
                    names.Insert(0, c.Name);
                return string.Join(" > ", names);
            }

            public string RootName(int id) => Root(id)?.Name ?? "Unknown";

            public bool RootIsBillable(int id) => Root(id)?.IsBillable ?? false;

            private Category? Find(int id) => _byId.GetValueOrDefault(id);

            private Category? Root(int id)
            {
                var c = Find(id);
                while (c?.ParentCategoryId != null)
                    c = Find(c.ParentCategoryId.Value);
                return c;
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
