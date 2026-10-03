using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services.Models;

namespace TimePlanner.Core.Services
{
    //-----------------------------
    //reads and loads a sample day into a users database for testing
    public class SampleDayService
    {
        private readonly TimesheetEditService _edits;
        private readonly ITimeEntryRepository _entries;
        private readonly IDaySessionRepository _sessions;

        public SampleDayService(TimesheetEditService edits, ITimeEntryRepository entries, IDaySessionRepository sessions)
        {
            _edits = edits;
            _entries = entries;
            _sessions = sessions;
        }

        //-----------------------------
        //reads sample day rows from a csv text reader using header names
        public static IReadOnlyList<SampleRow> Read(TextReader reader)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true
            };

            using var csv = new CsvReader(reader, config);

            //if checks whether reading the header record succeeds
            if (!csv.Read() || !csv.ReadHeader())
            {
                throw new FormatException("Line 1: Missing CSV header.");
            }

            var rows = new List<SampleRow>();
            var line = 1;

            //reads each record line by line
            while (csv.Read())
            {
                line++;

                try
                {
                    var task = csv.GetField<string>("Activity/Task") ?? string.Empty;
                    var clientProject = csv.GetField<string>("Client / Project") ?? string.Empty;
                    var startText = csv.GetField<string>("Start Time") ?? string.Empty;
                    var endText = csv.GetField<string>("End Time") ?? string.Empty;
                    var hoursText = csv.GetField<string>("Duration (hours)") ?? string.Empty;
                    var notes = csv.GetField<string>("Notes") ?? string.Empty;
                    var billable = csv.GetField<string>("Billable") ?? string.Empty;

                    //parses start time from string
                    if (!TimeOnly.TryParse(startText, CultureInfo.InvariantCulture, out var start))
                    {
                        throw new FormatException($"Line {line}: Invalid start time '{startText}'.");
                    }

                    //parses end time from string
                    if (!TimeOnly.TryParse(endText, CultureInfo.InvariantCulture, out var end))
                    {
                        throw new FormatException($"Line {line}: Invalid end time '{endText}'.");
                    }

                    //parses duration hours from string
                    if (!double.TryParse(hoursText, NumberStyles.Any, CultureInfo.InvariantCulture, out var hours))
                    {
                        throw new FormatException($"Line {line}: Invalid duration '{hoursText}'.");
                    }

                    rows.Add(new SampleRow(line, task, clientProject, start, end, hours, notes, billable));
                }
                catch (CsvHelperException ex)
                {
                    throw new FormatException($"Line {line}: {ex.Message}", ex);
                }
            }

            return rows;
        }

        //-----------------------------
        //splits client project text into company and project names
        public static (string Company, string Project) SplitClientProject(string value)
        {
            var parts = value.Split(new[] { " / " }, 2, StringSplitOptions.None);

            //if checks whether the text contains a slash separator
            if (parts.Length == 2)
            {
                return (parts[0].Trim(), parts[1].Trim());
            }

            var company = value.Trim();

            //if checks whether the company is the internal company
            if (company == LocalSetupService.InternalCompanyName)
            {
                return (company, LocalSetupService.InternalProjectName);
            }

            return (company, company);
        }

        //-----------------------------
        //calculates the most recent weekday before the current date
        public static DateOnly TargetDay(DateTime now)
        {
            var day = DateOnly.FromDateTime(now).AddDays(-1);

            //loops backward past weekend days to find the previous weekday
            while (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday)
            {
                day = day.AddDays(-1);
            }

            return day;
        }

        //-----------------------------
        //loads the sample day csv into the database when no entries exist on that day
        public async Task<SampleDayResult> LoadAsync(int userId, string path, DateTime now)
        {
            //if checks whether the sample file exists on disk
            if (!File.Exists(path))
            {
                return new SampleDayResult(false, default, 0, "The sample day file was not found.");
            }

            using var reader = new StreamReader(path);
            var rows = Read(reader);
            var day = TargetDay(now);

            var from = day.ToDateTime(TimeOnly.MinValue);
            var to = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var existing = await _entries.GetForUserAsync(userId, from, to);

            //if checks whether the target day already has entries
            if (existing.Count > 0)
            {
                return new SampleDayResult(false, day, 0, $"{day.ToString("dddd d MMM", CultureInfo.InvariantCulture)} already has entries so the sample was not loaded.");
            }

            var added = 0;

            //adds each sample row as a timesheet entry
            foreach (var row in rows)
            {
                var (company, project) = SplitClientProject(row.ClientProject);
                var start = day.ToDateTime(row.Start);
                var end = day.ToDateTime(row.End);
                var projectPath = new[] { company, project };
                var activityPath = row.ActivityPath.Split(new[] { " > " }, StringSplitOptions.None).Select(a => a.Trim()).ToArray();
                var note = string.Equals(row.Task, row.ActivityPath, StringComparison.Ordinal) ? null : row.Task;

                var edit = new EntryEdit(start, end, projectPath, activityPath, note);
                var result = await _edits.AddEntryAsync(userId, edit, now);

                //earlier rows stay when a line fails which is acceptable for a test tool
                if (!result.Ok)
                {
                    return new SampleDayResult(false, day, added, $"Line {row.Line}: {result.Error}");
                }

                added++;
            }

            var ordered = rows.OrderBy(r => r.Start).ToList();
            var firstStart = day.ToDateTime(ordered[0].Start);
            var lastEnd = day.ToDateTime(ordered[^1].End);

            var session = new DaySession
            {
                UserId = userId,
                StartedAt = firstStart,
                EndedAt = lastEnd
            };

            //gaps of thirty minutes or more between entries become session pauses
            for (var i = 0; i < ordered.Count - 1; i++)
            {
                var gapStart = day.ToDateTime(ordered[i].End);
                var gapEnd = day.ToDateTime(ordered[i + 1].Start);

                //if checks whether the gap duration is thirty minutes or more
                if ((gapEnd - gapStart).TotalMinutes >= 30)
                {
                    session.Pauses.Add(new SessionPause { StartedAt = gapStart, EndedAt = gapEnd });
                }
            }

            await _sessions.AddAsync(session);

            return new SampleDayResult(true, day, rows.Count, $"Sample day loaded for {day.ToString("dddd d MMM", CultureInfo.InvariantCulture)} with {rows.Count} entries. Open Timesheet to review and send it.");
        }
    }
}
//------------------------------EOF-----------------------------\\
