using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using Xunit;

namespace TimePlanner.Core.Tests.Data
{
    //-----------------------------
    //verifies check constraints refuse bad rows and migration repairs old data
    public class CheckConstraintTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        //-----------------------------
        //creates a file backed sqlite database with foreign keys enabled
        public CheckConstraintTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"check_tests_{Guid.NewGuid():N}.db");
            _connection = new SqliteConnection($"Data Source={_dbPath};Foreign Keys=True");
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;
        }

        //-----------------------------
        //clears pool and deletes the temporary database file
        public void Dispose()
        {
            _connection.Close();
            SqliteConnection.ClearPool(_connection);
            _connection.Dispose();

            //if checks whether the temporary database file still exists before deleting it
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }

        //-----------------------------
        //seeds basic entities so foreign keys pass
        private (AppUser User, WorkTask Task) SeedGraph(AppDbContext context)
        {
            var company = new Company { Name = "Client" };
            var project = new Project { Name = "Project", Company = company };
            var user = new AppUser { Name = "User", Email = "user@example.com", LocalAccountName = "user1" };
            var task = new WorkTask { Name = "Task", CategoryId = 1, Project = project, AssignedUser = user };

            context.Tasks.Add(task);
            context.SaveChanges();

            return (user, task);
        }

        //-----------------------------
        //refuses a time entry whose end time equals its start time
        [Fact]
        public void TimeEntry_EndEqualsStart_IsRejected()
        {
            using var context = new AppDbContext(_options);
            context.Database.Migrate();
            var (user, task) = SeedGraph(context);

            var now = DateTime.UtcNow;
            context.TimeEntries.Add(new TimeEntry { UserId = user.UserId, TaskId = task.TaskID, StartTime = now, EndTime = now });

            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        //-----------------------------
        //refuses a day session ending before it started
        [Fact]
        public void DaySession_EndBeforeStart_IsRejected()
        {
            using var context = new AppDbContext(_options);
            context.Database.Migrate();
            var (user, _) = SeedGraph(context);

            var now = DateTime.UtcNow;
            context.DaySessions.Add(new DaySession { UserId = user.UserId, StartedAt = now, EndedAt = now.AddHours(-1) });

            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        //-----------------------------
        //refuses a session pause ending before it started
        [Fact]
        public void SessionPause_EndBeforeStart_IsRejected()
        {
            using var context = new AppDbContext(_options);
            context.Database.Migrate();
            var (user, _) = SeedGraph(context);

            var now = DateTime.UtcNow;
            var session = new DaySession { UserId = user.UserId, StartedAt = now };
            context.DaySessions.Add(session);
            context.SaveChanges();

            context.SessionPauses.Add(new SessionPause { DaySessionId = session.DaySessionId, StartedAt = now, EndedAt = now.AddMinutes(-10) });

            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        //-----------------------------
        //refuses user settings with check in interval below minimum
        [Fact]
        public void UserSettings_IntervalFour_IsRejected()
        {
            using var context = new AppDbContext(_options);
            context.Database.Migrate();
            var (user, _) = SeedGraph(context);

            var settings = new UserSettings { UserId = user.UserId, CheckInIntervalMinutes = 4 };
            context.UserSettings.Add(settings);

            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        //-----------------------------
        //refuses user settings with daily goal hours above maximum
        [Fact]
        public void UserSettings_GoalTwentyFive_IsRejected()
        {
            using var context = new AppDbContext(_options);
            context.Database.Migrate();
            var (user, _) = SeedGraph(context);

            var settings = new UserSettings { UserId = user.UserId, DailyGoalHours = 25 };
            context.UserSettings.Add(settings);

            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        //-----------------------------
        //refuses user settings where lunch start is not before lunch end
        [Fact]
        public void UserSettings_LunchStartAfterEnd_IsRejected()
        {
            using var context = new AppDbContext(_options);
            context.Database.Migrate();
            var (user, _) = SeedGraph(context);

            var settings = new UserSettings { UserId = user.UserId, LunchStart = new TimeOnly(13, 0), LunchEnd = new TimeOnly(12, 0) };
            context.UserSettings.Add(settings);

            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        //-----------------------------
        //saves default settings and a normal time entry
        [Fact]
        public void GoodRows_SaveSuccessfully()
        {
            using var context = new AppDbContext(_options);
            context.Database.Migrate();
            var (user, task) = SeedGraph(context);

            var settings = new UserSettings { UserId = user.UserId };
            context.UserSettings.Add(settings);

            var now = DateTime.UtcNow;
            var entry = new TimeEntry { UserId = user.UserId, TaskId = task.TaskID, StartTime = now, EndTime = now.AddHours(1) };
            context.TimeEntries.Add(entry);

            context.SaveChanges();

            Assert.Equal(1, context.UserSettings.Count());
            Assert.Equal(1, context.TimeEntries.Count());
        }

        //-----------------------------
        //upgrading old database repairs invalid rows before adding check constraints
        [Fact]
        public void Migration_RepairsOldInvalidData()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"repair_test_{Guid.NewGuid():N}.db");
            using var connection = new SqliteConnection($"Data Source={dbPath};Foreign Keys=True");
            connection.Open();

            try
            {
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(connection)
                    .Options;

                using (var context = new AppDbContext(options))
                {
                    context.GetService<IMigrator>().Migrate("20261002171144_AddCategoryArchive");

                    context.Database.ExecuteSqlRaw("INSERT INTO \"Users\" (\"UserId\", \"Name\", \"Email\", \"LocalAccountName\", \"Role\") VALUES (1, 'Old User', 'old@example.com', 'olduser', 0);");
                    context.Database.ExecuteSqlRaw("INSERT INTO \"Companies\" (\"CompanyId\", \"Name\") VALUES (1, 'Old Company');");
                    context.Database.ExecuteSqlRaw("INSERT INTO \"Projects\" (\"ProjectID\", \"Name\", \"CompanyId\", \"Status\") VALUES (1, 'Old Project', 1, 'Active');");
                    context.Database.ExecuteSqlRaw("INSERT INTO \"Tasks\" (\"TaskID\", \"Name\", \"ProjectID\", \"CategoryId\", \"Status\", \"AssignedUserID\") VALUES (1, 'Old Task', 1, 1, 'Open', 1);");

                    context.Database.ExecuteSqlRaw("INSERT INTO \"TimeEntries\" (\"TimeEntryId\", \"UserId\", \"TaskId\", \"StartTime\", \"EndTime\", \"Method\") VALUES (1, 1, 1, '2026-10-02 10:00:00', '2026-10-02 10:00:00', 0);");
                    context.Database.ExecuteSqlRaw("INSERT INTO \"DaySessions\" (\"DaySessionId\", \"UserId\", \"StartedAt\", \"EndedAt\") VALUES (1, 1, '2026-10-02 10:00:00', '2026-10-02 09:00:00');");
                    context.Database.ExecuteSqlRaw("INSERT INTO \"UserSettings\" (\"UserSettingsId\", \"UserId\", \"CheckInIntervalMinutes\", \"SnoozeMinutes\", \"MaxSnoozes\", \"MaxSkipsPerDay\", \"DailyGoalHours\", \"IgnoredCheckInMinutes\", \"IgnoredCheckInAction\", \"LunchStart\", \"LunchEnd\") VALUES (1, 1, 1000, 10, 3, 3, 8.0, 5, 'KeepAsking', '12:00:00', '13:00:00');");
                }

                using (var context = new AppDbContext(options))
                {
                    context.Database.Migrate();

                    Assert.Empty(context.TimeEntries);

                    var session = context.DaySessions.Single();
                    Assert.Equal(session.StartedAt, session.EndedAt);

                    var settings = context.UserSettings.Single();
                    Assert.Equal(90, settings.CheckInIntervalMinutes);
                }
            }
            finally
            {
                connection.Close();
                SqliteConnection.ClearPool(connection);

                //if checks whether the temporary database file still exists before deleting it
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
        }

        //-----------------------------
        //composite index on user id and start time exists in the entity model
        [Fact]
        public void CompositeIndex_ExistsInModel()
        {
            using var context = new AppDbContext(_options);
            var entity = context.Model.FindEntityType(typeof(TimeEntry))!;
            var indexes = entity.GetIndexes();

            Assert.Contains(indexes, i => i.GetDatabaseName() == "IX_TimeEntries_UserId_StartTime");
        }
    }
}
//------------------------------EOF-----------------------------\\
