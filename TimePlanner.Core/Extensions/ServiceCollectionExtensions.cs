
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
        public static IServiceCollection AddTimePlannerCore(this IServiceCollection services, string connectionString) =>
            services.AddTimePlannerCore<AppDbContext>(o => o.UseSqlite(connectionString));

        //-----------------------------
        //registers core with any provider and context type
        public static IServiceCollection AddTimePlannerCore<TContext>(this IServiceCollection services, Action<DbContextOptionsBuilder> configureDatabase)
            where TContext : AppDbContext
        {
            services.AddDbContext<AppDbContext, TContext>(configureDatabase);

            services.AddScoped<ICompanyRepository, SQLiteCompanyRepository>();
            services.AddScoped<IProjectRepository, SQLiteProjectRepository>();
            services.AddScoped<IWorkTaskRepository, SQLiteWorkTaskRepository>();
            services.AddScoped<IAppUserRepository, SQLiteAppUserRepository>();
            services.AddScoped<ITimeEntryRepository, SQLiteTimeEntryRepository>();
            services.AddScoped<ITimeSheetRepository, SQLiteTimeSheetRepository>();
            services.AddScoped<ICategoryRepository, SQLiteCategoryRepository>();
            services.AddScoped<IDaySessionRepository, SQLiteDaySessionRepository>();
            services.AddScoped<ICheckInSkipRepository, SQLiteCheckInSkipRepository>();

            //widget services share the scope of their repos
            services.AddSingleton<TimeEntryFactory>();
            services.AddScoped<DaySessionService>();
            services.AddScoped<SettingsService>();
            services.AddScoped<ActivityService>();
            services.AddScoped<EntryService>();
            services.AddScoped<LocalSetupService>();

            //single engine and clock for the widget lifetime
            services.AddSingleton<IClock, SystemClock>();
            services.AddSingleton<CheckInEngine>();

            return services;
        }

    }
}
//------------------------------EOF-----------------------------\\
