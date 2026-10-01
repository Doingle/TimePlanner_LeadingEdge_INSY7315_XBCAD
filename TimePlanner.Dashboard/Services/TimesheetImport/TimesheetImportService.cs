using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Services;

namespace TimePlanner.Dashboard.Services.TimesheetImport
{
    //-----------------------------
    //the one place worked time enters the server, used by the json endpoint, the csv endpoint and the dashboard upload page.
    //every row is checked before anything is written, then everything is saved in one transaction: either the whole file is imported or none of it.
    //importing the same data twice is safe because an entry that already exists is skipped
    public class TimesheetImportService
    {
        public const int MaxRows = 5000;
        public const int MaxFileBytes = 1_000_000;
        public const int MaxNameLength = 100;

        public const string TemplateCsv =
            "Company,Project,Activity,Start,End,Note,Method\r\n" +
            "Acme Ltd,Website Redesign,Coding > Frontend,2026-09-28 09:00,2026-09-28 10:30,Built the login page,Manual\r\n";

        private static readonly DateTime EarliestStart = new(2020, 1, 1);
        private static readonly TimeSpan MaxEntryLength = TimeSpan.FromHours(24);

        private readonly AppDbContext _db;
        private readonly EntryService _entries;
        private readonly ActivityService _activities;
        private readonly ICategoryRepository _categories;
        private readonly IProjectRepository _projects;
        private readonly ITimeEntryRepository _timeEntries;
        private readonly TimeEntryFactory _factory;
        private readonly AuditLogger _audit;

        public TimesheetImportService(AppDbContext db, EntryService entries, ActivityService activities, ICategoryRepository categories,
            IProjectRepository projects, ITimeEntryRepository timeEntries, TimeEntryFactory factory, AuditLogger audit)
        {
            _audit = audit;
            _db = db;
            _entries = entries;
            _activities = activities;
            _categories = categories;
            _projects = projects;
            _timeEntries = timeEntries;
            _factory = factory;
        }

        //a row that passed every check
        private record ValidRow(int Row, string Company, string Project, IReadOnlyList<string> Path, DateTime Start, DateTime End, string? Note, EntryMethod Method);

        //-----------------------------
        //checks the upload itself (name, size) then imports the rows it contains
        public async Task<ImportResult> ImportCsvAsync(int userId, Stream stream, long length, string? fileName)
        {
            if (length == 0)
                return ImportResult.Failed(0, "The file is empty.");
            if (length > MaxFileBytes)
                return ImportResult.Failed(0, $"The file is larger than {MaxFileBytes / 1_000_000} MB.");
            if (!string.Equals(Path.GetExtension(fileName), ".csv", StringComparison.OrdinalIgnoreCase))
                return ImportResult.Failed(0, "Upload a .csv file.");

            var (rows, parseErrors) = await CsvTimesheetParser.ParseAsync(stream, MaxRows);

            //nothing readable means the parse errors already explain why, a second "no entries" message would only bury them
            if (rows.Count == 0 && parseErrors.Count > 0)
                return new ImportResult(0, 0, parseErrors);

            var result = await ImportAsync(userId, rows);

            //problems found while reading the file are reported together with the ones found while checking it
            return parseErrors.Count == 0 ? result : new ImportResult(0, 0, parseErrors.Concat(result.Errors).OrderBy(e => e.Row).ToList());
        }

        //-----------------------------
        //validates every row, and only if all are fine stores them for the user
        public async Task<ImportResult> ImportAsync(int userId, IReadOnlyList<(int Row, ImportEntry Entry)> rows)
        {
            if (rows.Count == 0)
                return ImportResult.Failed(0, "The file contains no entries.");
            if (rows.Count > MaxRows)
                return ImportResult.Failed(0, $"The file has more than {MaxRows} rows.");

            var (valid, errors) = await ValidateAsync(rows);
            if (errors.Count > 0)
            {
                await _audit.LogAsync(AuditActions.TimesheetImportRejected, $"profile {userId}, {errors.Count} problem(s) in {rows.Count} row(s)");
                return new ImportResult(0, 0, errors);
            }

            var stored = await StoreAsync(userId, valid);
            await _audit.LogAsync(AuditActions.TimesheetImported, $"profile {userId}, created {stored.Created}, skipped {stored.Skipped}");
            return stored;
        }

        //-----------------------------
        //every check that needs no writes, errors are collected for all rows so the user can fix a file in one go
        private async Task<(List<ValidRow> Valid, List<ImportRowError> Errors)> ValidateAsync(IReadOnlyList<(int Row, ImportEntry Entry)> rows)
        {
            var errors = new List<ImportRowError>();
            var valid = new List<ValidRow>();

            var categories = await _categories.GetAllAsync();
            var roots = categories.Where(c => c.ParentCategoryId == null).OrderBy(c => c.SortOrder).ToList();
            var closedProjects = (await _projects.GetAllAsync())
                .Where(p => p.Status != ProjectStatus.Active)
                .Select(p => (Company: p.Company!.Name.ToLowerInvariant(), Project: p.Name.ToLowerInvariant()))
                .ToHashSet();
            var latestStart = DateTime.Now.AddDays(1);

            foreach (var (row, entry) in rows)
            {
                var before = errors.Count;
                void Fail(string message) => errors.Add(new ImportRowError(row, message));

                var company = CleanName(entry.Company, "Company", Fail);
                var project = CleanName(entry.Project, "Project", Fail);

                //activity is a path of at most three names, and its top level must be one of the existing categories
                var path = (entry.Activity ?? string.Empty).Split('>').Select(s => s.Trim()).ToList();
                if (string.IsNullOrWhiteSpace(entry.Activity) || path.Any(s => s.Length == 0))
                    Fail("Activity is required, for example Coding or Coding > Frontend.");
                else if (path.Count > ActivityService.MaxDepth)
                    Fail($"Activity can only be {ActivityService.MaxDepth} levels deep.");
                else if (path.Any(s => s.Length > ActivityService.MaxNameLength || HasUnsafeStart(s) || s.Any(char.IsControl)))
                    Fail($"Activity names must be 1 to {ActivityService.MaxNameLength} characters and cannot start with = + - or @.");
                else if (!roots.Any(r => string.Equals(r.Name, path[0], StringComparison.OrdinalIgnoreCase)))
                    Fail($"Unknown activity '{Shorten(path[0])}'. The top level must be one of: {string.Join(", ", roots.Select(r => r.Name))}.");

                if (entry.Start is not DateTime start || entry.End is not DateTime end)
                {
                    Fail("Start and End are required.");
                }
                else if (start.Kind != DateTimeKind.Unspecified || end.Kind != DateTimeKind.Unspecified)
                {
                    Fail("Times must be local times without a time zone, for example 2026-09-28T09:30:00.");
                }
                else if (end <= start)
                {
                    Fail("End must be after Start.");
                }
                else if (end - start < EntryService.MinimumPiece || end - start > MaxEntryLength)
                {
                    Fail("An entry must be between 1 minute and 24 hours long.");
                }
                else if (start < EarliestStart || end > latestStart)
                {
                    Fail("Times must be after 2020 and cannot be in the future.");
                }

                var note = string.IsNullOrWhiteSpace(entry.Note) ? null : entry.Note.Trim();
                if (note?.Length > TimeEntryFactory.MaxNoteLength)
                    Fail($"Note is longer than {TimeEntryFactory.MaxNoteLength} characters.");

                var method = EntryMethod.Manual;
                if (!string.IsNullOrWhiteSpace(entry.Method)
                    && (int.TryParse(entry.Method, out _) || !Enum.TryParse(entry.Method.Trim(), ignoreCase: true, out method) || !Enum.IsDefined(method)))
                    Fail("Method must be Manual, AutoPrompted or AutoTracked.");

                if (company != null && project != null && closedProjects.Contains((company.ToLowerInvariant(), project.ToLowerInvariant())))
                    Fail($"Project '{Shorten(project)}' is closed and no longer accepts time.");

                if (errors.Count == before)
                    valid.Add(new ValidRow(row, company!, project!, path, entry.Start!.Value, entry.End!.Value, note, method));
            }

            //two entries for the same person cannot cover the same minute
            var previous = (ValidRow?)null;
            foreach (var current in valid.OrderBy(v => v.Start).ThenBy(v => v.End))
            {
                if (previous != null && current.Start < previous.End)
                    errors.Add(new ImportRowError(current.Row, $"This entry overlaps the entry on row {previous.Row}."));
                if (previous == null || current.End > previous.End)
                    previous = current;
            }

            return (valid, errors.OrderBy(e => e.Row).ToList());
        }

        //-----------------------------
        //saves the rows. Companies, projects, activities and tasks are created when new, existing entries are skipped, all in one transaction
        private async Task<ImportResult> StoreAsync(int userId, List<ValidRow> valid)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();

            var projects = new Dictionary<string, Project>();
            var activityIds = new Dictionary<string, int>();
            var taskIds = new Dictionary<(int Project, int Category), int>();
            var planned = new List<(int TaskId, ValidRow Row)>();

            foreach (var row in valid)
            {
                var projectKey = (row.Company + "\n" + row.Project).ToLowerInvariant();
                if (!projects.TryGetValue(projectKey, out var project))
                    projects[projectKey] = project = await _entries.AddProjectAsync(row.Company, row.Project, null);

                var activityKey = string.Join(">", row.Path).ToLowerInvariant();
                if (!activityIds.TryGetValue(activityKey, out var categoryId))
                    activityIds[activityKey] = categoryId = await ResolveActivityAsync(row.Path);

                if (!taskIds.TryGetValue((project.ProjectID, categoryId), out var taskId))
                    taskIds[(project.ProjectID, categoryId)] = taskId = (await _entries.FindOrCreateTaskAsync(userId, project.ProjectID, categoryId)).TaskID;

                planned.Add((taskId, row));
            }

            //an entry that is already stored for the same task and time is skipped, which makes re-sending harmless
            var existing = (await _timeEntries.GetForUserAsync(userId, valid.Min(v => v.Start), valid.Max(v => v.End)))
                .Select(e => (e.TaskId, e.StartTime, e.EndTime))
                .ToHashSet();

            var created = planned
                .Where(p => !existing.Contains((p.TaskId, p.Row.Start, p.Row.End)))
                .Select(p => Build(userId, p.TaskId, p.Row))
                .ToList();

            if (created.Count > 0)
                await _timeEntries.AddRangeAsync(created);

            await transaction.CommitAsync();
            return new ImportResult(created.Count, planned.Count - created.Count, Array.Empty<ImportRowError>());
        }

        //-----------------------------
        //the top level exists (validated), deeper levels are created like the widget does when a user adds a sub activity
        private async Task<int> ResolveActivityAsync(IReadOnlyList<string> path)
        {
            var id = await _activities.FindByPathAsync(new[] { path[0] })
                ?? throw new InvalidOperationException("Top level activity disappeared during the import.");

            foreach (var name in path.Skip(1))
                id = await _activities.AddActivityAsync(id, name);
            return id;
        }

        private TimeEntry Build(int userId, int taskId, ValidRow row) => row.Method switch
        {
            EntryMethod.AutoPrompted => _factory.CreateAutoPrompted(userId, taskId, row.Start, row.End, row.Note),
            EntryMethod.AutoTracked => _factory.CreateAutoTracked(userId, taskId, row.Start, row.End),
            _ => _factory.CreateManual(userId, taskId, row.Start, row.End, row.Note)
        };

        //-----------------------------
        //company and project names are stored and later shown and exported, so control characters and a leading formula character are refused
        private static string? CleanName(string? value, string field, Action<string> fail)
        {
            var clean = value?.Trim() ?? string.Empty;
            if (clean.Length == 0 || clean.Length > MaxNameLength || clean.Any(char.IsControl) || HasUnsafeStart(clean))
            {
                fail($"{field} must be 1 to {MaxNameLength} characters and cannot start with = + - or @.");
                return null;
            }
            return clean;
        }

        private static bool HasUnsafeStart(string value) => value.Length > 0 && "=+-@".Contains(value[0]);

        //keeps echoed user input short in error messages
        private static string Shorten(string value) => value.Length <= 40 ? value : value[..40] + "...";
    }
}
//------------------------------EOF-----------------------------\\
