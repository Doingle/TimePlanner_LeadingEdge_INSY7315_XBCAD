using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Tests.Support;
using Xunit;

namespace TimePlanner.Core.Tests.Services
{
    //-----------------------------
    //unit and integration tests for catalog service and billing rules
    public class CatalogAndRulesTests : IDisposable
    {
        private readonly CoreTestHost _host;

        public CatalogAndRulesTests()
        {
            _host = new CoreTestHost();
        }

        public void Dispose()
        {
            _host.Dispose();
        }

        //-----------------------------
        //internal company work is not billable
        [Fact]
        public void BillingRules_InternalIsNotBillable()
        {
            Assert.False(BillingRules.IsBillable(LocalSetupService.InternalCompanyName, true));
            Assert.True(BillingRules.IsInternal(LocalSetupService.InternalCompanyName));
            Assert.True(BillingRules.IsInternal("leading edge (internal)"));
        }

        //-----------------------------
        //non billable root activity makes entry non billable
        [Fact]
        public void BillingRules_NonBillableActivityIsNotBillable()
        {
            Assert.False(BillingRules.IsBillable("Acme", false));
        }

        //-----------------------------
        //client work with billable activity is billable
        [Fact]
        public void BillingRules_ClientWorkIsBillable()
        {
            Assert.True(BillingRules.IsBillable("Acme", true));
        }

        //-----------------------------
        //formula characters empty and long names are rejected
        [Fact]
        public void NameRules_RejectFormulaStartsAndLongNames()
        {
            Assert.False(NameRules.IsValidCompanyOrProjectName("=x"));
            Assert.False(NameRules.IsValidCompanyOrProjectName("@x"));
            Assert.False(NameRules.IsValidCompanyOrProjectName(new string('a', 101)));
            Assert.False(NameRules.IsValidCompanyOrProjectName(""));
            Assert.True(NameRules.IsValidCompanyOrProjectName("Acme"));
        }

        //-----------------------------
        //activity names cannot contain path separators
        [Fact]
        public void NameRules_ActivityRejectsSeparators()
        {
            Assert.False(NameRules.IsValidActivityName("a > b"));
            Assert.False(NameRules.IsValidActivityName("a › b"));
            Assert.True(NameRules.IsValidActivityName("Bug fix"));
        }

        //-----------------------------
        //adding project with unsafe name throws exception
        [Fact]
        public async Task AddProject_RejectsUnsafeNames()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();

            await Assert.ThrowsAsync<ArgumentException>(() => entryService.AddProjectAsync("=evil", "x", null));
        }

        //-----------------------------
        //adding closed project reopens it
        [Fact]
        public async Task AddProject_ReopensClosedProject()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var catalogService = scope.ServiceProvider.GetRequiredService<CatalogService>();
            var activityService = scope.ServiceProvider.GetRequiredService<ActivityService>();

            var project = await entryService.AddProjectAsync("Acme", "Portal", null);
            var tree = await activityService.GetTreeAsync();
            var coding = tree.First(t => t.Name == "Coding");

            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, project.ProjectID, coding.CategoryId, "test", EntryMethod.Manual, baseTime.AddHours(1)));

            var removeOutcome = await catalogService.RemoveProjectAsync(project.ProjectID);
            Assert.Equal(RemoveResult.Hidden, removeOutcome.Result);

            var reopened = await entryService.AddProjectAsync("acme", "portal", null);
            Assert.Equal(project.ProjectID, reopened.ProjectID);
            Assert.Equal(ProjectStatus.Active, reopened.Status);
        }

        //-----------------------------
        //unused project is completely deleted
        [Fact]
        public async Task RemoveProject_UnusedIsDeleted()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var catalogService = scope.ServiceProvider.GetRequiredService<CatalogService>();
            var companyRepo = scope.ServiceProvider.GetRequiredService<ICompanyRepository>();

            var project = await entryService.AddProjectAsync("Acme", "Portal", null);
            var outcome = await catalogService.RemoveProjectAsync(project.ProjectID);

            Assert.Equal(RemoveResult.Deleted, outcome.Result);
            Assert.DoesNotContain(await companyRepo.GetAllAsync(), c => c.Name == "Acme");
        }

        //-----------------------------
        //used project is closed not deleted
        [Fact]
        public async Task RemoveProject_UsedIsClosed()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var catalogService = scope.ServiceProvider.GetRequiredService<CatalogService>();
            var activityService = scope.ServiceProvider.GetRequiredService<ActivityService>();
            var projectRepo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();

            var project = await entryService.AddProjectAsync("Acme", "Portal", null);
            var tree = await activityService.GetTreeAsync();
            var coding = tree.First(t => t.Name == "Coding");

            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, project.ProjectID, coding.CategoryId, "work", EntryMethod.Manual, baseTime.AddHours(1)));

            var outcome = await catalogService.RemoveProjectAsync(project.ProjectID);
            Assert.Equal(RemoveResult.Hidden, outcome.Result);

            var updatedProject = await projectRepo.GetByIdAsync(project.ProjectID);
            Assert.NotNull(updatedProject);
            Assert.Equal(ProjectStatus.Closed, updatedProject.Status);

            var activeProjects = await entryService.GetActiveProjectsAsync();
            Assert.DoesNotContain(activeProjects, p => p.ProjectID == project.ProjectID);
        }

        //-----------------------------
        //internal company cannot be removed
        [Fact]
        public async Task RemoveCompany_InternalNotAllowed()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var companyRepo = scope.ServiceProvider.GetRequiredService<ICompanyRepository>();
            var catalogService = scope.ServiceProvider.GetRequiredService<CatalogService>();

            var companies = await companyRepo.GetAllAsync();
            var internalCompany = companies.First(c => c.Name == LocalSetupService.InternalCompanyName);

            var outcome = await catalogService.RemoveCompanyAsync(internalCompany.CompanyId);
            Assert.Equal(RemoveResult.NotAllowed, outcome.Result);
        }

        //-----------------------------
        //used activity is archived and hidden from tree
        [Fact]
        public async Task RemoveActivity_UsedIsArchivedAndHiddenFromTree()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var activityService = scope.ServiceProvider.GetRequiredService<ActivityService>();
            var catalogService = scope.ServiceProvider.GetRequiredService<CatalogService>();

            var tree = await activityService.GetTreeAsync();
            var coding = tree.First(t => t.Name == "Coding");
            var bugFixId = await activityService.AddActivityAsync(coding.CategoryId, "Bug fix");

            var defaultProject = (await entryService.GetActiveProjectsAsync()).First();
            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, defaultProject.ProjectID, bugFixId, "bugfix", EntryMethod.Manual, baseTime.AddHours(1)));

            var outcome = await catalogService.RemoveActivityAsync(bugFixId);
            Assert.Equal(RemoveResult.Hidden, outcome.Result);

            var updatedTree = await activityService.GetTreeAsync();
            var updatedCoding = updatedTree.First(t => t.Name == "Coding");
            Assert.DoesNotContain(updatedCoding.Children, c => c.Name == "Bug fix");

            var lookup = await activityService.GetActivityLookupAsync();
            Assert.NotNull(lookup[bugFixId].Path);
        }

        //-----------------------------
        //root activity cannot be removed and retyping unarchives child activity
        [Fact]
        public async Task RemoveActivity_RootNotAllowed_AndRetypingUnarchives()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var dayService = scope.ServiceProvider.GetRequiredService<DaySessionService>();
            await dayService.StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var activityService = scope.ServiceProvider.GetRequiredService<ActivityService>();
            var catalogService = scope.ServiceProvider.GetRequiredService<CatalogService>();

            var tree = await activityService.GetTreeAsync();
            var coding = tree.First(t => t.Name == "Coding");

            var rootOutcome = await catalogService.RemoveActivityAsync(coding.CategoryId);
            Assert.Equal(RemoveResult.NotAllowed, rootOutcome.Result);

            var bugFixId = await activityService.AddActivityAsync(coding.CategoryId, "Bug fix");
            var defaultProject = (await entryService.GetActiveProjectsAsync()).First();
            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, defaultProject.ProjectID, bugFixId, "bugfix", EntryMethod.Manual, baseTime.AddHours(1)));

            await catalogService.RemoveActivityAsync(bugFixId);

            var readdedId = await activityService.AddActivityAsync(coding.CategoryId, "bug fix");
            Assert.Equal(bugFixId, readdedId);

            var updatedTree = await activityService.GetTreeAsync();
            var updatedCoding = updatedTree.First(t => t.Name == "Coding");
            Assert.Contains(updatedCoding.Children, c => c.CategoryId == bugFixId);
        }
    }
}
//------------------------------EOF-----------------------------\\
