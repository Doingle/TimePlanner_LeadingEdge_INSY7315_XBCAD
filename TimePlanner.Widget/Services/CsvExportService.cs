using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Services
{
    /// <summary>
    /// Writes time entries to a CSV file for the timesheet, in the format the dashboard's CSV upload
    /// reads: names rather than database ids, so a file from any computer can be uploaded, and any
    /// value that starts like a spreadsheet formula is escaped.
    /// </summary>
    public sealed class CsvExportService
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        // The dashboard reads an activity as its levels joined by ">", for example "Coding > Frontend"
        private const string ActivitySeparator = " > ";

        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        public async Task WriteAsync(string path, IEnumerable<TimeEntry> entries)
        {
            var config = new CsvConfiguration(Culture) { InjectionOptions = InjectionOptions.Escape };

            // With a byte order mark, so Excel reads the names correctly
            await using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            await using var csv = new CsvWriter(writer, config);
            await csv.WriteRecordsAsync(entries.Select(ToRow));
        }

        private static TimesheetRow ToRow(TimeEntry entry)
        {
            var task = entry.Task;
            var project = task?.Project;
            return new TimesheetRow(
                project?.Company?.Name ?? string.Empty,
                project?.Name ?? string.Empty,
                task != null ? string.Join(ActivitySeparator, ActivityPath.Of(task)) : string.Empty,
                entry.StartTime.ToString(TimeFormat, Culture),
                entry.EndTime.ToString(TimeFormat, Culture),
                entry.Note ?? string.Empty,
                entry.Method.ToString());
        }

        /// <summary>One line of the file. The property names are the column headers.</summary>
        public sealed record TimesheetRow(string Company, string Project, string Activity, string Start, string End, string Note, string Method);
    }
}
