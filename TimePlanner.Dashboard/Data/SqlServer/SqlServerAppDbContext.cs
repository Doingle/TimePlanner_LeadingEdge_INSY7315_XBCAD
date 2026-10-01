using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;

namespace TimePlanner.Dashboard.Data.SqlServer
{
    //-----------------------------
    //app data on sql server with migrations kept apart from sqlite
    public class SqlServerAppDbContext : AppDbContext
    {
        public SqlServerAppDbContext(DbContextOptions<SqlServerAppDbContext> options) : base(options)
        {
        }
    }
}
//------------------------------EOF-----------------------------\\
