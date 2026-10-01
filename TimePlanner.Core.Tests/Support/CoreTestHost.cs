using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Data;
using TimePlanner.Core.Extensions;

namespace TimePlanner.Core.Tests.Support
{
    //-----------------------------
    //real core services over one migrated in memory database
    public sealed class CoreTestHost : IDisposable
    {
        private readonly SqliteConnection _connection;

        public ServiceProvider Provider { get; }

        //-----------------------------
        //opens the connection then registers and migrates
        public CoreTestHost(Action<IServiceCollection>? configure = null)
        {
            _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
            _connection.Open();

            var services = new ServiceCollection();
            services.AddTimePlannerCore("DataSource=:memory:");

            //every scope shares the one open connection
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
            configure?.Invoke(services);
            Provider = services.BuildServiceProvider();

            using var scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
        }

        //-----------------------------
        //new scope like one widget operation
        public IServiceScope CreateScope() => Provider.CreateScope();

        //-----------------------------
        //disposes the provider then the connection
        public void Dispose()
        {
            Provider.Dispose();
            _connection.Dispose();
        }
    }
}
//------------------------------EOF-----------------------------\\
