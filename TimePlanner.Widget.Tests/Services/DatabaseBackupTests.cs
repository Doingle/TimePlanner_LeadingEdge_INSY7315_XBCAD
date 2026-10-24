using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TimePlanner.Core.Data;
using TimePlanner.Widget.Services;
using Xunit;

namespace TimePlanner.Widget.Tests.Services
{
    //-----------------------------
    //tests for the copy taken before the widget migrates its database. Every test works in its own temporary folder, never in the real data folder
    public class DatabaseBackupTests : IDisposable
    {
        //the last migration before the one that adds the check constraints
        private const string OldMigration = "20261002171144_AddCategoryArchive";

        private static readonly DateTime Now = new(2026, 10, 24, 14, 5, 1);

        private readonly string _folder = Path.Combine(Path.GetTempPath(), $"backup_tests_{Guid.NewGuid():N}");
        private readonly string _dbPath;

        public DatabaseBackupTests()
        {
            Directory.CreateDirectory(_folder);
            _dbPath = Path.Combine(_folder, "timeplanner.db");
        }

        //-----------------------------
        //releases every pooled connection so the temporary folder can be deleted
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }

        private AppDbContext NewContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

        //-----------------------------
        //a database one migration behind, holding three time entries and the rows they depend on
        private void SeedOldDatabase()
        {
            using var context = NewContext();
            context.GetService<IMigrator>().Migrate(OldMigration);

            context.Database.ExecuteSqlRaw("INSERT INTO \"Users\" (\"UserId\", \"Name\", \"Email\", \"LocalAccountName\", \"Role\") VALUES (1, 'Old User', 'old@example.com', 'olduser', 0);");
            context.Database.ExecuteSqlRaw("INSERT INTO \"Companies\" (\"CompanyId\", \"Name\") VALUES (1, 'Old Company');");
            context.Database.ExecuteSqlRaw("INSERT INTO \"Projects\" (\"ProjectID\", \"Name\", \"CompanyId\", \"Status\") VALUES (1, 'Old Project', 1, 'Active');");
            context.Database.ExecuteSqlRaw("INSERT INTO \"Tasks\" (\"TaskID\", \"Name\", \"ProjectID\", \"CategoryId\", \"Status\", \"AssignedUserID\") VALUES (1, 'Old Task', 1, 1, 'Open', 1);");
            for (var i = 1; i <= 3; i++)
            {
                var start = $"2026-10-02 {8 + i:00}:00:00";
                var end = $"2026-10-02 {8 + i:00}:45:00";
                context.Database.ExecuteSql($"INSERT INTO \"TimeEntries\" (\"TimeEntryId\", \"UserId\", \"TaskId\", \"StartTime\", \"EndTime\", \"Method\") VALUES ({i}, 1, 1, {start}, {end}, 0)");
            }
            context.Database.ExecuteSqlRaw("INSERT INTO \"DaySessions\" (\"DaySessionId\", \"UserId\", \"StartedAt\", \"EndedAt\") VALUES (1, 1, '2026-10-02 08:00:00', '2026-10-02 17:00:00');");
            context.Database.ExecuteSqlRaw("INSERT INTO \"UserSettings\" (\"UserSettingsId\", \"UserId\", \"CheckInIntervalMinutes\", \"SnoozeMinutes\", \"MaxSnoozes\", \"MaxSkipsPerDay\", \"DailyGoalHours\", \"IgnoredCheckInMinutes\", \"IgnoredCheckInAction\", \"LunchStart\", \"LunchEnd\") VALUES (1, 1, 90, 10, 3, 3, 8.0, 5, 'KeepAsking', '12:00:00', '13:00:00');");
        }

        //how many time entries and users a database file holds, read straight from the file
        private static (long TimeEntries, long Users) Counts(string file)
        {
            using var connection = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT (SELECT COUNT(*) FROM \"TimeEntries\"), (SELECT COUNT(*) FROM \"Users\")";
            using var reader = command.ExecuteReader();
            reader.Read();
            return (reader.GetInt64(0), reader.GetInt64(1));
        }

        //-----------------------------
        //migrating after the backup keeps every entry, and the backup is a copy of the database as it was before the migration
        [Fact]
        public async Task MigrationWithData_KeepsEveryEntry_AndTheBackupHoldsTheOldDatabase()
        {
            SeedOldDatabase();

            string? backup;
            using (var context = NewContext())
            {
                backup = await DatabaseBackup.BackUpBeforeMigratingAsync(context, Now);
                await context.Database.MigrateAsync();

                Assert.Equal(3, context.TimeEntries.Count());
                Assert.Equal(1, context.DaySessions.Count());
                Assert.Equal(1, context.UserSettings.Count());
                Assert.Equal(1, context.Users.Count());
            }

            Assert.Equal(Path.Combine(_folder, "backups", "timeplanner-20261024-140501.db"), backup);
            Assert.True(File.Exists(backup));
            Assert.Equal((3L, 1L), Counts(backup!));

            //the copy was taken before the migration ran, so it still sits at the old schema
            using var check = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False");
            check.Open();
            using var command = check.CreateCommand();
            command.CommandText = "SELECT MAX(\"MigrationId\") FROM \"__EFMigrationsHistory\"";
            Assert.Equal(OldMigration, (string)command.ExecuteScalar()!);
        }

        //-----------------------------
        //a new install and a database that is already up to date have nothing to protect
        [Fact]
        public async Task NothingToProtect_MakesNoBackup()
        {
            using (var fresh = NewContext())
                Assert.Null(await DatabaseBackup.BackUpBeforeMigratingAsync(fresh, Now));

            using (var context = NewContext())
            {
                await context.Database.MigrateAsync();
                Assert.Null(await DatabaseBackup.BackUpBeforeMigratingAsync(context, Now));
            }

            Assert.False(Directory.Exists(Path.Combine(_folder, "backups")));
        }

        //-----------------------------
        //only the newest backups stay, and files in the folder that are not backups of this database are left alone
        [Fact]
        public async Task OnlyTheNewestBackupsAreKept()
        {
            SeedOldDatabase();
            var folder = Path.Combine(_folder, "backups");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "keep me");

            using (var context = NewContext())
                for (var minute = 0; minute < 4; minute++)
                    await DatabaseBackup.BackUpBeforeMigratingAsync(context, Now.AddMinutes(minute), keep: 2);

            var kept = Directory.GetFiles(folder, "timeplanner-*.db").Select(Path.GetFileName).Order().ToArray();
            Assert.Equal(new[] { "timeplanner-20261024-140701.db", "timeplanner-20261024-140801.db" }, kept);
            Assert.True(File.Exists(Path.Combine(folder, "notes.txt")));
            Assert.Empty(Directory.GetFiles(folder, "*.partial"));
        }

        //-----------------------------
        //when the copy cannot be made the backup throws, which stops the migration before it changes anything
        [Fact]
        public async Task FailedBackup_Throws_AndTheMigrationIsStillPending()
        {
            SeedOldDatabase();
            //a file where the backups folder has to be makes the folder impossible to create
            File.WriteAllText(Path.Combine(_folder, "backups"), "in the way");

            using var context = NewContext();
            await Assert.ThrowsAnyAsync<IOException>(() => DatabaseBackup.BackUpBeforeMigratingAsync(context, Now));

            Assert.NotEmpty(await context.Database.GetPendingMigrationsAsync());
        }
    }
}
