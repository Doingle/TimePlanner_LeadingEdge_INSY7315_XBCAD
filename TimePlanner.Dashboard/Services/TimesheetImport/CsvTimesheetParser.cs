using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace TimePlanner.Dashboard.Services.TimesheetImport
{
    //-----------------------------
    //reads the timesheet csv format: a header row naming the columns, then one row per worked period.
    //columns (any order, case does not matter): Company, Project, Activity, Start, End are required, Note and Method are optional.
    //times are local wall clock times written yyyy-MM-dd HH:mm, activity is a path such as "Coding > Frontend"
    public static class CsvTimesheetParser
    {
        public static readonly string[] RequiredColumns = { "company", "project", "activity", "start", "end" };
        public static readonly string[] OptionalColumns = { "note", "method" };

        private static readonly string[] TimeFormats =
            { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" };

        //-----------------------------
        //turns the file into rows. A row that cannot be read becomes an error and is left out, the caller decides what to do with the rest
        public static async Task<(List<(int Row, ImportEntry Entry)> Rows, List<ImportRowError> Errors)> ParseAsync(Stream stream, int maxRows)
        {
            var rows = new List<(int, ImportEntry)>();
            var errors = new List<ImportRowError>();

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null
            };
            using var csv = new CsvReader(reader, config);

            try
            {
                if (!await csv.ReadAsync())
                    return (rows, new List<ImportRowError> { new(0, "The file is empty.") });

                csv.ReadHeader();
                var headers = csv.HeaderRecord!.Select(h => h.Trim().ToLowerInvariant()).ToHashSet();

                //a misspelt or missing column would silently drop data, so the header must match exactly
                var missing = RequiredColumns.Where(c => !headers.Contains(c)).ToList();
                var unknown = headers.Where(h => !RequiredColumns.Contains(h) && !OptionalColumns.Contains(h)).ToList();
                if (missing.Count > 0)
                    errors.Add(new ImportRowError(1, "Missing column(s): " + string.Join(", ", missing) + ". Expected: Company, Project, Activity, Start, End, and optionally Note, Method."));
                if (unknown.Count > 0)
                    errors.Add(new ImportRowError(1, "Unknown column(s): " + string.Join(", ", unknown) + ". Expected: Company, Project, Activity, Start, End, and optionally Note, Method."));
                if (errors.Count > 0)
                    return (rows, errors);

                string? Field(string name) => headers.Contains(name) ? csv.GetField(name) : null;

                while (await csv.ReadAsync())
                {
                    var rowNumber = csv.Parser.Row;
                    if (rows.Count + errors.Count >= maxRows)
                    {
                        errors.Add(new ImportRowError(0, $"The file has more than {maxRows} rows."));
                        break;
                    }

                    var start = ParseTime(Field("start"));
                    var end = ParseTime(Field("end"));
                    if (start == null || end == null)
                    {
                        errors.Add(new ImportRowError(rowNumber, "Start and End must look like 2026-09-28 09:30."));
                        continue;
                    }

                    rows.Add((rowNumber, new ImportEntry(Field("company"), Field("project"), Field("activity"), start, end, Field("note"), Field("method"))));
                }
            }
            catch (CsvHelperException)
            {
                //the library message can quote file content, so the user gets a fixed one with the position
                errors.Add(new ImportRowError(csv.Parser.Row, "This is not a valid CSV file (check quotes around text containing commas)."));
            }

            return (rows, errors);
        }

        private static DateTime? ParseTime(string? value) =>
            DateTime.TryParseExact(value, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    }
}
//------------------------------EOF-----------------------------\\
