using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Tests.Support;
using Xunit;

namespace TimePlanner.Core.Tests.Services
{
    //-----------------------------
    //unit and integration tests for ActivityService
    public class ActivityServiceTests : IDisposable
    {
        private readonly CoreTestHost _host;

        public ActivityServiceTests()
        {
            _host = new CoreTestHost();
        }

        public void Dispose()
        {
            _host.Dispose();
        }

        //-----------------------------
        //tree orders roots, child inherits root attributes
        [Fact]
        public async Task GetTree_OrdersRootsAndInheritsColour()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var service = scope.ServiceProvider.GetRequiredService<ActivityService>();
            var tree = await service.GetTreeAsync();

            var expectedRootNames = new[] { "Meeting", "Coding", "Design", "Email", "Admin", "Learning" };
            Assert.Equal(expectedRootNames, tree.Select(t => t.Name));

            var learningRoot = tree.First(t => t.Name == "Learning");
            var childId = await service.AddActivityAsync(learningRoot.CategoryId, "Reading");

            var updatedTree = await service.GetTreeAsync();
            var updatedLearning = updatedTree.First(t => t.Name == "Learning");
            var childNode = Assert.Single(updatedLearning.Children);

            Assert.Equal(childId, childNode.CategoryId);
            Assert.Equal(learningRoot.Colour, childNode.Colour);
            Assert.Equal(learningRoot.IsBillable, childNode.IsBillable);
        }

        //-----------------------------
        //adding an activity with same name ignoring case reuses existing id
        [Fact]
        public async Task AddActivity_ReusesExistingNameIgnoringCase()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var service = scope.ServiceProvider.GetRequiredService<ActivityService>();
            var id1 = await service.AddActivityAsync(2, "Frontend");
            var id2 = await service.AddActivityAsync(2, "frontend");

            Assert.Equal(id1, id2);
        }

        //-----------------------------
        //nesting beyond three levels deep throws an exception
        [Fact]
        public async Task AddActivity_RejectsFourthLevel()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var service = scope.ServiceProvider.GetRequiredService<ActivityService>();
            var level2Id = await service.AddActivityAsync(2, "Feature work");
            var level3Id = await service.AddActivityAsync(level2Id, "Frontend");

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddActivityAsync(level3Id, "Components"));
        }

        //-----------------------------
        //finding an activity path matches ignoring case and returns null for invalid path
        [Fact]
        public async Task FindByPath_MatchesIgnoringCase()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var service = scope.ServiceProvider.GetRequiredService<ActivityService>();
            await service.AddActivityAsync(2, "Bug fix");
            var found = await service.FindByPathAsync(new[] { "coding", "BUG FIX" });
            Assert.NotNull(found);

            var missing = await service.FindByPathAsync(new[] { "coding", "NonExistent" });
            Assert.Null(missing);
        }

        //-----------------------------
        //recent activities return newest entries first without duplicate activities
        [Fact]
        public async Task GetRecent_ReturnsNewestFirstWithoutDuplicates()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var projects = await entryService.GetActiveProjectsAsync();
            var internalProj = projects.First(p => p.Name == LocalSetupService.InternalProjectName);

            //log Coding (2)
            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "work 1", EntryMethod.Manual, baseTime.AddHours(1)));

            //log Meeting (1)
            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 1, "work 2", EntryMethod.Manual, baseTime.AddHours(2)));

            //log Coding (2) again
            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, internalProj.ProjectID, 2, "work 3", EntryMethod.Manual, baseTime.AddHours(3)));

            var activityService = scope.ServiceProvider.GetRequiredService<ActivityService>();
            var recent = await activityService.GetRecentAsync(user.UserId, 5);

            Assert.Equal(2, recent.Count);
            Assert.Equal(2, recent[0].CategoryId);
            Assert.Equal(new[] { "Coding" }, recent[0].Path);
            Assert.Equal(1, recent[1].CategoryId);
            Assert.Equal(new[] { "Meeting" }, recent[1].Path);
        }

        //-----------------------------
        //activity lookup maps category details to root and full path
        [Fact]
        public async Task GetActivityLookup_ResolvesRootAndPath()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var service = scope.ServiceProvider.GetRequiredService<ActivityService>();
            var childId = await service.AddActivityAsync(7, "Courses");

            var lookup = await service.GetActivityLookupAsync();
            Assert.True(lookup.TryGetValue(childId, out var info));

            Assert.Equal(new[] { "Learning", "Courses" }, info!.Path);
            Assert.Equal(7, info.RootCategoryId);
            Assert.False(info.IsBillable);
        }
    }
}
//------------------------------EOF-----------------------------\\
