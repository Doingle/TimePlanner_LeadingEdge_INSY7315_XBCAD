using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TimePlanner.Core.Data;
using TimePlanner.Widget.Models;
using TimePlanner.Widget.Services;

namespace TimePlanner.Widget
{
    public static class WidgetServices
    {
        public static string DataFolder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimePlanner");

        public static IHost CreateForApp(IWidgetHost window)
        {
            Directory.CreateDirectory(DataFolder);
            var builder = CreateBuilder();
            var connectionString = builder.Configuration.GetConnectionString("Default")
                ?? $"Data Source={Path.Combine(DataFolder, "timeplanner.db")}";
            Register(builder.Services, window, connectionString, TimeProvider.System,
                new PreferencesStore(Path.Combine(DataFolder, "widget.json")));
            return builder.Build();
        }

#if DEBUG
        public static IHost CreateForPreview(IWidgetHost window, string connectionString, TimeProvider clock)
        {
            var builder = CreateBuilder();
            Register(builder.Services, window, connectionString, clock, new PreferencesStore(null));
            return builder.Build();
        }
#endif

        public static async Task MigrateAsync(IServiceProvider services)
        {
            var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.MigrateAsync();
        }

        private static HostApplicationBuilder CreateBuilder()
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                DisableDefaults = true,
                ContentRootPath = AppContext.BaseDirectory,
            });
            builder.Configuration.SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            builder.Logging.SetMinimumLevel(LogLevel.Warning).AddEventLog().AddDebug();
            return builder;
        }

        private static void Register(IServiceCollection services, IWidgetHost window, string connectionString, TimeProvider clock,
            PreferencesStore preferences)
        {
            services.AddTimePlannerCore(connectionString);

            services.AddSingleton(window);
            services.AddSingleton(clock);
            services.AddSingleton(preferences);
            services.AddSingleton<WidgetSession>();
            services.AddSingleton<LunchBreakDetector>();
            services.AddSingleton<SnoozeManager>();
            services.AddSingleton<CheckInScheduler>();
            services.AddSingleton<TimeLogService>();
            services.AddSingleton<CsvExportService>();
            services.AddSingleton<WidgetFlow>();
        }
    }
}
