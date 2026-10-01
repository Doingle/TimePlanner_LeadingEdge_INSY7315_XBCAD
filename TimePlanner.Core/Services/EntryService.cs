using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services.Models;

namespace TimePlanner.Core.Services
{
    //-----------------------------
    //logs entries and manages projects for the widget
    public class EntryService
    {
        //shortest piece of work saved as an entry
        public static readonly TimeSpan MinimumPiece = TimeSpan.FromMinutes(1);

        private readonly IDaySessionRepository _sessions;
        private readonly ITimeEntryRepository _entries;
        private readonly IWorkTaskRepository _tasks;
        private readonly IProjectRepository _projects;
        private readonly ICompanyRepository _companies;
        private readonly ICategoryRepository _categories;
        private readonly TimeEntryFactory _factory;
        private readonly ActivityService _activities;

        public EntryService(
            IDaySessionRepository sessions,
            ITimeEntryRepository entries,
            IWorkTaskRepository tasks,
            IProjectRepository projects,
            ICompanyRepository companies,
            ICategoryRepository categories,
            TimeEntryFactory factory,
            ActivityService activities)
        {
            _sessions = sessions;
            _entries = entries;
            _tasks = tasks;
            _projects = projects;
            _companies = companies;
            _categories = categories;
            _factory = factory;
            _activities = activities;
        }

        //-----------------------------
        //active projects for the project picker
        public Task<List<Project>> GetActiveProjectsAsync() => _projects.GetActiveAsync();

        //-----------------------------
        //finds or creates a company and project
        public async Task<Project> AddProjectAsync(string companyName, string projectName, string? colour)
        {
            var cleanCompany = companyName?.Trim() ?? string.Empty;
            var cleanProject = projectName?.Trim() ?? string.Empty;

            //both names are required
            if (cleanCompany.Length == 0 || cleanProject.Length == 0)
            {
                throw new ArgumentException("Company and project names are required.");
            }

            var company = (await _companies.GetAllAsync())
                .FirstOrDefault(c => string.Equals(c.Name, cleanCompany, StringComparison.OrdinalIgnoreCase));

            //a new client is created on first use
            if (company == null)
            {
                company = new Company { Name = cleanCompany };
                await _companies.AddAsync(company);
            }

            var project = (await _projects.GetByCompanyAsync(company.CompanyId))
                .FirstOrDefault(p => string.Equals(p.Name, cleanProject, StringComparison.OrdinalIgnoreCase));

            //a same named project is reused
            if (project != null)
            {
                return project;
            }

            project = new Project { Name = cleanProject, CompanyId = company.CompanyId, Colour = colour };
            await _projects.AddAsync(project);
            return project;
        }

        //-----------------------------
        //time since the last entry still to log
        public async Task<UnloggedSpan> GetUnloggedSpanAsync(int userId, DateTime now)
        {
            var session = await RequireActiveSessionAsync(userId);
            var start = session.StartedAt;
            var latest = await _entries.GetLatestForUserAsync(userId);

            //an entry from this day moves the start forward
            if (latest != null && latest.EndTime > start)
            {
                start = latest.EndTime;
            }

            return new UnloggedSpan(start, now, PausedTimeBetween(session.Pauses, start, now));
        }

        //-----------------------------
        //finished break time inside the window
        private static TimeSpan PausedTimeBetween(IEnumerable<SessionPause> pauses, DateTime from, DateTime to)
        {
            var total = TimeSpan.Zero;

            //only the overlap of each break counts
            foreach (var pause in pauses.Where(p => p.EndedAt != null))
            {
                var overlapStart = pause.StartedAt > from ? pause.StartedAt : from;
                var overlapEnd = pause.EndedAt!.Value < to ? pause.EndedAt.Value : to;

                //breaks outside the window add nothing
                if (overlapEnd > overlapStart)
                {
                    total += overlapEnd - overlapStart;
                }
            }

            return total;
        }

        //-----------------------------
        //splits a span into work pieces between breaks
        public static IReadOnlyList<(DateTime Start, DateTime End)> SplitAroundPauses(IEnumerable<SessionPause> pauses, DateTime start, DateTime end)
        {
            var pieces = new List<(DateTime Start, DateTime End)>();
            var cursor = start;
            var inside = pauses
                .Where(p => p.EndedAt != null && p.EndedAt > start && p.StartedAt < end)
                .OrderBy(p => p.StartedAt);

            //each break closes the current piece
            foreach (var pause in inside)
            {
                //work before the break is its own piece
                if (pause.StartedAt > cursor)
                {
                    pieces.Add((cursor, pause.StartedAt));
                }

                //the next piece starts after the break
                if (pause.EndedAt!.Value > cursor)
                {
                    cursor = pause.EndedAt.Value;
                }
            }

            //work after the last break is the final piece
            if (end > cursor)
            {
                pieces.Add((cursor, end));
            }

            return pieces;
        }

        //-----------------------------
        //saves unlogged time split around breaks
        public async Task<IReadOnlyList<TimeEntry>> LogEntryAsync(LogEntryRequest request)
        {
            //the log form only saves manual or prompted entries
            if (request.Method == EntryMethod.AutoTracked)
            {
                throw new ArgumentException("Use a manual or prompted method.", nameof(request));
            }

            var session = await RequireActiveSessionAsync(request.UserId);
            var span = await GetUnloggedSpanAsync(request.UserId, request.Now);
            var pieces = SplitAroundPauses(session.Pauses, span.Start, span.End)
                .Where(p => p.End - p.Start >= MinimumPiece)
                .ToList();

            //under a minute of work gives nothing to save
            if (pieces.Count == 0)
            {
                throw new InvalidOperationException("There is no unlogged time to save.");
            }

            var task = await FindOrCreateTaskAsync(request.UserId, request.ProjectId, request.CategoryId);

            var created = pieces
                .Select(p => request.Method == EntryMethod.Manual
                    ? _factory.CreateManual(request.UserId, task.TaskID, p.Start, p.End, request.Note)
                    : _factory.CreateAutoPrompted(request.UserId, task.TaskID, p.Start, p.End, request.Note))
                .ToList();

            await _entries.AddRangeAsync(created);
            return created;
        }

        //-----------------------------
        //reuses or creates the task for project activity and user
        public async Task<WorkTask> FindOrCreateTaskAsync(int userId, int projectId, int categoryId)
        {
            var project = await _projects.GetByIdAsync(projectId);

            //entries only go to active projects
            if (project == null || project.Status != ProjectStatus.Active)
            {
                throw new ArgumentException("Project is not active.", nameof(projectId));
            }

            var category = await _categories.GetByIdAsync(categoryId)
                ?? throw new ArgumentException("Activity does not exist.", nameof(categoryId));

            var existing = await _tasks.FindAsync(projectId, categoryId, userId);

            //one project and activity share one task
            if (existing != null)
            {
                return existing;
            }

            var task = new WorkTask
            {
                Name = category.Name,
                ProjectID = projectId,
                CategoryId = categoryId,
                AssignedUserID = userId,
                Status = WorkTaskStatus.InProgress
            };

            await _tasks.AddAsync(task);
            return task;
        }

        //-----------------------------
        //latest project and activity for prefill
        public async Task<LastEntryInfo?> GetLastEntryAsync(int userId)
        {
            var latest = await _entries.GetLatestForUserAsync(userId);

            //a new user has nothing to prefill
            if (latest?.Task == null)
            {
                return null;
            }

            var path = await _activities.GetPathAsync(latest.Task.CategoryId);
            return new LastEntryInfo(latest.Task.ProjectID, latest.Task.CategoryId, path, latest.EndTime);
        }

        //-----------------------------
        //open unpaused day or an error
        private async Task<DaySession> RequireActiveSessionAsync(int userId)
        {
            var session = await _sessions.GetOpenAsync(userId);

            //logging needs a started day
            if (session == null)
            {
                throw new InvalidOperationException("Start the day before logging.");
            }

            //logging waits until the break ends
            if (session.Pauses.Any(p => p.EndedAt == null))
            {
                throw new InvalidOperationException("Resume tracking before logging.");
            }

            return session;
        }
    }
}
//------------------------------EOF-----------------------------\\
