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

        //-----------------------------
        //renaming keeps the same rows so logged time follows the new names
        [Fact]
        public async Task Rename_KeepsLoggedTimeUnderTheNewNames()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);
            await scope.ServiceProvider.GetRequiredService<DaySessionService>().StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var activityService = scope.ServiceProvider.GetRequiredService<ActivityService>();
            var catalogService = scope.ServiceProvider.GetRequiredService<CatalogService>();

            var project = await entryService.AddProjectAsync("Acme", "Portal", null);
            var coding = (await activityService.GetTreeAsync()).First(t => t.Name == "Coding");
            var bugFixId = await activityService.AddActivityAsync(coding.CategoryId, "Bug fix");
            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, project.ProjectID, bugFixId, "fix", EntryMethod.Manual, baseTime.AddHours(1)));

            Assert.True((await catalogService.RenameCompanyAsync(project.CompanyId, "Acme Ltd")).Ok);
            Assert.True((await catalogService.RenameProjectAsync(project.ProjectID, "Customer portal")).Ok);
            Assert.True((await catalogService.RenameActivityAsync(bugFixId, "Bug fixing")).Ok);

            var renamed = await scope.ServiceProvider.GetRequiredService<IProjectRepository>().GetByIdAsync(project.ProjectID);
            Assert.Equal("Customer portal", renamed!.Name);
            Assert.Equal("Acme Ltd", renamed.Company!.Name);
            Assert.Equal(new[] { "Coding", "Bug fixing" }, await activityService.GetPathAsync(bugFixId));

            var entries = await scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>().GetForUserAsync(user.UserId, baseTime.Date, baseTime.Date.AddDays(1));
            Assert.Single(entries);
            Assert.Equal(project.ProjectID, entries[0].Task!.ProjectID);
            Assert.Equal(bugFixId, entries[0].Task!.CategoryId);
        }

        //-----------------------------
        //renames that would clash, break the name rules or touch shared items are refused
        [Fact]
        public async Task Rename_RefusesClashesBadNamesAndSharedItems()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var activityService = scope.ServiceProvider.GetRequiredService<ActivityService>();
            var catalogService = scope.ServiceProvider.GetRequiredService<CatalogService>();

            var portal = await entryService.AddProjectAsync("Acme", "Portal", null);
            var website = await entryService.AddProjectAsync("Acme", "Website", null);
            var globex = await entryService.AddProjectAsync("Globex", "Shop", null);
            var internalProject = (await entryService.GetActiveProjectsAsync()).First(p => BillingRules.IsInternal(p.Company?.Name));
            var coding = (await activityService.GetTreeAsync()).First(t => t.Name == "Coding");
            var bugFixId = await activityService.AddActivityAsync(coding.CategoryId, "Bug fix");
            await activityService.AddActivityAsync(coding.CategoryId, "Review");

            Assert.False((await catalogService.RenameCompanyAsync(globex.CompanyId, "acme")).Ok);
            Assert.False((await catalogService.RenameCompanyAsync(globex.CompanyId, "=evil")).Ok);
            Assert.False((await catalogService.RenameCompanyAsync(globex.CompanyId, LocalSetupService.InternalCompanyName)).Ok);
            Assert.False((await catalogService.RenameCompanyAsync(internalProject.CompanyId, "Ours")).Ok);
            Assert.False((await catalogService.RenameProjectAsync(website.ProjectID, "PORTAL")).Ok);
            Assert.False((await catalogService.RenameProjectAsync(internalProject.ProjectID, "Admin")).Ok);
            Assert.False((await catalogService.RenameActivityAsync(bugFixId, "review")).Ok);
            Assert.False((await catalogService.RenameActivityAsync(bugFixId, "a › b")).Ok);
            Assert.False((await catalogService.RenameActivityAsync(coding.CategoryId, "Programming")).Ok);

            //changing only the case of a name is allowed
            Assert.True((await catalogService.RenameProjectAsync(portal.ProjectID, "PORTAL")).Ok);
        }

        //-----------------------------
        //hiding never deletes, and shown items come back with their logged time
        [Fact]
        public async Task HideAndShow_NeverDeleteAndKeepLoggedTime()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);
            await scope.ServiceProvider.GetRequiredService<DaySessionService>().StartDayAsync(user.UserId, baseTime);

            var entryService = scope.ServiceProvider.GetRequiredService<EntryService>();
            var activityService = scope.ServiceProvider.GetRequiredService<ActivityService>();
            var catalogService = scope.ServiceProvider.GetRequiredService<CatalogService>();
            var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();

            var used = await entryService.AddProjectAsync("Acme", "Portal", null);
            var unused = await entryService.AddProjectAsync("Acme", "Website", null);
            var coding = (await activityService.GetTreeAsync()).First(t => t.Name == "Coding");
            var bugFixId = await activityService.AddActivityAsync(coding.CategoryId, "Bug fix");
            var loginId = await activityService.AddActivityAsync(bugFixId, "Login");
            await entryService.LogEntryAsync(new LogEntryRequest(user.UserId, used.ProjectID, loginId, "fix", EntryMethod.Manual, baseTime.AddHours(1)));

            //an unused project is hidden too, not deleted
            Assert.True((await catalogService.HideProjectAsync(unused.ProjectID)).Ok);
            Assert.Equal(ProjectStatus.Closed, (await projects.GetByIdAsync(unused.ProjectID))!.Status);

            Assert.True((await catalogService.HideCompanyAsync(used.CompanyId)).Ok);
            Assert.DoesNotContain(await entryService.GetActiveProjectsAsync(), p => p.CompanyId == used.CompanyId);

            Assert.True((await catalogService.HideActivityAsync(bugFixId)).Ok);
            Assert.DoesNotContain((await activityService.GetTreeAsync()).First(t => t.Name == "Coding").Children, c => c.CategoryId == bugFixId);
            Assert.False((await catalogService.HideActivityAsync(coding.CategoryId)).Ok);

            var entries = await scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>().GetForUserAsync(user.UserId, baseTime.Date, baseTime.Date.AddDays(1));
            Assert.Single(entries);
            Assert.Equal(new[] { "Coding", "Bug fix", "Login" }, await activityService.GetPathAsync(loginId));

            //showing a client brings back all its projects, showing a child brings back its hidden parent
            Assert.True((await catalogService.ShowCompanyAsync(used.CompanyId)).Ok);
            Assert.Equal(2, (await entryService.GetActiveProjectsAsync()).Count(p => p.CompanyId == used.CompanyId));
            await catalogService.HideActivityAsync(loginId);
            Assert.True((await catalogService.ShowActivityAsync(loginId)).Ok);
            Assert.Equal(loginId, await activityService.FindByPathAsync(new[] { "Coding", "Bug fix", "Login" }));
        }

        //-----------------------------
        //internal work can't be hidden
        [Fact]
        public async Task Hide_InternalNotAllowed()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var internalProject = (await scope.ServiceProvider.GetRequiredService<EntryService>().GetActiveProjectsAsync())
                .First(p => BillingRules.IsInternal(p.Company?.Name));
            var catalogService = scope.ServiceProvider.GetRequiredService<CatalogService>();

            Assert.False((await catalogService.HideProjectAsync(internalProject.ProjectID)).Ok);
            Assert.False((await catalogService.HideCompanyAsync(internalProject.CompanyId)).Ok);
        }

        //-----------------------------
        //projects added under internal work can still be fixed, only the internal project is locked
        [Fact]
        public async Task AddedInternalProject_CanBeRenamedAndHidden()
        {
            var baseTime = new DateTime(2026, 9, 28, 8, 0, 0);
            using var scope = _host.CreateScope();
            await scope.ServiceProvider.GetRequiredService<LocalSetupService>().InitialiseAsync("tester", baseTime);

            var training = await scope.ServiceProvider.GetRequiredService<EntryService>()
                .AddProjectAsync(LocalSetupService.InternalCompanyName, "Trainnig", null);
            var catalogService = scope.ServiceProvider.GetRequiredService<CatalogService>();

            Assert.False((await catalogService.RenameProjectAsync(training.ProjectID, LocalSetupService.InternalProjectName)).Ok);
            Assert.True((await catalogService.RenameProjectAsync(training.ProjectID, "Training")).Ok);
            Assert.True((await catalogService.HideProjectAsync(training.ProjectID)).Ok);
        }
    }
}
//------------------------------EOF-----------------------------\\
