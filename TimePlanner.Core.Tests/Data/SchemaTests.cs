using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;

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
            var task = new WorkTask { Name = "Build feature", CategoryId = 2, Project = project, AssignedUser = user };
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

            Assert.Equal(90, settings.CheckInIntervalMinutes);
            Assert.Equal(10, settings.SnoozeMinutes);
            Assert.Equal(3, settings.MaxSnoozes);
            Assert.Equal(new TimeOnly(12, 0), settings.LunchStart);
            Assert.Equal(new TimeOnly(13, 0), settings.LunchEnd);
            Assert.Equal(3, settings.MaxSkipsPerDay);
            Assert.Equal(8.0, settings.DailyGoalHours);
            Assert.Equal(5, settings.IgnoredCheckInMinutes);
            Assert.Equal(IgnoredCheckInAction.KeepAsking, settings.IgnoredCheckInAction);
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

        //-----------------------------
        //six default top level categories
        [Fact]
        public void DefaultCategories_AreSeeded()
        {
            using var context = CreateContext();

            var categories = context.Categories.Where(c => c.ParentCategoryId == null).OrderBy(c => c.SortOrder).ToList();

            Assert.Equal(new[] { "Meeting", "Coding", "Design", "Email", "Admin", "Learning" }, categories.Select(c => c.Name));
            Assert.Equal(new[] { "Learning" }, categories.Where(c => !c.IsBillable).Select(c => c.Name));
        }

        //-----------------------------
        //a task must point at an existing category
        [Fact]
        public void TaskWithoutCategory_IsRejected()
        {
            using var context = CreateContext();
            var company = new Company { Name = "Test Client" };
            var project = new Project { Name = "Test Project", Company = company };
            var user = new AppUser { Name = "Test User", Email = "test@example.com" };
            context.Tasks.Add(new WorkTask { Name = "No category", Project = project, AssignedUser = user });

            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        //-----------------------------
        //a category that is still used by a task cannot be deleted
        [Fact]
        public void DeletingCategoryInUse_IsBlocked()
        {
            SeedBasicGraph();
            using var context = CreateContext();
            var coding = context.Categories.Single(c => c.Name == "Coding");

            context.Categories.Remove(coding);

            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        //-----------------------------
        //two categories cannot share a name
        [Fact]
        public void CategoryName_MustBeUnique()
        {
            using var context = CreateContext();
            context.Categories.Add(new Category { Name = "Coding", Colour = "#000000", SortOrder = 99 });

            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        //-----------------------------
        //the ignored check in action is written to the database as its enum name, not a number
        [Fact]
        public void IgnoredCheckInAction_IsStoredAsText()
        {
            using (var context = CreateContext())
            {
                context.Users.Add(new AppUser { Name = "User A", Email = "a@example.com", LocalAccountName = "usera", Settings = new UserSettings() });
                context.SaveChanges();
            }

            using var readContext = CreateContext();
            var actions = readContext.Database.SqlQueryRaw<string>("SELECT IgnoredCheckInAction AS Value FROM UserSettings").ToList();

            Assert.Equal(new[] { "KeepAsking" }, actions);
        }

        //-----------------------------
        //saves a user with no other data and returns its id
        private int AddUser(string localAccountName)
        {
            using var context = CreateContext();
            var user = new AppUser { Name = localAccountName, Email = localAccountName + "@example.com", LocalAccountName = localAccountName };
            context.Users.Add(user);
            context.SaveChanges();
            return user.UserId;
        }

        //-----------------------------
        //the same sub activity name may sit under two different parents
        [Fact]
        public void SubActivity_SameNameUnderDifferentParents_IsAllowed()
        {
            using var context = CreateContext();
            context.Categories.Add(new Category { Name = "Frontend", ParentCategoryId = 2 });
            context.Categories.Add(new Category { Name = "Frontend", ParentCategoryId = 6 });
            context.SaveChanges();

            Assert.Equal(2, context.Categories.Count(c => c.Name == "Frontend"));
        }

        //-----------------------------
        //two sub activities under the same parent cannot share a name
        [Fact]
        public void SubActivity_DuplicateNameUnderSameParent_IsRejected()
        {
            using var context = CreateContext();
            context.Categories.Add(new Category { Name = "Bug fix", ParentCategoryId = 2 });
            context.Categories.Add(new Category { Name = "Bug fix", ParentCategoryId = 2 });

            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        //-----------------------------
        //a category that still has sub activities cannot be deleted
        [Fact]
        public void DeletingCategoryWithChildren_IsBlocked()
        {
            using (var context = CreateContext())
            {
                context.Categories.Add(new Category { Name = "Courses", ParentCategoryId = 7 });
                context.SaveChanges();
            }

            using var deleteContext = CreateContext();
            var learning = deleteContext.Categories.Single(c => c.CategoryId == 7);
            deleteContext.Categories.Remove(learning);

            Assert.Throws<DbUpdateException>(() => deleteContext.SaveChanges());
        }

        //-----------------------------
        //a user may have many closed day sessions but only one open one
        [Fact]
        public void DaySession_OnlyOneOpenPerUser()
        {
            var userId = AddUser("sessionuser");
            var day = new DateTime(2026, 9, 28, 8, 0, 0);
            using (var context = CreateContext())
            {
                context.DaySessions.Add(new DaySession { UserId = userId, StartedAt = day.AddDays(-1), EndedAt = day.AddDays(-1).AddHours(8) });
                context.DaySessions.Add(new DaySession { UserId = userId, StartedAt = day });
                context.SaveChanges();
            }

            using var secondContext = CreateContext();
            secondContext.DaySessions.Add(new DaySession { UserId = userId, StartedAt = day.AddHours(1) });

            Assert.Throws<DbUpdateException>(() => secondContext.SaveChanges());
        }

        //-----------------------------
        //a session may have many finished pauses but only one pause in progress
        [Fact]
        public void SessionPause_OnlyOneOpenPerSession()
        {
            var userId = AddUser("pauseuser");
            var day = new DateTime(2026, 9, 28, 8, 0, 0);
            int sessionId;
            using (var context = CreateContext())
            {
                var session = new DaySession { UserId = userId, StartedAt = day };
                session.Pauses.Add(new SessionPause { StartedAt = day.AddHours(2), EndedAt = day.AddHours(2.5) });
                session.Pauses.Add(new SessionPause { StartedAt = day.AddHours(4) });
                context.DaySessions.Add(session);
                context.SaveChanges();
                sessionId = session.DaySessionId;
            }

            using var secondContext = CreateContext();
            secondContext.SessionPauses.Add(new SessionPause { DaySessionId = sessionId, StartedAt = day.AddHours(5) });

            Assert.Throws<DbUpdateException>(() => secondContext.SaveChanges());
        }

        //-----------------------------
        //deleting a day session also deletes its pauses
        [Fact]
        public void DeletingDaySession_RemovesItsPauses()
        {
            var userId = AddUser("cascadeuser");
            var day = new DateTime(2026, 9, 28, 8, 0, 0);
            using (var context = CreateContext())
            {
                var session = new DaySession { UserId = userId, StartedAt = day, EndedAt = day.AddHours(8) };
                session.Pauses.Add(new SessionPause { StartedAt = day.AddHours(2), EndedAt = day.AddHours(2.5) });
                context.DaySessions.Add(session);
                context.SaveChanges();
            }

            using var deleteContext = CreateContext();
            deleteContext.DaySessions.Remove(deleteContext.DaySessions.Single());
            deleteContext.SaveChanges();

            Assert.Empty(deleteContext.SessionPauses);
        }
    }
}
//------------------------------EOF-----------------------------\\
