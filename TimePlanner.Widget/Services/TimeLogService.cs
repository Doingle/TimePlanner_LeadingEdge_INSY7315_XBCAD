using System.IO;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Sync;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Services
{
    /// <summary>
    /// What the widget reads from and writes to the database, through Core's services. Core gives
    /// each operation its own database context, so every call here runs in a scope of its own.
    /// An activity is a node in Core's activity tree; time logged to it in a project goes to the
    /// user's task for that project and activity, which Core adds the first time.
    /// </summary>
    public sealed class TimeLogService(IServiceScopeFactory scopes, TimeProvider clock)
    {
        private const int RecentLimit = 3;

        // Enough of the user's recent activities to find the last few they used in any one project
        private const int RecentHistory = 50;

        /// <summary>
        /// The Windows user, added with the default settings the first time. Core brings the database up to date first.
        /// A day left open overnight is left for the scheduler to close, so the widget can say it did.
        /// </summary>
        public async Task<AppUser> SignInAsync()
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<LocalSetupService>()
                .InitialiseAsync(Environment.UserName, clock.LocalNow(), closeLeftOpenDays: false);
        }

        // How far back the timesheet's day list and the not-sent reminder look
        private const int RecentDays = 14;

        /// <summary>The latest earlier day with logged time when it was never sent, by Core's day rules, or null.</summary>
        public async Task<UnsentDay?> GetUnsentEarlierDayAsync(int userId)
        {
            var today = DateOnly.FromDateTime(clock.LocalNow());
            var recent = await GetRecentDaysAsync(userId);
            var day = DayRules.UnsentEarlierDay(
                recent.Where(d => d.Entries > 0).Select(d => d.Day),
                recent.Where(d => d.Sent != null).Select(d => d.Day),
                today);
            return day is { } unsent && recent.FirstOrDefault(d => d.Day == unsent) is { } found
                ? new UnsentDay(found.Day, found.Logged, found.Entries)
                : null;
        }

        /// <summary>Today and the days before it, newest first, with what was logged and whether each was sent.</summary>
        public async Task<IReadOnlyList<DayChoice>> GetRecentDaysAsync(int userId)
        {
            var today = DateOnly.FromDateTime(clock.LocalNow());
            var first = today.AddDays(-(RecentDays - 1));

            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var entries = await services.GetRequiredService<ITimeEntryRepository>()
                .GetForUserAsync(userId, first.ToDateTime(TimeOnly.MinValue), today.AddDays(1).ToDateTime(TimeOnly.MinValue));
            var sent = await services.GetRequiredService<ISendHistoryStore>().GetAsync();

            return Enumerable.Range(0, RecentDays)
                .Select(back => today.AddDays(-back))
                .Select(day =>
                {
                    var onDay = entries.Where(e => DateOnly.FromDateTime(e.StartTime) == day).ToList();
                    return new DayChoice(day, onDay.Aggregate(TimeSpan.Zero, (total, e) => total + e.GetDuration()), onDay.Count,
                        sent.FirstOrDefault(s => s.Day == day));
                })
                .ToList();
        }

        public async Task SaveSettingsAsync(UserSettings settings)
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SettingsService>().SaveAsync(settings);
        }

        public async Task<List<Project>> GetProjectsAsync()
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<EntryService>().GetActiveProjectsAsync();
        }

        /// <summary>The activity tree (one for every project), and what the user logged in this project last.</summary>
        public async Task<ActivityChoices> GetActivitiesAsync(int userId, int projectId)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var activities = services.GetRequiredService<ActivityService>();

            var tree = (await activities.GetTreeAsync()).Select(ToNode).ToList();

            var used = (await services.GetRequiredService<IWorkTaskRepository>().GetByProjectAsync(projectId))
                .Where(t => t.AssignedUserID == userId && t.Status != WorkTaskStatus.Done)
                .Select(t => t.CategoryId)
                .ToHashSet();
            var recent = (await activities.GetRecentAsync(userId, RecentHistory))
                .Where(r => used.Contains(r.CategoryId) && r.Path.Count > 0)
                .Take(RecentLimit)
                .Select(r => r.Path)
                .ToList();

            return new ActivityChoices(tree, recent);
        }

        private static ActivityNode ToNode(ActivityTreeNode node) => new(node.Name, node.Children.Select(ToNode).ToArray());

        /// <summary>The project and activity of the user's last entry, offered first next time.</summary>
        public async Task<(int? ProjectId, IReadOnlyList<string> Activity)> GetLastChoiceAsync(int userId)
        {
            await using var scope = scopes.CreateAsyncScope();
            var last = await scope.ServiceProvider.GetRequiredService<EntryService>().GetLastEntryAsync(userId);
            if (last == null)
                return (null, []);
            return (last.ProjectId, last.ActivityPath);
        }

        /// <summary>
        /// Logs a period to an activity in a project: adds the activity if the user typed a new one,
        /// finds or adds the user's task for it, and saves an entry for each stretch of the period
        /// outside the breaks. What the user typed is checked here as well as in the fields, before
        /// any of it is saved.
        /// </summary>
        public async Task<int> SaveAsync(int userId, IReadOnlyList<string> projectPath, IReadOnlyList<string> activity, DateTime start, DateTime end,
            string? note, EntryMethod method, IEnumerable<(DateTime Start, DateTime End)> breaks)
        {
            //company and project path must be client and project name
            if (projectPath.Count != 2 || !InputLimits.IsValidCompanyOrProjectName(projectPath[0]) || !InputLimits.IsValidCompanyOrProjectName(projectPath[1]))
            {
                throw new ArgumentException("Choose a client and project to save this entry.", nameof(projectPath));
            }

            note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            if (note?.Length > InputLimits.Note)
                throw new ArgumentException($"A note can be at most {InputLimits.Note} characters.", nameof(note));
            if (activity.Count == 0)
                throw new ArgumentException("An entry needs an activity.", nameof(activity));

            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var project = await services.GetRequiredService<EntryService>().AddProjectAsync(projectPath[0].Trim(), projectPath[1].Trim(), null);
            var categoryId = await FindOrAddActivityAsync(services.GetRequiredService<ActivityService>(),
                services.GetRequiredService<ICategoryRepository>(), activity);
            var task = await services.GetRequiredService<EntryService>().FindOrCreateTaskAsync(userId, project.ProjectID, categoryId);

            var factory = services.GetRequiredService<TimeEntryFactory>();
            var entries = CheckInScheduler.WorkingSpans(start, end, breaks)
                .Where(span => span.End - span.Start >= EntryService.MinimumPiece)
                .Select(span => method == EntryMethod.Manual
                    ? factory.CreateManual(userId, task.TaskID, span.Start, span.End, note)
                    : factory.CreateAutoPrompted(userId, task.TaskID, span.Start, span.End, note))
                .ToList();

            await services.GetRequiredService<ITimeEntryRepository>().AddRangeAsync(entries);
            return project.ProjectID;
        }

        //-----------------------------
        //the activity tree with no recent list
        public async Task<ActivityChoices> GetActivityTreeAsync()
        {
            await using var scope = scopes.CreateAsyncScope();
            var tree = (await scope.ServiceProvider.GetRequiredService<ActivityService>().GetTreeAsync()).Select(ToNode).ToList();
            return new ActivityChoices(tree, []);
        }

        //-----------------------------
        //removes a company by name
        public async Task<RemoveOutcome> RemoveCompanyAsync(string company)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var match = (await services.GetRequiredService<ICompanyRepository>().GetAllAsync())
                .FirstOrDefault(c => string.Equals(c.Name, company.Trim(), StringComparison.OrdinalIgnoreCase));

            //unpersisted company counts as deleted
            if (match == null)
            {
                return new RemoveOutcome(RemoveResult.Deleted, "Removed.");
            }

            return await services.GetRequiredService<CatalogService>().RemoveCompanyAsync(match.CompanyId);
        }

        //-----------------------------
        //removes a project by company and project name
        public async Task<RemoveOutcome> RemoveProjectAsync(string company, string project)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var comp = (await services.GetRequiredService<ICompanyRepository>().GetAllAsync())
                .FirstOrDefault(c => string.Equals(c.Name, company.Trim(), StringComparison.OrdinalIgnoreCase));

            //unpersisted company has no persisted project
            if (comp == null)
            {
                return new RemoveOutcome(RemoveResult.Deleted, "Removed.");
            }

            var proj = (await services.GetRequiredService<IProjectRepository>().GetByCompanyAsync(comp.CompanyId))
                .FirstOrDefault(p => string.Equals(p.Name, project.Trim(), StringComparison.OrdinalIgnoreCase));

            //unpersisted project counts as deleted
            if (proj == null)
            {
                return new RemoveOutcome(RemoveResult.Deleted, "Removed.");
            }

            return await services.GetRequiredService<CatalogService>().RemoveProjectAsync(proj.ProjectID);
        }

        //-----------------------------
        //removes an activity at a given path
        public async Task<RemoveOutcome> RemoveActivityAsync(IReadOnlyList<string> path)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var categoryId = await services.GetRequiredService<ActivityService>().FindByPathAsync(path);

            //unpersisted activity counts as deleted
            if (categoryId == null)
            {
                return new RemoveOutcome(RemoveResult.Deleted, "Removed.");
            }

            return await services.GetRequiredService<CatalogService>().RemoveActivityAsync(categoryId.Value);
        }

        /// <summary>Every client, project and activity, hidden ones included, for Settings → Manage lists.</summary>
        public async Task<ManagedLists> GetManagedListsAsync()
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var projects = await services.GetRequiredService<IProjectRepository>().GetAllAsync();

            // A client with no projects never shows in the picker, so it has nothing to manage
            var clients = projects
                .Where(p => p.Company != null)
                .GroupBy(p => p.CompanyId)
                .Select(g => new ManagedClient(g.Key, g.First().Company!.Name, BillingRules.IsInternal(g.First().Company!.Name),
                    g.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(p => new ManagedProject(p.ProjectID, p.Name, p.Status == ProjectStatus.Closed, CatalogService.IsInternalProject(p)))
                        .ToList()))
                .OrderByDescending(c => c.Locked)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var categories = await services.GetRequiredService<ICategoryRepository>().GetAllAsync();
            ManagedActivity Build(Category category, int depth) => new(category.CategoryId, category.Name, depth, category.IsArchived,
                categories.Where(c => c.ParentCategoryId == category.CategoryId)
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(c => Build(c, depth + 1))
                    .ToList());
            var activities = categories
                .Where(c => c.ParentCategoryId == null && !c.IsArchived)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => Build(c, 1))
                .ToList();

            return new ManagedLists(clients, activities);
        }

        /// <summary>
        /// The saved item at a picker path: a client or project (client › project), or an activity.
        /// Null when it was only typed in the form and is not saved yet.
        /// </summary>
        public async Task<(ListKind Kind, int Id)?> FindListItemAsync(bool activity, IReadOnlyList<string> path)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            if (activity)
                return await services.GetRequiredService<ActivityService>().FindByPathAsync(path) is { } categoryId
                    ? (ListKind.Activity, categoryId)
                    : null;

            var company = (await services.GetRequiredService<ICompanyRepository>().GetAllAsync())
                .FirstOrDefault(c => string.Equals(c.Name, path[0].Trim(), StringComparison.OrdinalIgnoreCase));
            if (company == null)
                return null;
            if (path.Count == 1)
                return (ListKind.Client, company.CompanyId);

            var project = (await services.GetRequiredService<IProjectRepository>().GetByCompanyAsync(company.CompanyId))
                .FirstOrDefault(p => string.Equals(p.Name, path[1].Trim(), StringComparison.OrdinalIgnoreCase));
            return project == null ? null : (ListKind.Project, project.ProjectID);
        }

        /// <summary>
        /// The days already sent to the dashboard that have time logged to an item (or, for an
        /// activity, to anything under it). The dashboard keeps the names it was sent, so these
        /// days show the old name there until they are sent again.
        /// </summary>
        public async Task<IReadOnlyList<DateOnly>> GetSentDaysUsingAsync(int userId, ListKind kind, int id)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var sent = (await services.GetRequiredService<ISendHistoryStore>().GetAsync()).Select(s => s.Day).ToHashSet();
            if (sent.Count == 0)
                return [];

            var from = sent.Min().ToDateTime(TimeOnly.MinValue);
            var to = sent.Max().AddDays(1).ToDateTime(TimeOnly.MinValue);
            var entries = await services.GetRequiredService<ITimeEntryRepository>().GetForUserAsync(userId, from, to);

            var activityIds = new HashSet<int>();
            if (kind == ListKind.Activity)
            {
                var categories = await services.GetRequiredService<ICategoryRepository>().GetAllAsync();
                var next = new Queue<int>([id]);
                while (next.TryDequeue(out var current))
                {
                    activityIds.Add(current);
                    foreach (var child in categories.Where(c => c.ParentCategoryId == current))
                        next.Enqueue(child.CategoryId);
                }
            }

            return entries
                .Where(e => e.Task is { } task && kind switch
                {
                    ListKind.Client => task.Project?.CompanyId == id,
                    ListKind.Project => task.ProjectID == id,
                    _ => activityIds.Contains(task.CategoryId),
                })
                .Select(e => DateOnly.FromDateTime(e.StartTime))
                .Where(sent.Contains)
                .Distinct()
                .Order()
                .ToList();
        }

        /// <summary>
        /// Renames an item. Days already sent that use it are marked as changed, so the timesheet
        /// asks for them to be sent again and the dashboard picks up the new name.
        /// </summary>
        public async Task<ListChangeOutcome> RenameAsync(int userId, ListKind kind, int id, string name)
        {
            var sentDays = await GetSentDaysUsingAsync(userId, kind, id);

            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var catalog = services.GetRequiredService<CatalogService>();
            var outcome = kind switch
            {
                ListKind.Client => await catalog.RenameCompanyAsync(id, name),
                ListKind.Project => await catalog.RenameProjectAsync(id, name),
                _ => await catalog.RenameActivityAsync(id, name),
            };

            if (outcome.Ok)
            {
                var history = services.GetRequiredService<ISendHistoryStore>();
                foreach (var day in sentDays)
                    await history.MarkChangedAsync(day, clock.LocalNow());
            }
            return outcome;
        }

        /// <summary>Hides an item from the pickers, or brings it back. Nothing is deleted, so logged time keeps its names.</summary>
        public async Task<ListChangeOutcome> SetHiddenAsync(ListKind kind, int id, bool hidden)
        {
            await using var scope = scopes.CreateAsyncScope();
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogService>();
            return (kind, hidden) switch
            {
                (ListKind.Client, true) => await catalog.HideCompanyAsync(id),
                (ListKind.Client, false) => await catalog.ShowCompanyAsync(id),
                (ListKind.Project, true) => await catalog.HideProjectAsync(id),
                (ListKind.Project, false) => await catalog.ShowProjectAsync(id),
                (_, true) => await catalog.HideActivityAsync(id),
                _ => await catalog.ShowActivityAsync(id),
            };
        }

        /// <summary>Adds a project, and its client when it is new. A hidden project with the same name comes back instead.</summary>
        public async Task<ListChangeOutcome> AddProjectAsync(string client, string project)
        {
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                var added = await scope.ServiceProvider.GetRequiredService<EntryService>().AddProjectAsync(client, project, null);
                return ListChangeOutcome.Done($"Added {added.Name}.");
            }
            catch (ArgumentException ex)
            {
                return ListChangeOutcome.Refused(ex.Message);
            }
        }

        /// <summary>Adds an activity under another. A hidden one with the same name comes back instead.</summary>
        public async Task<ListChangeOutcome> AddActivityAsync(int parentId, string name)
        {
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<ActivityService>().AddActivityAsync(parentId, name);
                return ListChangeOutcome.Done($"Added {name.Trim()}.");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return ListChangeOutcome.Refused(ex.Message);
            }
        }

        /// <summary>The activity at a path, adding the levels the user typed that are not in the tree yet.</summary>
        private static async Task<int> FindOrAddActivityAsync(ActivityService activities, ICategoryRepository categories,
            IReadOnlyList<string> path)
        {
            if (await activities.FindByPathAsync(path) is { } existing)
                return existing;

            if (!path.All(InputLimits.IsValidActivityName))
                throw new ArgumentException("An activity name is blank, too long, or has characters it cannot have.", nameof(path));

            var id = await activities.FindByPathAsync([path[0]]) ?? throw new ArgumentException("Choose one of the main activities.", nameof(path));
            foreach (var name in path.Skip(1))
                id = await activities.AddActivityAsync(id, name.Trim());
            return id;
        }

        /// <summary>
        /// The user's entries on one day, each with what the timesheet shows: its activity's place in
        /// the tree (Category and its parents) and its project's company.
        /// </summary>
        public async Task<DaySummary> GetDayAsync(int userId, DateTime day)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var entries = await services.GetRequiredService<ITimeEntryRepository>().GetForUserAsync(userId, day.Date, day.Date.AddDays(1));

            var categories = (await services.GetRequiredService<ICategoryRepository>().GetAllAsync()).ToDictionary(c => c.CategoryId);
            foreach (var category in categories.Values)
                category.Parent = category.ParentCategoryId is { } parentId ? categories.GetValueOrDefault(parentId) : null;
            var companies = (await services.GetRequiredService<ICompanyRepository>().GetAllAsync()).ToDictionary(c => c.CompanyId);

            foreach (var task in entries.Select(e => e.Task).OfType<WorkTask>())
            {
                task.Category = categories.GetValueOrDefault(task.CategoryId);
                if (task.Project is { } project)
                    project.Company = companies.GetValueOrDefault(project.CompanyId);
            }

            return new DaySummary(day.Date, entries);
        }

        //-----------------------------
        //one day of rows for the timesheet screen
        public async Task<IReadOnlyList<TimesheetSlot>> GetTimesheetAsync(int userId, DateOnly day)
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TimesheetEditService>().GetDayAsync(userId, day, clock.LocalNow());
        }

        //-----------------------------
        //adds missing time and flags the day as changed
        public async Task<EditResult> AddEntryAsync(int userId, EntryEdit edit)
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<TimesheetEditService>().AddEntryAsync(userId, edit, clock.LocalNow());

            //successful addition marks day changed
            if (result.Ok)
            {
                await scope.ServiceProvider.GetRequiredService<ISendHistoryStore>().MarkChangedAsync(DateOnly.FromDateTime(edit.Start), clock.LocalNow());
            }

            return result;
        }

        //-----------------------------
        //changes an entry and flags the day as changed
        public async Task<EditResult> UpdateEntryAsync(int userId, int entryId, EntryEdit edit)
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<TimesheetEditService>().UpdateEntryAsync(userId, entryId, edit, clock.LocalNow());

            //successful update marks day changed
            if (result.Ok)
            {
                await scope.ServiceProvider.GetRequiredService<ISendHistoryStore>().MarkChangedAsync(DateOnly.FromDateTime(edit.Start), clock.LocalNow());
            }

            return result;
        }

        //-----------------------------
        //deletes an entry and flags the day as changed
        public async Task<EditResult> DeleteEntryAsync(int userId, int entryId)
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<TimesheetEditService>().DeleteEntryAsync(userId, entryId);

            //successful deletion marks day changed
            if (result.Ok && result.Previous != null)
            {
                await scope.ServiceProvider.GetRequiredService<ISendHistoryStore>().MarkChangedAsync(DateOnly.FromDateTime(result.Previous.Start), clock.LocalNow());
            }

            return result;
        }

        //-----------------------------
        //whether the day can be sent, its last send, and whether a send is still waiting to get through
        public async Task<(DayPreview Preview, SentDay? Sent, PendingDay? Pending)> GetSendStateAsync(int userId, DateOnly day)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var send = services.GetRequiredService<DaySendService>();
            var preview = await send.PreviewDayAsync(userId, day, clock.LocalNow());
            var sent = (await services.GetRequiredService<ISendHistoryStore>().GetAsync()).FirstOrDefault(s => s.Day == day);
            var pending = (await send.GetPendingAsync()).FirstOrDefault(p => p.Day == day);

            return (preview, sent, pending);
        }

        private int _retrying;
        private DateTimeOffset _nextRetry;

        //-----------------------------
        //retries the days the user already sent that did not get through. Cheap to call on every tick: one run at a time and, unless forced, at most every 30 seconds.
        //returns the days that got through
        public async Task<IReadOnlyList<DateOnly>> RetryPendingSendsAsync(int userId, bool force = false)
        {
            var nowUtc = clock.GetUtcNow();
            if ((!force && nowUtc < _nextRetry) || Interlocked.Exchange(ref _retrying, 1) == 1)
                return [];

            try
            {
                _nextRetry = nowUtc.AddSeconds(30);
                await using var scope = scopes.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<DaySendService>().RetryPendingAsync(userId, clock.LocalNow(), nowUtc.UtcDateTime);
            }
            finally
            {
                Volatile.Write(ref _retrying, 0);
            }
        }

        //-----------------------------
        //sends a day through the dashboard client
        public async Task<SendOutcome> SendDayAsync(int userId, DateOnly day)
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<DaySendService>().SendDayAsync(userId, day, clock.LocalNow(), clock.GetUtcNow().UtcDateTime);
        }

        //-----------------------------
        //the host name of the dashboard client
        public string DashboardHost
        {
            get
            {
                using var scope = scopes.CreateScope();
                return scope.ServiceProvider.GetRequiredService<DashboardClient>().Address?.Host ?? "the dashboard";
            }
        }

        //-----------------------------
        //signs in to the dashboard and saves the token
        public async Task<SignInOutcome> SignInToDashboardAsync(string email, string password)
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<DaySendService>().SignInAsync(email, password);
        }

        //-----------------------------
        //gets the signed in dashboard email if still valid or refreshable
        public async Task<string?> GetDashboardEmailAsync()
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<DaySendService>().GetSignedInEmailAsync(clock.GetUtcNow().UtcDateTime);
        }

        //-----------------------------
        //signs out of the dashboard
        public async Task SignOutOfDashboardAsync()
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DaySendService>().SignOutAsync();
        }

        //-----------------------------
        //loads sample day data from sample csv file
        public async Task<SampleDayResult> LoadSampleDayAsync(int userId)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "SampleData", "sample-day.csv");
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<SampleDayService>().LoadAsync(userId, path, clock.LocalNow());
        }
    }
}
