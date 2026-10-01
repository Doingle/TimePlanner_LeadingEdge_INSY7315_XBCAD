using Microsoft.EntityFrameworkCore;
using TimePlanner.Dashboard.Data.SqlServer;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //checks the sql server setup with no server needed
    public class SqlServerModelTests
    {
        //unreachable server so nothing connects
        private const string OfflineConnection = "Server=offline.invalid;Database=TimePlanner;User Id=test;Password=test;TrustServerCertificate=True";

        //-----------------------------
        //app context on sql server for model checks
        private static SqlServerAppDbContext CreateApp() =>
            new(new DbContextOptionsBuilder<SqlServerAppDbContext>().UseSqlServer(OfflineConnection).Options);

        //-----------------------------
        //login context on sql server for model checks
        private static SqlServerAuthDbContext CreateAuth() =>
            new(new DbContextOptionsBuilder<SqlServerAuthDbContext>()
                .UseSqlServer(OfflineConnection, s => s.MigrationsHistoryTable("__AuthMigrationHistory"))
                .Options);

        //-----------------------------
        //app migrations match the current model
        [Fact]
        public void AppMigrations_MatchModel()
        {
            using var context = CreateApp();

            Assert.False(context.Database.HasPendingModelChanges());
        }

        //-----------------------------
        //login migrations match the current model
        [Fact]
        public void AuthMigrations_MatchModel()
        {
            using var context = CreateAuth();

            Assert.False(context.Database.HasPendingModelChanges());
        }

        //-----------------------------
        //create script holds every app table
        [Fact]
        public void AppModel_ScriptsEveryTable()
        {
            using var context = CreateApp();

            var script = context.Database.GenerateCreateScript();

            Assert.Contains("CREATE TABLE [CheckInSkips]", script);
            Assert.Contains("CREATE TABLE [Categories]", script);
            Assert.Contains("CREATE TABLE [TimeEntries]", script);
        }
    }
}
//------------------------------EOF-----------------------------\\
