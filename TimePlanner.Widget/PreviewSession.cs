// Design-review and test tooling: compiled into debug builds only, never into a release build.
#if DEBUG
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget
{
    public sealed class PreviewSession : IDisposable
    {
        public static readonly DateTime Day = new(2023, 8, 1);

        private const int IntervalMinutes = 90;

        private readonly SqliteConnection _keepAlive;   
        private readonly IHost _services;

        private PreviewSession(SqliteConnection keepAlive, IHost services, PreviewClock clock)
        {
            _keepAlive = keepAlive;
            _services = services;
            Clock = clock;
            Flow = services.Services.GetRequiredService<WidgetFlow>();
        }

        public PreviewClock Clock { get; }

        public WidgetFlow Flow { get; }

        public static async Task<PreviewSession> StartAsync(IWidgetHost window, bool withEntries)
        {
            var connectionString = $"Data Source=preview-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keepAlive = new SqliteConnection(connectionString);
            keepAlive.Open();

            var clock = new PreviewClock(Day.AddHours(9));
            var session = new PreviewSession(keepAlive, WidgetServices.CreateForPreview(window, connectionString, clock), clock);
            var services = session._services.Services;
            await WidgetServices.MigrateAsync(services);
            await session.Flow.LoadAsync();
            await SampleData.SeedAsync(services, session.Flow.Session.User, withEntries ? Day : null);
            await session.Flow.LoadAsync();  
            session.Flow.Session.Settings.CheckInIntervalMinutes = IntervalMinutes;
            return session;
        }

        public PreviewSession At(int hour, int minute)
        {
            Clock.Now = Day.AddHours(hour).AddMinutes(minute);
            return this;
        }

        public async Task<WidgetFlow> StartDayAtAsync(int hour, int minute)
        {
            At(hour, minute);
            await Flow.Scheduler.StartAsync();
            return Flow;
        }

        public void Dispose()
        {
            _services.Dispose();
            SqliteConnection.ClearPool(_keepAlive);
            _keepAlive.Dispose();
        }
    }

    public sealed class PreviewClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(Now, LocalTimeZone.GetUtcOffset(Now)).ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
    }
}
#endif

