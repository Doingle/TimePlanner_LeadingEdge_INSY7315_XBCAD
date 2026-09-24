using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Core.Tests.Data
{
    //-----------------------------
    //verifies the migrated sqlite schema enforces the rules agreed in the schema update
    public class SchemaTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        //-----------------------------
        //opens one in memory sqlite connection shared by every context in a test and applies all migrations to it
        public SchemaTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var context = CreateContext();
            context.Database.Migrate();
        }

        private AppDbContext CreateContext() => new AppDbContext(_options);

        //-----------------------------
        //closing the connection discards the in memory database
        public void Dispose()
        {
            _connection.Dispose();
        }

        //-----------------------------
        //saves one company, project, user and task so tests have a valid graph to work with
        private void SeedBasicGraph()
        {
            using var context = CreateContext();
            var company = new Company { Name = "Test Client" };
            var project = new Project { Name = "Test Project", Company = company };
            var user = new AppUser { Name = "Test User", Email = "test@example.com", LocalAccountName = "testuser" };
            var task = new WorkTask { Name = "Build feature", Category = "Coding", Project = project, AssignedUser = user };
            context.Tasks.Add(task);
            context.SaveChanges();
        }

        //-----------------------------
        //every migration is applied and none are left pending
        [Fact]
        public void Migrate_LeavesNoPendingMigrations()
        {
            using var context = CreateContext();

            Assert.Empty(context.Database.GetPendingMigrations());
        }

        //-----------------------------
        //task and project statuses are written to the database as their enum names, not numbers
        [Fact]
        public void Statuses_AreStoredAsText()
        {
            SeedBasicGraph();
            using var context = CreateContext();

            var taskStatuses = context.Database.SqlQueryRaw<string>("SELECT Status AS Value FROM Tasks").ToList();
            var projectStatuses = context.Database.SqlQueryRaw<string>("SELECT Status AS Value FROM Projects").ToList();

            Assert.Equal(new[] { "Open" }, taskStatuses);
            Assert.Equal(new[] { "Active" }, projectStatuses);
        }

        //-----------------------------
        //a company that still owns a project cannot be deleted
        [Fact]
        public void DeletingCompanyWithProjects_IsBlocked()
        {
            SeedBasicGraph();
            using var context = CreateContext();
            var company = context.Companies.Single();

            context.Companies.Remove(company);

            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        //-----------------------------
        //two users cannot share the same windows account name
        [Fact]
        public void LocalAccountName_MustBeUnique()
        {
            using var context = CreateContext();
            context.Users.Add(new AppUser { Name = "User A", Email = "a@example.com", LocalAccountName = "same" });
            context.Users.Add(new AppUser { Name = "User B", Email = "b@example.com", LocalAccountName = "same" });

            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        //-----------------------------
        //any number of dashboard only users may have no windows account name
        [Fact]
        public void LocalAccountName_AllowsMultipleNulls()
        {
            using var context = CreateContext();
            context.Users.Add(new AppUser { Name = "User A", Email = "a@example.com" });
            context.Users.Add(new AppUser { Name = "User B", Email = "b@example.com" });
            context.SaveChanges();

            Assert.Equal(2, context.Users.Count());
        }

        //-----------------------------
        //a new settings row saves and reloads with the agreed default values
        [Fact]
        public void UserSettings_RoundTripsDefaults()
        {
            using (var context = CreateContext())
            {
                context.Users.Add(new AppUser { Name = "User A", Email = "a@example.com", LocalAccountName = "usera", Settings = new UserSettings() });
                context.SaveChanges();
            }

            using var readContext = CreateContext();
            var settings = readContext.UserSettings.Single();

            Assert.Equal(30, settings.CheckInIntervalMinutes);
            Assert.Equal(10, settings.SnoozeMinutes);
            Assert.Equal(3, settings.MaxSnoozes);
            Assert.Equal(new TimeOnly(12, 0), settings.LunchStart);
            Assert.Equal(new TimeOnly(13, 0), settings.LunchEnd);
            Assert.Equal(new TimeOnly(8, 0), settings.WorkdayStart);
            Assert.Equal(new TimeOnly(17, 0), settings.WorkdayEnd);
        }

        //-----------------------------
        //a user cannot end up with a second settings row
        [Fact]
        public void UserSettings_AllowsOnlyOneRowPerUser()
        {
            int userId;
            using (var context = CreateContext())
            {
                var user = new AppUser { Name = "User A", Email = "a@example.com", LocalAccountName = "usera", Settings = new UserSettings() };
                context.Users.Add(user);
                context.SaveChanges();
                userId = user.UserId;
            }

            using var secondContext = CreateContext();
            secondContext.UserSettings.Add(new UserSettings { UserId = userId });

            Assert.Throws<DbUpdateException>(() => secondContext.SaveChanges());
        }
    }
}
//------------------------------EOF-----------------------------\\
