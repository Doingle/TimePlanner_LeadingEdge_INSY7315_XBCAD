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
using TimePlanner.Widget.Controls;
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

    internal static async Task OnUi(Func<Task> test) => await (await Ui.Value).InvokeAsync(test).Task.Unwrap();

    [Theory]
    [InlineData(90, 30, 20, 10, 10)]
    [InlineData(5, 60, 50, 10, 10)]
    [InlineData(20, 15, 15, 12, 0)]
    [InlineData(20, 15, 5, 12, 10)]
    public Task IntervalChangeImmediatelyUpdatesTimer(int oldMinutes, int newMinutes, int remainingMinutes, int localHour, int elapsedMinutes) => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        host.Clock.UtcNow = new DateTimeOffset(2026, 10, 23, localHour - 2, 15, 0, TimeSpan.Zero);
        await host.Flow.ChangeIntervalAsync(oldMinutes);
        await host.Flow.StartTrackingAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(elapsedMinutes);
        await host.Flow.Scheduler.RefreshAsync();
        host.Flow.ShowSettings();
        var settings = Assert.IsType<SettingsView>(host.Window.Screen);
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnTick(object? sender, EventArgs e) => updated.TrySetResult();
        host.Flow.Scheduler.Ticked += OnTick;
        var periodStart = host.Flow.GetLogPeriod().Start;
        ((ComboBox)settings.FindName("IntervalSelect")).SelectedIndex = CheckInPolicy.ChoicesIncluding(oldMinutes).IndexOf(newMinutes);
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Flow.Scheduler.Ticked -= OnTick;
        Assert.Equal(TimeSpan.FromMinutes(remainingMinutes), host.Flow.Scheduler.TimeLeft);
        Assert.Equal(periodStart, host.Flow.GetLogPeriod().Start);
        using (var scope = host.Services.CreateScope())
            Assert.Equal(newMinutes, (await scope.ServiceProvider.GetRequiredService<SettingsService>().GetAsync(host.Flow.Session.User.UserId)).CheckInIntervalMinutes);
        host.Flow.ShowTimer();
        Assert.IsType<TimerView>(host.Window.Screen);
    });

    [Fact]
    public Task TrayActivatesEvenWhenCheckInIsDue() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        host.Clock.UtcNow += TimeSpan.FromHours(3);
        await host.Flow.Scheduler.RefreshAsync();
        Assert.True(host.Flow.Scheduler.IsDue);
        host.Flow.ShowFromTray();
        Assert.IsType<CheckInAlertView>(host.Window.Screen);
        Assert.True(host.Window.Activated);
    });

    [Theory]
    [InlineData(IdleShape.Pill)]
    [InlineData(IdleShape.Ring)]
    [InlineData(IdleShape.Dot)]
    public Task IdleHoverFillRenders_AndTrayAlwaysOpensMainWidget(IdleShape shape) => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        host.Flow.Session.Preferences.IdleShape = shape;
        await host.Flow.StartTrackingAsync();
        var idle = Assert.IsType<IdleView>(host.Window.Screen);
        var root = (FrameworkElement)idle.FindName("Root");
        root.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
        { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
        Assert.Same(idle, host.Window.Screen);
        var fill = (TimePlanner.Widget.Controls.HoverProgress)idle.FindName("HoverFill");
        fill.Fraction = 0.5;
        var preview = new Grid { Width = 160, Height = 120 };
        preview.Children.Add(idle);
        Render(preview, $"idle-hover-{shape}");
        preview.Children.Remove(idle);
        root.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
        { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
        Assert.Equal(0, fill.Fraction);
        host.Flow.ShowFromTray();
        Assert.IsType<TimerView>(host.Window.Screen);
        host.Flow.Session.Preferences.IdleVisibility = IdleVisibility.Hidden;
        host.Flow.ShowIdle();
        host.Flow.ShowFromTray();
        Assert.IsType<TimerView>(host.Window.Screen);
    });

    [Fact]
    public Task TrayActionsAreOrderedAndLogAvailabilityRefreshes() => OnUi(async () =>
    {
        var calls = new List<string>();
        var running = false;
        var menu = TrayIconManager.CreateMenu(() => calls.Add("open"), () => calls.Add("log"),
            () => calls.Add("timesheet"), () => calls.Add("settings"), () => calls.Add("help"), () => calls.Add("exit"), () => running);
        var items = menu.Items.OfType<MenuItem>().ToArray();
        var slotSize = (double)Application.Current.FindResource("SizeIconSm");
        Assert.All(items, item =>
        {
            var icon = Assert.IsType<TimePlanner.Widget.Controls.Icon>(item.Icon);
            Assert.Equal(slotSize, icon.Size);
        });
        Assert.Equal(TimePlanner.Widget.Controls.IconKind.Edit, ((TimePlanner.Widget.Controls.Icon)items[1].Icon).Kind);
        Assert.Equal(TimePlanner.Widget.Controls.IconKind.List, ((TimePlanner.Widget.Controls.Icon)items[2].Icon).Kind);
        Assert.IsType<Separator>(menu.Items[4]);
        Assert.IsType<Separator>(menu.Items[6]);
        Assert.Equal(new[] { "Open Time Planner", "Log time since last check-in", "Timesheet", "Settings", "Help", "Exit" },
            items.Select(item => item.Header.ToString()));
        Assert.False(items[1].IsEnabled);
        running = true;
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        Assert.True(items[1].IsEnabled);
        foreach (var item in items) item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(new[] { "open", "log", "timesheet", "settings", "help", "exit" }, calls);
        await Task.CompletedTask;
    });

    [Fact]
    public Task HelpStaysOpenWhenCheckInBecomesDue_AndBackShowsCheckIn() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        var start = host.Flow.GetLogPeriod().Start;
        host.Flow.ShowHelp();
        var help = Assert.IsType<HelpView>(host.Window.Screen);
        Assert.True(host.Window.Activated);
        host.Flow.ShowHelp();
        Assert.Same(help, host.Window.Screen);
        host.Clock.UtcNow += TimeSpan.FromHours(3);
        await host.Flow.Scheduler.RefreshAsync();
        Assert.True(host.Flow.Scheduler.IsDue);
        Assert.Same(help, host.Window.Screen);
        ((Button)help.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsType<CheckInAlertView>(host.Window.Screen);
        Assert.Equal(start, host.Flow.GetLogPeriod().Start);
        Assert.True(host.Flow.Scheduler.IsRunning);
        Assert.Equal(0, host.Http.Requests);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HelpFromSetupAndSettingsReturnsToSameView(bool settings) => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        if (settings) host.Flow.ShowSettings(); else host.Flow.ShowSetup();
        var original = Assert.IsAssignableFrom<SetupView>(host.Window.Screen);
        ((Button)original.FindName(settings ? "SettingsHelpButton" : "HelpButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var help = Assert.IsType<HelpView>(host.Window.Screen);
        ((Button)help.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(original, host.Window.Screen);
        Assert.False(host.Flow.Scheduler.IsRunning);
        Assert.Equal(0, host.Http.Requests);
    });

    [Fact]
    public Task HelpAnswersExpandAndScrollWithinTheCard() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        host.Flow.ShowHelp();
        var help = Assert.IsType<HelpView>(host.Window.Screen);
        Render(help, "help");
        var questions = (ItemsControl)help.FindName("Questions");
        var container = (ContentPresenter)questions.ItemContainerGenerator.ContainerFromIndex(1);
        var expander = Assert.IsType<Expander>(VisualTreeHelper.GetChild(container, 0));
        var peer = new System.Windows.Automation.Peers.ExpanderAutomationPeer(expander);
        var provider = (System.Windows.Automation.Provider.IExpandCollapseProvider)peer;
        Assert.False(expander.IsExpanded);
        provider.Expand();
        help.UpdateLayout();
        Assert.True(expander.IsExpanded);
        var answer = (Border)expander.Template.FindName("AnswerPanel", expander);
        Assert.Equal(Visibility.Visible, answer.Visibility);
        Assert.True(answer.ActualWidth > 0);
        var scroll = (ScrollViewer)help.FindName("QuestionsScroll");
        Assert.True(scroll.ScrollableHeight > 0);
        Assert.True(scroll.ActualHeight <= scroll.MaxHeight);
        provider.Collapse();
        help.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, answer.Visibility);

        provider.Expand();
        host.Flow.ShowSetup();
        host.Flow.ShowHelp();
        var reopened = Assert.IsType<HelpView>(host.Window.Screen);
        Render(reopened, "help-reopened");
        var reopenedQuestions = (ItemsControl)reopened.FindName("Questions");
        var reopenedContainer = (ContentPresenter)reopenedQuestions.ItemContainerGenerator.ContainerFromIndex(1);
        Assert.False(((Expander)VisualTreeHelper.GetChild(reopenedContainer, 0)).IsExpanded);
    });

    [Fact]
    public Task ExitAndCancelPreserveOpenDay_AndRestartRestoresLoggingRange() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        host.Flow.ShowTimer();
        var timer = Assert.IsType<TimerView>(host.Window.Screen);
        var start = host.Flow.GetLogPeriod().Start;
        Assert.Equal($"Log time since {Format.Clock(start)}", ((Button)timer.FindName("LogButton")).Content);
        Render(timer, "timer-feedback");
        ((Button)timer.FindName("ExitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var confirmation = Assert.IsType<ExitView>(host.Window.Screen);
        Assert.Contains("Your day stays open. You can log it later.", ((TextBlock)confirmation.FindName("Explanation")).Text);
        Render(confirmation, "exit-feedback");
        ((Button)confirmation.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(timer, host.Window.Screen);

        var exited = false;
        host.Flow.AskExit(() => exited = true);
        confirmation = Assert.IsType<ExitView>(host.Window.Screen);
        ((Button)confirmation.FindName("ExitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(exited);
        Assert.True(host.Flow.Scheduler.IsRunning);
        using (var scope = host.Services.CreateScope())
            Assert.Null((await scope.ServiceProvider.GetRequiredService<AppDbContext>().DaySessions.SingleAsync()).EndedAt);
        host.Flow.Scheduler.Dispose();
        using var restartedScheduler = ActivatorUtilities.CreateInstance<CheckInScheduler>(host.Services);
        var restarted = ActivatorUtilities.CreateInstance<WidgetFlow>(host.Services, restartedScheduler);
        await restarted.LoadAsync();
        Assert.True(restartedScheduler.IsRunning);
        Assert.Equal(start, restarted.GetLogPeriod().Start);
        Assert.Equal(0, host.Http.Requests);
    });

    [Fact]
    public Task LogRangeRefreshesAfterEntry_AndTrayUsesExistingLogScreen() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.LogFromTrayAsync();
        Assert.IsType<SetupView>(host.Window.Screen);
        await host.Flow.StartTrackingAsync();
        host.Flow.ShowTimer();
        var timer = Assert.IsType<TimerView>(host.Window.Screen);
        timer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        try
        {
            await host.SeedEntryAsync();
            host.Clock.UtcNow += TimeSpan.FromMinutes(30);
            await host.Flow.Scheduler.LoggedAsync();
            Assert.Equal($"Log time since {Format.Clock(host.Flow.GetLogPeriod().Start)}", ((Button)timer.FindName("LogButton")).Content);
            await host.Flow.LogFromTrayAsync();
            Assert.IsType<LogEntryView>(host.Window.Screen);
            host.Flow.ShowSettings();
            var settings = Assert.IsType<SettingsView>(host.Window.Screen);
            host.Flow.AskExit(() => { });
            ((Button)((ExitView)host.Window.Screen!).FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(settings, host.Window.Screen);
        }
        finally { timer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    });

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task UpdateCheckAtSignInNeverBlocksSendDay(bool slowCheck) => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        host.Http.StallVersionCheck = slowCheck;
        using var checker = new WidgetUpdateChecker(host.Services.GetRequiredService<DashboardClient>(), "1.0.0");
        Task? check = null;
        host.Flow.DashboardSignedIn += (_, _) => check = checker.CheckAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.EndDayAsync();
        var login = await host.Flow.SignInToDashboardAsync("test@example.invalid", "test-only").WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(SignInStatus.SignedIn, login.Status);
        Assert.NotNull(check);
        if (slowCheck)
            Assert.False(check.IsCompleted);
        else
            Assert.NotNull(checker.Available);
        var sent = await host.Flow.SendDayAsync(DateOnly.FromDateTime(host.Clock.LocalNow())).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(SendStatus.Sent, sent.Status);
        Assert.Single(host.History.Days);
    });

    [Fact]
    public Task StartupOverlayFitsExistingSetup() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        var setup = host.Flow.ShowSetup();
        var layers = new Grid { Width = 320 };
        layers.Children.Add(setup);
        layers.Children.Add(new UpdateAvailableView(() => { }));
        Render(layers, "update-startup");
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

    [Fact]
    public Task ManageListsRenameWarnsAboutSentDays_AndHidingKeepsLoggedTime() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.EndDayAsync();
        Assert.Equal(SendStatus.Sent, (await host.Flow.SendDayAsync(host.Flow.Reminder.Today)).Status);

        var view = await host.Flow.ShowManageListsAsync();
        Assert.Same(view, host.Window.Screen);
        var rows = (Panel)view.FindName("Rows");
        var lists = await host.Flow.GetManagedListsAsync();
        var client = lists.Clients.Single(c => c.Name == "Test client");
        var project = client.Projects.Single();

        // Opening rename warns that the sent day keeps the old name on the dashboard
        Click(Descendants<Button>(rows).Single(b => b.Tag is ValueTuple<ListKind, int> key && key == (ListKind.Client, client.Id)));
        await WaitUntil(() => Descendants<TextBlock>(rows).Any(t => t.Text.StartsWith("Used on a day you already sent")));

        Descendants<TextBox>(rows).Single().Text = "Renamed client";
        Click(Descendants<Button>(rows).Single(b => Equals(b.Content, "Save")));
        await WaitUntil(() => ((TextBlock)view.FindName("NoticeText")).Text.Length > 0);
        Assert.Equal("Renamed to Renamed client. Send that day again.", ((TextBlock)view.FindName("NoticeText")).Text);
        Assert.Contains(Descendants<TextBlock>(rows), t => t.Text == "Renamed client");

        // Hiding takes the project out of the pickers but never deletes it or its time
        Assert.True((await host.Flow.SetListItemHiddenAsync(ListKind.Project, project.Id, hidden: true)).Ok);
        Assert.DoesNotContain(await host.Services.GetRequiredService<TimeLogService>().GetProjectsAsync(), p => p.ProjectID == project.Id);
        var day = await host.Services.GetRequiredService<TimeLogService>().GetDayAsync(host.Flow.Session.User.UserId, host.Clock.LocalNow());
        Assert.Equal("Test project", Assert.Single(day.Entries).Task!.Project!.Name);
        Assert.True((await host.Flow.GetManagedListsAsync()).Clients.Single(c => c.Id == client.Id).Hidden);
    });

    [Fact]
    public Task EndedDay_ThenTheDateChanges_OpensOnStartTodayWithYesterdaysSendNow() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.EndDayAsync();
        Assert.Equal(new CardStatus("Friday 23 Oct", false, "Friday 23 Oct, day ended"), host.Flow.Status);

        // The PC date moves on, and the widget is opened again
        host.Clock.UtcNow += TimeSpan.FromDays(1);
        var (scheduler, flow) = Restart(host);
        using (scheduler)
        {
            await flow.LoadAsync();
            flow.ShowStart();
            var setup = Assert.IsType<SetupView>(host.Window.Screen);
            Assert.Equal("Start today", ((Button)setup.FindName("PrimaryButton")).Content);
            Assert.Equal(new CardStatus("Saturday 24 Oct", false, "Saturday 24 Oct, not started"), flow.Status);
            Assert.Equal(flow.Status, WidgetCard.GetStatus(setup));

            var notice = (UnsentEarlierDayView)setup.FindName("EarlierNotice");
            await WaitUntil(() => notice.Visibility == Visibility.Visible);
            Assert.Equal("Yesterday isn't sent yet", ((TextBlock)notice.FindName("TitleText")).Text);
            Render(setup, "start-today");

            // Resume can't reopen yesterday: today starts as a day of its own
            await flow.ResumeDayAsync();
            Assert.Equal(new CardStatus("Saturday 24 Oct", true, "Saturday 24 Oct, tracking"), flow.Status);
            flow.ShowTimer();
            var timer = Assert.IsType<TimerView>(host.Window.Screen);
            Assert.True(WidgetCard.GetStatus(timer)!.Live);
            Render(timer, "timer-status");

            // Only the time-tracking screens carry the status line
            flow.ShowSettings();
            Assert.Null(WidgetCard.GetStatus(host.Window.Screen!));
            var days = await DaySessionsAsync(host);
            Assert.Equal(2, days.Count);
            Assert.NotNull(days[0].EndedAt);
            Assert.Equal(DateTime.Parse("2026-10-24"), days[1].StartedAt.Date);
            Assert.Null(days[1].EndedAt);
        }
    });

    [Fact]
    public Task DayLeftOpenOvernight_ClosesAtItsLastActivity_AndSaysSo() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        var lastActivity = host.Clock.LocalNow().AddMinutes(30);

        // The widget is closed with the day still running, and opened the next morning
        host.Clock.UtcNow += TimeSpan.FromDays(1);
        var (scheduler, flow) = Restart(host);
        using (scheduler)
        {
            await flow.LoadAsync();
            flow.ShowStart();
            var ended = Assert.IsType<DayEndedView>(host.Window.Screen);
            Assert.Equal(Visibility.Visible, ((Border)ended.FindName("OvernightNote")).Visibility);
            Assert.Equal($"It was left open overnight, so it ended at {Format.Clock(lastActivity)}, your last activity.",
                ((TextBlock)ended.FindName("OvernightText")).Text);
            Assert.Equal(Visibility.Visible, ((StackPanel)ended.FindName("EarlierActions")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((StackPanel)ended.FindName("ExportActions")).Visibility);
            Assert.Equal(lastActivity, (await DaySessionsAsync(host)).Single().EndedAt);
            await WaitUntil(() => ((UnsentEarlierDayView)ended.FindName("EarlierNotice")).Visibility == Visibility.Visible);
            Render(ended, "day-ended-overnight");

            await flow.StartTrackingAsync();
            Assert.Null(flow.Scheduler.ClosedOvernight);
            Assert.Equal(2, (await DaySessionsAsync(host)).Count);
        }
    });

    [Fact]
    public Task TrackingPastMidnight_ClosesTheDay_AndShowsDayEnded() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();

        host.Clock.UtcNow += TimeSpan.FromDays(1);
        await host.Flow.Scheduler.RefreshAsync();

        await WaitUntil(() => host.Window.Screen is DayEndedView);
        var ended = (DayEndedView)host.Window.Screen!;
        Assert.Equal(Visibility.Visible, ((Border)ended.FindName("OvernightNote")).Visibility);
        Assert.False(host.Flow.Scheduler.IsRunning);
        Assert.Equal("Saturday 24 Oct, not started", WidgetCard.GetStatus(ended)?.Spoken);
    });

    [Fact]
    public Task DayEndedLeftOpenPastMidnight_SwapsResumeForStartToday() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.EndDayAsync();
        var ended = Assert.IsType<DayEndedView>(host.Window.Screen);
        ended.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.Equal(Visibility.Visible, ((StackPanel)ended.FindName("ExportActions")).Visibility);

        host.Clock.UtcNow += TimeSpan.FromDays(1);
        await host.Flow.Scheduler.RefreshAsync();

        Assert.Equal(Visibility.Collapsed, ((StackPanel)ended.FindName("ExportActions")).Visibility);
        Assert.Equal(Visibility.Visible, ((StackPanel)ended.FindName("EarlierActions")).Visibility);
    });

    [Fact]
    public Task PastDayTimesheet_SaysItCanStillBeEdited_AndSendsAgain() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.EndDayAsync();
        var friday = host.Flow.Reminder.Today;
        host.Clock.UtcNow += TimeSpan.FromDays(1);

        await host.Flow.ShowTimesheetAsync(friday, () => { });
        var sheet = Assert.IsType<TimesheetReviewView>(host.Window.Screen);
        Assert.Equal(Visibility.Visible, ((TextBlock)sheet.FindName("PastDayHelp")).Visibility);
        Assert.Equal(Visibility.Visible, ((Button)sheet.FindName("TodayButton")).Visibility);
        Assert.Equal("Send day", ((Button)sheet.FindName("SendButton")).Content);
        Assert.Equal("Yesterday, Friday 23 Oct · 30m", ((TextBlock)sheet.FindName("Subtitle")).Text);
        Render(sheet, "timesheet-yesterday");

        host.History.Days.Add(new SentDay(friday, host.Clock.LocalNow(), 1, 0.5));
        await host.Flow.ShowTimesheetAsync(friday, () => { });
        Assert.Equal("Send again", ((Button)((TimesheetReviewView)host.Window.Screen!).FindName("SendButton")).Content);

        await host.Flow.ShowTimesheetAsync(DateOnly.FromDateTime(host.Clock.LocalNow()), () => { });
        Assert.Equal(Visibility.Collapsed, ((Button)((TimesheetReviewView)host.Window.Screen!).FindName("TodayButton")).Visibility);
    });

    // A new scheduler and flow over the same in-memory database, as when the widget is opened again
    private static (CheckInScheduler Scheduler, WidgetFlow Flow) Restart(TestHost host)
    {
        host.Flow.Scheduler.Dispose();
        var scheduler = ActivatorUtilities.CreateInstance<CheckInScheduler>(host.Services);
        return (scheduler, ActivatorUtilities.CreateInstance<WidgetFlow>(host.Services, scheduler));
    }

    private static async Task<List<DaySession>> DaySessionsAsync(TestHost host)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DaySessions.AsNoTracking().OrderBy(d => d.StartedAt).ToListAsync();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    private static async Task WaitUntil(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
            await Task.Delay(20);
        Assert.True(done());
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;
            foreach (var below in Descendants<T>(child))
                yield return below;
        }
    }

    private sealed class RecordingHost : IWidgetHost
    {
        public bool Activated { get; private set; }
        public FrameworkElement? Screen { get; private set; }
        public void Present(FrameworkElement screen, bool activate = true)
        {
            Activated = activate;
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
        public bool StallVersionCheck { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/widget/version")
                return StallVersionCheck ? WaitForCancellation(cancellationToken) : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new WidgetRelease("2.0.0")) });
            if (request.RequestUri.AbsolutePath == "/api/v1/auth/login")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new { accessToken = "test-only", tokenType = "Bearer", expiresAtUtc = DateTime.UtcNow.AddHours(1) }) });
            Requests++;
            Assert.Equal("/api/v1/timesheets/import", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(Succeeds ? HttpStatusCode.OK : HttpStatusCode.InternalServerError)
            {
                Content = JsonContent.Create(new { created = 1, skipped = 0, errors = Array.Empty<string>() })
            });
        }

        private static async Task<HttpResponseMessage> WaitForCancellation(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The test request must be cancelled.");
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
