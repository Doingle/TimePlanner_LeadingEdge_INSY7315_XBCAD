// Design-review and test tooling: compiled into debug builds only, never into a release build.
#if DEBUG
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services;

namespace TimePlanner.Widget.Models
{
    /// <summary>
    /// Sample content from the HTML spec for the screen previews: its projects, its activities and a
    /// day of entries. The top-level activities are Core's own; the levels below them are added here.
    /// It is not in release builds at all: real projects come from the dashboard.
    /// </summary>
    public static class SampleData
    {
        /// <summary>The spec's client projects. Its internal work goes to Core's internal project.</summary>
        public static readonly IReadOnlyList<(string Company, string Project)> Projects =
        [
            ("Contoso", "Project X"),
            ("Northwind Traders", "Northwind Co."),
            ("Atlas Group", "Atlas Rebuild"),
        ];

        /// <summary>The levels the spec has under Core's top-level activities.</summary>
        public static List<ActivityNode> CreateActivityTree() =>
        [
            new("Meeting", new("Stand-up"), new("Client call"), new("Planning"), new("One-to-one")),
            new("Coding",
                new("Feature work", new("Frontend"), new("Backend"), new("Database")),
                new("Bug fix"), new("Code review"), new("Testing")),
            new("Design", new("Wireframes"), new("UI design"), new("Design review")),
            new("Admin", new("Timesheets"), new("Reporting")),
        ];

        /// <summary>A day of entries from 8:00 AM to the spec's last check-in at 3:05 PM, with lunch left out.</summary>
        private static readonly (int FromHour, int FromMinute, int ToHour, int ToMinute, string Project, string Activity)[] DayOfEntries =
        [
            (8, 0, 8, 15, "Project X", "Meeting › Stand-up"),
            (8, 15, 9, 45, "Project X", "Coding › Feature work › Frontend"),
            (9, 45, 10, 30, "Project X", "Coding › Code review"),
            (10, 30, 11, 30, "Northwind Co.", "Meeting › Client call"),
            (11, 30, 12, 0, LocalSetupService.InternalProjectName, "Email"),
            (13, 0, 13, 45, "Project X", "Coding › Bug fix"),
            (13, 45, 14, 30, "Atlas Rebuild", "Design › UI design"),
            (14, 30, 15, 5, "Project X", "Coding › Bug fix"),
        ];

        /// <summary>
        /// Adds the sample projects and activities to a database that has no projects besides Core's
        /// internal one, plus the day of entries for <paramref name="user"/> on <paramref name="entriesOn"/> if given.
        /// </summary>
        public static async Task SeedAsync(IServiceProvider services, AppUser user, DateTime? entriesOn = null)
        {
            await using var scope = services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var entryService = provider.GetRequiredService<EntryService>();
            var activities = provider.GetRequiredService<ActivityService>();

            var projects = await entryService.GetActiveProjectsAsync();
            if (projects.Any(p => p.Name != LocalSetupService.InternalProjectName))
                return;

            foreach (var (company, project) in Projects)
                projects.Add(await entryService.AddProjectAsync(company, project, null));

            foreach (var top in CreateActivityTree())
            {
                var id = await activities.FindByPathAsync([top.Label])
                    ?? throw new InvalidOperationException($"Core has no top-level activity called {top.Label}.");
                await AddChildrenAsync(activities, id, top.Children);
            }

            if (entriesOn is not { } day)
                return;

            var factory = provider.GetRequiredService<TimeEntryFactory>();
            var entries = new List<TimeEntry>();
            foreach (var entry in DayOfEntries)
            {
                var project = projects.First(p => p.Name == entry.Project);
                var categoryId = await activities.FindByPathAsync(entry.Activity.Split(ActivityPath.Separator))
                    ?? throw new InvalidOperationException($"The sample activity {entry.Activity} was not added.");
                var task = await entryService.FindOrCreateTaskAsync(user.UserId, project.ProjectID, categoryId);
                entries.Add(factory.CreateAutoPrompted(user.UserId, task.TaskID,
                    day.Date.AddHours(entry.FromHour).AddMinutes(entry.FromMinute),
                    day.Date.AddHours(entry.ToHour).AddMinutes(entry.ToMinute), null));
            }

            await provider.GetRequiredService<ITimeEntryRepository>().AddRangeAsync(entries);
        }

        private static async Task AddChildrenAsync(ActivityService activities, int parentId, List<ActivityNode> children)
        {
            foreach (var child in children)
                await AddChildrenAsync(activities, await activities.AddActivityAsync(parentId, child.Label), child.Children);
        }
    }
}
#endif
