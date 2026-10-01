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
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
            return services;
        }

    }
}
