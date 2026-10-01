using Microsoft.EntityFrameworkCore;

namespace TimePlanner.Dashboard.Data.SqlServer
{
    //-----------------------------
    //login data on sql server with migrations kept apart from sqlite
    public class SqlServerAuthDbContext : AuthDbContext
    {
        public SqlServerAuthDbContext(DbContextOptions<SqlServerAuthDbContext> options) : base(options)
        {
        }
    }
}
//------------------------------EOF-----------------------------\\
