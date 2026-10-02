
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
 //scoped lifetimes, so a web request or a widget operation gets one DbContext shared by its repositories
        public static IServiceCollection AddTimePlannerCore(this IServiceCollection services, string connectionString) =>
            services.AddTimePlannerCore(_ => connectionString);

        //----------------------------------------------------------------
        //same registration, but the connection string is looked up when the first database context is created. A web host uses this so the value
        //can come from configuration that is only final once the host is built (and so a test host can run against its own database)
        public static IServiceCollection AddTimePlannerCore(this IServiceCollection services, Func<IServiceProvider, string> connectionString) =>
            services.AddTimePlannerCore<AppDbContext>((sp, o) => o.UseSqlite(connectionString(sp)));

        //-----------------------------
        //registers core with any provider and context type
        public static IServiceCollection AddTimePlannerCore<TContext>(this IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> configureDatabase)
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
            services.AddScoped<CatalogService>();
            services.AddScoped<TimesheetEditService>();

            //single engine and clock for the widget lifetime
            services.AddSingleton<IClock, SystemClock>();
            services.AddSingleton<CheckInEngine>();

            return services;
        }

    }
}
//------------------------------EOF-----------------------------\\
