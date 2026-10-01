using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Data;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Repositories.SQLite;
using TimePlanner.Core.Services;


namespace TimePlanner.Core.Extensions
{
    public static class ServiceCollectionExtensions
    {

        //----------------------------------------------------------------
        //this helper method registers all core repositories, db contexts and services into dependancy injection container
        public static IServiceCollection AddTimePlannerCore(this IServiceCollection services, string connectionString)
        {

            services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));

            services.AddSingleton<IUserRepository, SQLiteUserRepository>();
            services.AddSingleton<IProjectRepository, SQLiteProjectRepository>();
            services.AddSingleton<IWorkTaskRepository, SQLiteWorkTaskRepository>();
            services.AddSingleton<ITimeEntryRepository, SQLiteTimeEntryRepository>();
            services.AddSingleton<TimeEntryFactory>();

            return services;
        }

    }
}
