using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Extensions;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Tests.Repositories
{
    //-----------------------------
    //exercises every repository through the same dependency injection registration the apps use, against migrated in memory sqlite
    public class RepositoryTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;

        public RepositoryTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
            _connection.Open();

            var services = new ServiceCollection();
            services.AddTimePlannerCore("DataSource=:memory:");
            //reuse one open connection so every scope sees the same in memory database
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
            _provider = services.BuildServiceProvider();

            using var scope = _provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
        }

        public void Dispose()
        {
            _provider.Dispose();
            _connection.Dispose();
        }

        //-----------------------------
        //saves a company, project, user and two tasks (one Done) and returns their ids
        private async Task<(int userId, int projectId, int companyId, int taskId)> SeedAsync()
        {
            using var scope = _provider.CreateScope();
            var sp = scope.ServiceProvider;

            var company = new Company { Name = "Client" };
            await sp.GetRequiredService<ICompanyRepository>().AddAsync(company);

            var project = new Project { Name = "Proj", CompanyId = company.CompanyId };
            await sp.GetRequiredService<IProjectRepository>().AddAsync(project);

            var user = new AppUser { Name = "Dev", Email = "dev@example.com", LocalAccountName = "dev" };
            await sp.GetRequiredService<IAppUserRepository>().AddAsync(user);

            var tasks = sp.GetRequiredService<IWorkTaskRepository>();
            var open = new WorkTask { Name = "Open task", Category = "Coding", ProjectID = project.ProjectID, AssignedUserID = user.UserId };
            await tasks.AddAsync(open);
            await tasks.AddAsync(new WorkTask { Name = "Done task", Category = "Coding", ProjectID = project.ProjectID, AssignedUserID = user.UserId, Status = WorkTaskStatus.Done });

            return (user.UserId, project.ProjectID, company.CompanyId, open.TaskID);
        }

        [Fact]
        public async Task CompanyAndProject_AreQueryableByCompany()
        {
            var ids = await SeedAsync();
            using var scope = _provider.CreateScope();

            var projects = await scope.ServiceProvider.GetRequiredService<IProjectRepository>().GetByCompanyAsync(ids.companyId);

            Assert.Single(projects);
            Assert.Equal("Client", (await scope.ServiceProvider.GetRequiredService<ICompanyRepository>().GetByIdAsync(ids.companyId))!.Name);
        }

        [Fact]
        public async Task GetActiveForUser_ExcludesDoneTasks()
        {
            var ids = await SeedAsync();
            using var scope = _provider.CreateScope();

            var active = await scope.ServiceProvider.GetRequiredService<IWorkTaskRepository>().GetActiveForUserAsync(ids.userId);

            Assert.Equal("Open task", Assert.Single(active).Name);
        }

        [Fact]
        public async Task User_IsFoundByEmailAndLocalAccountName()
        {
            await SeedAsync();
            using var scope = _provider.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IAppUserRepository>();

            Assert.NotNull(await users.GetByEmailAsync("dev@example.com"));
            Assert.NotNull(await users.GetByLocalAccountNameAsync("dev"));
            Assert.Null(await users.GetByEmailAsync("nobody@example.com"));
        }

        [Fact]
        public async Task TimeEntries_AreFilteredByUserAndWindow()
        {
            var ids = await SeedAsync();
            var day = new DateTime(2026, 9, 28, 9, 0, 0);
            using var scope = _provider.CreateScope();
            var entries = scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>();

            await entries.AddRangeAsync(new[]
            {
                new TimeEntry { UserId = ids.userId, TaskId = ids.taskId, StartTime = day, EndTime = day.AddHours(1), Method = EntryMethod.Manual },
                new TimeEntry { UserId = ids.userId, TaskId = ids.taskId, StartTime = day.AddDays(5), EndTime = day.AddDays(5).AddHours(1), Method = EntryMethod.AutoPrompted },
            });

            var result = await entries.GetForUserAsync(ids.userId, day.Date, day.Date.AddDays(1));

            var entry = Assert.Single(result);
            Assert.Equal("Proj", entry.Task!.Project!.Name);
        }

        [Fact]
        public async Task AddRange_StoresNothingWhenOneEntryIsInvalid()
        {
            var ids = await SeedAsync();
            var day = new DateTime(2026, 9, 28, 9, 0, 0);
            using var scope = _provider.CreateScope();
            var entries = scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>();

            //second entry points at a task that does not exist, so the foreign key rejects the whole batch
            await Assert.ThrowsAsync<DbUpdateException>(() => entries.AddRangeAsync(new[]
            {
                new TimeEntry { UserId = ids.userId, TaskId = ids.taskId, StartTime = day, EndTime = day.AddHours(1) },
                new TimeEntry { UserId = ids.userId, TaskId = 9999, StartTime = day, EndTime = day.AddHours(1) },
            }));

            using var fresh = _provider.CreateScope();
            Assert.Empty(await fresh.ServiceProvider.GetRequiredService<ITimeEntryRepository>().GetForUserAsync(ids.userId, day.Date, day.Date.AddDays(1)));
        }

        [Fact]
        public async Task TimeSheets_AreReturnedNewestFirst()
        {
            var ids = await SeedAsync();
            using var scope = _provider.CreateScope();
            var sheets = scope.ServiceProvider.GetRequiredService<ITimeSheetRepository>();

            await sheets.AddAsync(new TimeSheet { UserId = ids.userId, ProjectId = ids.projectId, PeriodStart = new DateTime(2026, 9, 1), PeriodEnd = new DateTime(2026, 9, 7) });
            await sheets.AddAsync(new TimeSheet { UserId = ids.userId, ProjectId = ids.projectId, PeriodStart = new DateTime(2026, 9, 8), PeriodEnd = new DateTime(2026, 9, 14) });

            var result = await sheets.GetForUserAsync(ids.userId);

            Assert.Equal(new DateTime(2026, 9, 8), result[0].PeriodStart);
        }
    }
}
//------------------------------EOF-----------------------------\\
