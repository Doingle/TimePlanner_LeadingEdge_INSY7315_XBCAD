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

        /// <summary>The Windows user, added with the default settings the first time. Core brings the database up to date first.</summary>
        public async Task<AppUser> SignInAsync()
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<LocalSetupService>()
                .InitialiseAsync(Environment.UserName, clock.LocalNow());
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
        //whether the day can be sent and its last send
        public async Task<(DayPreview Preview, SentDay? Sent)> GetSendStateAsync(int userId, DateOnly day)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var preview = await services.GetRequiredService<DaySendService>().PreviewDayAsync(userId, day, clock.LocalNow());
            var sent = (await services.GetRequiredService<ISendHistoryStore>().GetAsync()).FirstOrDefault(s => s.Day == day);

            return (preview, sent);
        }

        //-----------------------------
        //sends a day through the dashboard client
        public async Task<SendOutcome> SendDayAsync(int userId, DateOnly day)
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<DaySendService>().SendDayAsync(userId, day, clock.LocalNow(), clock.GetUtcNow().UtcDateTime);
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
