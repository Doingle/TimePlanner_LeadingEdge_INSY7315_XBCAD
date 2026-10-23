using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Extensions;
using TimePlanner.Core.Services;
using TimePlanner.Core.Sync;
using TimePlanner.Widget.Models;
using TimePlanner.Widget.Services;
using TimePlanner.Widget.Views;

namespace TimePlanner.Widget.Tests;

public class ReminderFlowTests
{
    // All WPF tests share an STA dispatcher and theme resources, without starting the real app.
    private static readonly Lazy<Task<Dispatcher>> Ui = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (var name in new[] { "Tokens", "Text", "Buttons", "Fields", "Options", "Menus", "Card" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/TimePlanner.Widget;component/Themes/{name}.xaml")
                    });
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    });

    private static async Task OnUi(Func<Task> test) => await (await Ui.Value).InvokeAsync(test).Task.Unwrap();

    [Fact]
    public Task TimerHasNoNotice_DayEndedRowReplacesOpenTimesheet_AndOpensItWithoutSending() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        host.Flow.ShowTimer();
        var timer = Assert.IsType<TimerView>(host.Window.Screen);
        Assert.Null(timer.FindName("ReminderNotice"));

        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.EndDayAsync();
        var ended = Assert.IsType<DayEndedView>(host.Window.Screen);
        var notice = (UnsentDayReminderView)ended.FindName("ReminderNotice");
        Assert.Equal(Visibility.Visible, notice.Visibility);
        Assert.Equal(Visibility.Collapsed, ((Button)ended.FindName("OpenTimesheetButton")).Visibility);
        ((Button)notice.FindName("OpenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.IsType<TimesheetReviewView>(host.Window.Screen);
        Assert.Equal(0, host.Http.Requests);
        Assert.Empty(host.History.Days);
    });

    [Fact]
    public Task EndDayAndRestartRestoreNotice_ResumeKeepsTheSameDay() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.EndDayAsync();
        var ended = Assert.IsType<DayEndedView>(host.Window.Screen);
        Assert.Equal(Visibility.Visible, ((UnsentDayReminderView)ended.FindName("ReminderNotice")).Visibility);
        Render(ended, "day-ended");

        // New scheduler and flow over the same in-memory database, as on restart.
        host.Flow.Scheduler.Dispose();
        var restartedScheduler = ActivatorUtilities.CreateInstance<CheckInScheduler>(host.Services);
        using (restartedScheduler)
        {
            var restarted = ActivatorUtilities.CreateInstance<WidgetFlow>(host.Services, restartedScheduler);
            await restarted.LoadAsync();
            restarted.ShowStart();
            Assert.IsType<DayEndedView>(host.Window.Screen);
            Assert.True(restartedScheduler.EndedToday);
            await restarted.ResumeDayAsync();
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Single(await db.DaySessions.ToListAsync());
            Assert.Null((await db.DaySessions.SingleAsync()).EndedAt);
        }
    });

    [Fact]
    public Task SentDayShowsOpenTimesheetInsteadOfTheRow() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        host.History.Days.Add(new SentDay(host.Flow.Reminder.Today, host.Clock.LocalNow(), 1, 1));
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.EndDayAsync();
        var ended = Assert.IsType<DayEndedView>(host.Window.Screen);
        Assert.Equal(Visibility.Collapsed, ((UnsentDayReminderView)ended.FindName("ReminderNotice")).Visibility);
        Assert.Equal(Visibility.Visible, ((Button)ended.FindName("OpenTimesheetButton")).Visibility);
    });

    [Fact]
    public Task EmptyDayHasNoRow() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.EndDayAsync();
        var ended = Assert.IsType<DayEndedView>(host.Window.Screen);
        Assert.Equal(Visibility.Collapsed, ((UnsentDayReminderView)ended.FindName("ReminderNotice")).Visibility);
    });

    [Fact]
    public Task ResumeAndEndAgainShowsTheRowAgain() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.EndDayAsync();
        await host.Flow.ResumeDayAsync();
        Assert.False(host.Flow.Reminder.IsVisible);
        host.Clock.UtcNow += TimeSpan.FromMinutes(10);
        await host.Flow.EndDayAsync();
        var ended = Assert.IsType<DayEndedView>(host.Window.Screen);
        Assert.Equal(Visibility.Visible, ((UnsentDayReminderView)ended.FindName("ReminderNotice")).Visibility);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task SendingHidesVisibleNoticeOnlyOnSuccess(bool success) => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.EndDayAsync();
        var ended = Assert.IsType<DayEndedView>(host.Window.Screen);
        var notice = (UnsentDayReminderView)ended.FindName("ReminderNotice");
        Assert.Equal(Visibility.Visible, notice.Visibility);
        host.Http.Succeeds = success;
        var result = await host.Flow.SendDayAsync(host.Flow.Reminder.Today);
        Assert.Equal(success ? SendStatus.Sent : SendStatus.Failed, result.Status);
        Assert.Equal(success ? Visibility.Collapsed : Visibility.Visible, notice.Visibility);
        Assert.Equal(success ? Visibility.Visible : Visibility.Collapsed, ((Button)ended.FindName("OpenTimesheetButton")).Visibility);
        Assert.Equal(1, host.Http.Requests);
        Assert.Equal(success ? 1 : 0, host.History.Days.Count);
    });

    [Fact]
    public Task PreviousDayScreenDoesNotShowTodaysNotice() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        host.Flow.ShowDayEnded(new DaySummary(host.Clock.LocalNow().AddDays(-1), []));
        var ended = Assert.IsType<DayEndedView>(host.Window.Screen);
        Assert.Equal(Visibility.Collapsed, ((UnsentDayReminderView)ended.FindName("ReminderNotice")).Visibility);
    });

    private static void Render(FrameworkElement view, string name)
    {
        view.Measure(new Size(380, double.PositiveInfinity));
        view.Arrange(new Rect(view.DesiredSize));
        view.UpdateLayout();
        Assert.InRange(view.ActualHeight, 100, 850);
        var folder = Environment.GetEnvironmentVariable("TIMEPLANNER_REMINDER_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(folder))
            return;
        Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }

    private sealed class RecordingHost : IWidgetHost
    {
        public FrameworkElement? Screen { get; private set; }
        public void Present(FrameworkElement screen, bool activate = true)
        {
            if (Screen?.FindName("ReminderNotice") is UnsentDayReminderView old)
                old.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Screen = screen;
            if (screen.FindName("ReminderNotice") is UnsentDayReminderView current)
                current.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        }
        public void HideWidget() { }
    }

    private sealed class FakeDashboard : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public bool Succeeds { get; set; } = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal("/api/v1/timesheets/import", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(Succeeds ? HttpStatusCode.OK : HttpStatusCode.InternalServerError)
            {
                Content = JsonContent.Create(new { created = 1, skipped = 0, errors = Array.Empty<string>() })
            });
        }
    }

    private sealed class TestHost : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public ReminderClock Clock { get; } = new();
        public ReminderHistory History { get; } = new();
        public RecordingHost Window { get; } = new();
        public FakeDashboard Http { get; } = new();
        // Windows startup kept in memory, so the tests never touch the real Run key
        public InMemoryStartupRegistration Startup { get; } = new();
        public ServiceProvider Services { get; private set; } = null!;
        public WidgetFlow Flow => Services.GetRequiredService<WidgetFlow>();

        public static async Task<TestHost> CreateAsync()
        {
            var host = new TestHost();
            await host._connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTimePlannerCore<AppDbContext>((_, options) => options.UseSqlite(host._connection));
            services.AddSingleton<TimeProvider>(host.Clock);
            services.AddSingleton<IClock>(new TimeProviderClock(host.Clock));
            services.AddSingleton<IWidgetHost>(host.Window);
            services.AddSingleton<ISendHistoryStore>(host.History);
            var tokens = new InMemoryTokenStore();
            await tokens.SaveAsync(new StoredToken("test-only", host.Clock.UtcNow.UtcDateTime.AddHours(8), "test@example.invalid"));
            services.AddSingleton<ITokenStore>(tokens);
            services.AddSingleton(new DashboardClient(new HttpClient(host.Http) { BaseAddress = new Uri("https://localhost/") }));
            services.AddScoped<DaySendService>();
            services.AddSingleton(new PreferencesStore(null));
            services.AddSingleton<WidgetSession>();
            services.AddSingleton<CheckInScheduler>();
            services.AddSingleton<SnoozeManager>();
            services.AddSingleton<TimeLogService>();
            services.AddSingleton<CsvExportService>();
            services.AddSingleton<UnsentDayReminder>();
            services.AddSingleton(provider => new StartupService(host.Startup, @"C:\Test\TimePlanner.Widget.exe",
                provider.GetRequiredService<ILogger<StartupService>>()));
            services.AddSingleton<WidgetFlow>();
            host.Services = services.BuildServiceProvider();
            using (var scope = host.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
            await host.Flow.LoadAsync();
            return host;
        }

        public async Task SeedEntryAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TimeEntries.Add(new TimeEntry
            {
                UserId = Flow.Session.User.UserId,
                Task = new WorkTask
                {
                    Name = "Test task", CategoryId = 1, AssignedUserID = Flow.Session.User.UserId,
                    Project = new Project { Name = "Test project", Company = new Company { Name = "Test client" } }
                },
                StartTime = Clock.LocalNow(), EndTime = Clock.LocalNow().AddMinutes(30)
            });
            await db.SaveChangesAsync();
        }

        public void Dispose()
        {
            Services.Dispose();
            _connection.Dispose();
            Http.Dispose();
        }
    }
}
