using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TimePlanner.Dashboard.Data.SqlServer
{
    //-----------------------------
    //settings shared by both sql server design time factories
    internal static class SqlServerDesignTime
    {
        //placeholder used only to build migrations offline
        public const string Connection = "Server=localhost;Database=TimePlannerDesign;Trusted_Connection=True;TrustServerCertificate=True";

        //history table for the login migrations
        public const string AuthHistoryTable = "__AuthMigrationHistory";
    }

    //-----------------------------
    //builds the app context for sql server migrations
    public class SqlServerAppDbContextFactory : IDesignTimeDbContextFactory<SqlServerAppDbContext>
    {
        //-----------------------------
        //sql server options for dotnet ef
        public SqlServerAppDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<SqlServerAppDbContext>()
                .UseSqlServer(SqlServerDesignTime.Connection)
                .Options;

            return new SqlServerAppDbContext(options);
        }
    }

    //-----------------------------
    //builds the login context for sql server migrations
    public class SqlServerAuthDbContextFactory : IDesignTimeDbContextFactory<SqlServerAuthDbContext>
    {
        //-----------------------------
        //sql server options with the login history table
        public SqlServerAuthDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<SqlServerAuthDbContext>()
                .UseSqlServer(SqlServerDesignTime.Connection, s => s.MigrationsHistoryTable(SqlServerDesignTime.AuthHistoryTable))
                .Options;

            return new SqlServerAuthDbContext(options);
        }
    }
}
//------------------------------EOF-----------------------------\\
