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
        public static IServiceCollection AddTimePlannerCore(this IServiceCollection services, string connectionString)
        {
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));

            services.AddScoped<ICompanyRepository, SQLiteCompanyRepository>();
            services.AddScoped<IProjectRepository, SQLiteProjectRepository>();
            services.AddScoped<IWorkTaskRepository, SQLiteWorkTaskRepository>();
            services.AddScoped<IAppUserRepository, SQLiteAppUserRepository>();
            services.AddScoped<ITimeEntryRepository, SQLiteTimeEntryRepository>();
            services.AddScoped<ITimeSheetRepository, SQLiteTimeSheetRepository>();
            services.AddScoped<ICategoryRepository, SQLiteCategoryRepository>();
            services.AddScoped<IDaySessionRepository, SQLiteDaySessionRepository>();

            //widget services share the scope of their repos
            services.AddSingleton<TimeEntryFactory>();
            services.AddScoped<DaySessionService>();
            services.AddScoped<SettingsService>();
            services.AddScoped<ActivityService>();
            services.AddScoped<EntryService>();
            services.AddScoped<LocalSetupService>();

            return services;
        }

    }
}
//------------------------------EOF-----------------------------\\
