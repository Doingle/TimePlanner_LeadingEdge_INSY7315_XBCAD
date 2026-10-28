using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TimePlanner.Core.Ai;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Extensions;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Sync;
using TimePlanner.Widget.Controls;
using TimePlanner.Widget.Models;
using TimePlanner.Widget.Services;
using TimePlanner.Widget.Services.Ai;
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
    public Task LargerText_IsSavedAndAppliedStraightAway() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        Assert.False(host.Flow.Session.Preferences.LargerText);

        host.Flow.ChangeLargerText(true);
        Assert.True(host.Flow.Session.Preferences.LargerText);
        Assert.Equal(1, host.Window.SizeApplied);

        host.Flow.ChangeLargerText(false);
        Assert.False(host.Flow.Session.Preferences.LargerText);
        Assert.Equal(2, host.Window.SizeApplied);
    });

    [Fact]
    public Task LargeTimer_ShowsAWordUnderEachIconButton_AndKeepsItsWording() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        host.Flow.ShowTimer();
        var timer = Assert.IsType<TimerView>(host.Window.Screen);
        var card = (WidgetCard)timer.FindName("Card");
        var pause = (Button)timer.FindName("PauseButton");

        // Laid out as the window does it: the root carries large view and the 1.5 times scale
        var root = new Grid();
        root.Children.Add(timer);
        var page = new Border { Child = root };
        LayOut(page);
        var words = Descendants<TextBlock>(timer).Where(t => t.Name == "Word").ToList();
        var state = (TextBlock)card.Template.FindName("StatusState", card);
        // Voice is off here, so the speak button stays hidden
        Assert.Equal(new[] { "Exit", "Pause", "Settings" }, words.Select(w => w.Text).Order());
        Assert.All(words, w => Assert.Equal(Visibility.Collapsed, w.Visibility));
        Assert.Equal(Visibility.Collapsed, state.Visibility);
        Assert.Equal(40, pause.ActualHeight);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        RenderElement(page, "timer-normal");

        WidgetCard.SetIsLarge(root, true);
        root.LayoutTransform = new ScaleTransform(WidgetCard.LargeScale, WidgetCard.LargeScale);
        LayOut(page);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.All(words, w => Assert.Equal(Visibility.Visible, w.Visibility));
        Assert.Equal(Visibility.Visible, state.Visibility);
        Assert.Equal(" · Tracking", string.Concat(state.Inlines.OfType<Run>().Select(r => r.Text)));
        Assert.Equal("until next check-in", ((TextBlock)timer.FindName("StatusText")).Text);
        Assert.Equal("Pause the timer", pause.ToolTip);
        Assert.Equal(WidgetCard.LargeScale * card.ActualWidth, page.ActualWidth, precision: 0);
        // The timer stays on one line with the pause circle, as at the normal size; the word hangs under the circle
        var timerText = (TextBlock)timer.FindName("TimerText");
        var circleCentre = pause.TranslatePoint(new Point(0, (double)Application.Current.FindResource("SizeControl") / 2), page).Y;
        var timerCentre = timerText.TranslatePoint(new Point(0, timerText.ActualHeight / 2), page).Y;
        Assert.InRange(timerCentre - circleCentre, -2, 2);
        // A fresh layout for the picture, so nothing from the normal size is left in it
        page.Child = null;
        page.Child = root;
        LayOut(page);
        RenderElement(page, "timer-large");

        // Paused, the same button says Resume and keeps its place
        var circleLeft = pause.TranslatePoint(new Point(), page).X;
        Click(pause);
        await WaitUntil(() => WidgetCard.GetWord(pause) == "Resume");
        LayOut(page);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(circleLeft, pause.TranslatePoint(new Point(), page).X);
        Assert.Equal("Resume the timer", pause.ToolTip);
        RenderElement(page, "timer-paused-large");
    });

    [Fact]
    public Task LargeView_EveryIconButtonShowsItsWord_OnlyInLargeView() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        var today = DateOnly.FromDateTime(host.Clock.LocalNow());

        // The screens with icon buttons; Day ended last, as it ends the day
        var screens = new (string Name, Func<Task> Show)[]
        {
            ("settings", () => { host.Flow.ShowSettings(); return Task.CompletedTask; }),
            ("more-options", () => { host.Flow.ShowMoreOptions(fromSettings: true); return Task.CompletedTask; }),
            ("working-days", () => { host.Flow.ShowWorkingDays(() => { }); return Task.CompletedTask; }),
            ("help", () => { host.Flow.ShowHelp(); return Task.CompletedTask; }),
            ("manage-lists", () => host.Flow.ShowManageListsAsync()),
            ("check-in", () => { host.Flow.CheckIn(); return Task.CompletedTask; }),
            ("log-entry", () => host.Flow.LogTimeAsync(LogEntryMode.CheckIn)),
            ("end-day", () => { host.Flow.AskEndDay(); return Task.CompletedTask; }),
            ("sign-in", () => { host.Flow.ShowSignIn(() => Task.CompletedTask, () => { }); return Task.CompletedTask; }),
            ("timesheet", () => host.Flow.ShowTimesheetAsync(today, () => { })),
            ("entry-edit", () => host.Flow.ShowEntryEditorAsync(today, null, host.Clock.LocalNow().AddMinutes(-20), host.Clock.LocalNow(), () => { })),
            ("day-ended", () => host.Flow.EndDayAsync()),
        };

        var seen = new List<string>();
        foreach (var (name, show) in screens)
        {
            await show();
            var screen = host.Window.Screen!;
            var root = new Grid();
            root.Children.Add(screen);
            var page = new Border { Child = root };
            LayOut(page);
            Assert.All(Descendants<TextBlock>(page).Where(t => t.Name == "Word"), w => Assert.Equal(Visibility.Collapsed, w.Visibility));

            WidgetCard.SetIsLarge(root, true);
            root.LayoutTransform = new ScaleTransform(WidgetCard.LargeScale, WidgetCard.LargeScale);
            LayOut(page);

            // Every icon-only button on show says what it does, in a word beside or under its icon
            var iconButtons = Descendants<Button>(page).Where(b => b.Content is Icon && Shown(b, page)).ToList();
            seen.Add($"{name}: {string.Join(", ", iconButtons.Select(WidgetCard.GetWord))}");
            Assert.All(iconButtons, b =>
            {
                Assert.False(string.IsNullOrWhiteSpace(WidgetCard.GetWord(b)), $"{name}: {b.ToolTip} has no word");
                var word = Assert.Single(Descendants<TextBlock>(b), t => t.Name == "Word");
                Assert.Equal(Visibility.Visible, word.Visibility);
                Assert.Equal(WidgetCard.GetWord(b), word.Text);
            });

            page.Child = null;
            root.Children.Clear();
            page.Child = new Grid { LayoutTransform = root.LayoutTransform, Children = { screen } };
            WidgetCard.SetIsLarge(page.Child, true);
            LayOut(page);
            RenderElement(page, $"large-{name}");
            ((Grid)page.Child).Children.Clear();
        }

        // Answering a check-in, Log entry has no close button to show
        Assert.Equal(
            "settings: Back | more-options: Back | working-days: Back | help: Back | manage-lists: Back, Add, Edit, Add, Edit"
            + " | check-in: Close | log-entry:  | end-day: Close | sign-in: Close | timesheet: Back, Previous, Next, Edit, Delete"
            + " | entry-edit: Close | day-ended: Close",
            string.Join(" | ", seen));
    });

    [Fact]
    public Task LargeView_DropdownsAndMenusTakeThePopupScale() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();

        // What the window does when large view comes on (screens in the window hear of the change; these are built after it)
        Application.Current.Resources["PopupScale"] = new ScaleTransform(WidgetCard.LargeScale, WidgetCard.LargeScale);
        try
        {
            host.Flow.ShowSettings();
            var settings = Assert.IsType<SettingsView>(host.Window.Screen);
            Render(settings, "settings-popup-scale");
            var interval = (ComboBox)settings.FindName("IntervalSelect");
            var popup = (Popup)interval.Template.FindName("PART_Popup", interval);
            var menu = new ContextMenu { Style = (Style)Application.Current.FindResource("Menu.Dropdown") };
            Assert.Equal(WidgetCard.LargeScale, Assert.IsType<ScaleTransform>(((FrameworkElement)popup.Child).LayoutTransform).ScaleX);
            Assert.Equal(WidgetCard.LargeScale, Assert.IsType<ScaleTransform>(menu.LayoutTransform).ScaleX);
        }
        finally
        {
            Application.Current.Resources.Remove("PopupScale");
        }

        // Normal size: the theme's own PopupScale, no change at all
        var plain = new ContextMenu { Style = (Style)Application.Current.FindResource("Menu.Dropdown") };
        Assert.Equal(1, Assert.IsType<ScaleTransform>(plain.LayoutTransform).ScaleX);
    });

    // Shown on the page: it and everything above it up to the page are visible
    private static bool Shown(FrameworkElement element, DependencyObject page)
    {
        for (DependencyObject? at = element; at != null && at != page; at = VisualTreeHelper.GetParent(at))
            if (at is UIElement { Visibility: not Visibility.Visible })
                return false;
        return true;
    }

    // Measures and arranges an element at its own size, with room for large view
    private static void LayOut(FrameworkElement element)
    {
        element.Measure(new Size(800, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
    }

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

    private static async Task WaitFor(Func<bool> condition) => await WaitFor(() => Task.FromResult(condition()));

    //the save and the undo run after the click, so the test waits for their result rather than for the click to return
    private static async Task WaitFor(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100 && !await condition(); i++)
            await Task.Delay(30);
        Assert.True(await condition(), "The screen did not update in time.");
    }

    private static Button? FindButton(DependencyObject root, string content)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button button && Equals(button.Content, content))
                return button;
            if (FindButton(child, content) is { } found)
                return found;
        }
        return null;
    }

    [Fact]
    public Task EditThatChangesAnotherEntry_AsksFirst_SavesBothOnYes_AndUndoPutsBothBack() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        var userId = host.Flow.Session.User.UserId;
        var day = DateOnly.FromDateTime(host.Clock.LocalNow());
        DateTime At(int hour, int minute) => day.ToDateTime(new TimeOnly(hour, minute));
        string[] project = ["Client A", "Project A"];
        var first = (await host.Flow.AddEntryAsync(new EntryEdit(At(8, 0), At(9, 0), project, ["Coding"], "first"))).EntryId!.Value;
        var second = (await host.Flow.AddEntryAsync(new EntryEdit(At(9, 0), At(9, 30), project, ["Coding"], "second"))).EntryId!.Value;

        async Task<(DateTime Start, DateTime End)> TimesOf(int id)
        {
            var slot = (await host.Flow.GetTimesheetAsync(userId, day)).Single(s => s.EntryId == id);
            return (slot.Start, slot.End);
        }

        var slot = (await host.Flow.GetTimesheetAsync(userId, day)).Single(s => s.EntryId == first);
        await host.Flow.ShowEntryEditorAsync(day, slot, slot.Start, slot.End, () => { });
        var editor = Assert.IsType<EntryEditView>(host.Window.Screen);
        var banner = (Border)editor.FindName("ConfirmBanner");
        ((TextBox)editor.FindName("EndBox")).Text = "08:45";

        //saving asks first and stores nothing yet
        Click((Button)editor.FindName("SaveButton"));
        await WaitFor(() => banner.Visibility == Visibility.Visible);
        Assert.Equal("This changes Coding · Project A to 8:45 AM–9:30 AM. Continue?", ((TextBlock)editor.FindName("BannerText")).Text);
        Assert.Equal((At(8, 0), At(9, 0)), await TimesOf(first));

        //No keeps the editor open with nothing saved
        Click(FindButton(banner, "No")!);
        Assert.Equal(Visibility.Collapsed, banner.Visibility);
        Assert.Same(editor, host.Window.Screen);
        Assert.Equal((At(9, 0), At(9, 30)), await TimesOf(second));

        //Yes saves both together and goes back to the timesheet
        Click((Button)editor.FindName("SaveButton"));
        await WaitFor(() => banner.Visibility == Visibility.Visible);
        Click(FindButton(banner, "Yes")!);
        await WaitFor(async () => await TimesOf(first) == (At(8, 0), At(8, 45)));
        Assert.Equal((At(8, 0), At(8, 45)), await TimesOf(first));
        Assert.Equal((At(8, 45), At(9, 30)), await TimesOf(second));

        //the timesheet offers an undo that reverses both
        await host.Flow.ShowTimesheetAsync(day, () => { });
        var timesheet = Assert.IsType<TimesheetReviewView>(host.Window.Screen);
        var undoBar = (Grid)timesheet.FindName("UndoBar");
        Assert.Equal(Visibility.Visible, undoBar.Visibility);
        Assert.Equal("Entry updated · other entries changed", ((TextBlock)timesheet.FindName("UndoText")).Text);
        Click(FindButton(undoBar, "Undo")!);
        await WaitFor(async () => await TimesOf(first) == (At(8, 0), At(9, 0)));
        Assert.Equal((At(9, 0), At(9, 30)), await TimesOf(second));
    });

    [Fact]
    public Task Timesheet_MaximiseOpensTheWideWindow_RestoreComesBack_AndTheChoiceIsRemembered() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        var shown = new List<Window>();
        host.Flow.ShowWindow = shown.Add;
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        var today = DateOnly.FromDateTime(host.Clock.LocalNow());
        var backs = 0;
        void Back()
        {
            backs++;
            host.Flow.ShowTimer();
        }

        // The small page carries the status line, and opens first
        await host.Flow.ShowTimesheetAsync(today, Back);
        var small = Assert.IsType<TimesheetReviewView>(host.Window.Screen);
        Assert.Equal(host.Flow.Status, WidgetCard.GetStatus(small));
        Assert.Equal("Not sent yet", ((TextBlock)small.FindName("BadgeText")).Text);

        // Maximise opens the wide window on the same day; the widget goes back to where the timesheet was opened from
        Click((Button)small.FindName("MaximiseButton"));
        await WaitFor(() => shown.Count == 1);
        var wide = Assert.IsType<TimesheetWindow>(shown[0]);
        Assert.True(host.Flow.Session.Preferences.TimesheetWide);
        Assert.Equal(1, backs);
        Assert.IsType<TimerView>(host.Window.Screen);
        Assert.Equal(Format.Date(today), ((TextBlock)wide.FindName("DayText")).Text);
        Assert.Single(((StackPanel)wide.FindName("Rows")).Children);

        // From anywhere else, such as the tray, it now opens wide too, and the widget stays as it is
        await host.Flow.ShowTimesheetAsync(today, host.Flow.ShowFromTray);
        Assert.Equal(2, shown.Count);
        Assert.Same(wide, shown[1]);
        Assert.IsType<TimerView>(host.Window.Screen);

        // Restore: the small page in the widget again, on the same day, and that is how it opens from now on
        Click((Button)wide.FindName("RestoreButton"));
        await WaitFor(() => host.Window.Screen is TimesheetReviewView);
        Assert.False(host.Flow.Session.Preferences.TimesheetWide);
        await host.Flow.ShowTimesheetAsync(today, Back);
        Assert.Equal(2, shown.Count);
    });

    [Fact]
    public Task WideTimesheet_EditsInPlace_AsksBeforeChangingANeighbour_AndUndoPutsBothBack() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        var shown = new List<Window>();
        host.Flow.ShowWindow = shown.Add;
        var userId = host.Flow.Session.User.UserId;
        var day = DateOnly.FromDateTime(host.Clock.LocalNow());
        DateTime At(int hour, int minute) => day.ToDateTime(new TimeOnly(hour, minute));
        string[] project = ["Client A", "Project A"];
        var first = (await host.Flow.AddEntryAsync(new EntryEdit(At(8, 0), At(9, 0), project, ["Coding"], "first"))).EntryId!.Value;
        var second = (await host.Flow.AddEntryAsync(new EntryEdit(At(9, 0), At(9, 30), project, ["Coding"], null))).EntryId!.Value;
        // The test clock reads 10:00, and time can't be logged ahead of it
        Assert.True((await host.Flow.AddEntryAsync(new EntryEdit(At(9, 30), At(9, 50), project, ["Coding"], "Weekly plan"))).Ok);

        async Task<(DateTime Start, DateTime End)> TimesOf(int id)
        {
            var slot = (await host.Flow.GetTimesheetAsync(userId, day)).Single(s => s.EntryId == id);
            return (slot.Start, slot.End);
        }

        await host.Flow.ShowWideTimesheetAsync(day, () => { });
        var wide = Assert.IsType<TimesheetWindow>(shown.Single());
        var rows = (StackPanel)wide.FindName("Rows");
        Assert.Equal(3, rows.Children.Count);
        RenderWindow(wide, "wide-timesheet", large: false);
        wide.ApplySize(true);
        RenderWindow(wide, "wide-timesheet-large", large: true);

        // Edit turns the row into its boxes and pickers, with Save and Cancel in place of Edit and Delete
        Click(Descendants<Button>(rows.Children[0]).First(b => WidgetCard.GetWord(b) == "Edit"));
        await WaitFor(() => FindButton(rows, "Save") != null);
        var boxes = Descendants<TextBox>(rows).ToList();
        Assert.Equal(["8:00 AM", "9:00 AM", "first"], boxes.Select(b => b.Text));
        Assert.NotNull(FindButton(rows, "Cancel"));

        // Saving an end that runs into the next entry asks first, and stores nothing yet
        boxes[1].Text = "8:45 AM";
        Click(FindButton(rows, "Save")!);
        static string TextOf(TextBlock block) => new TextRange(block.ContentStart, block.ContentEnd).Text;
        await WaitFor(() => Descendants<TextBlock>(rows).Any(t => TextOf(t).StartsWith("This changes ")));
        var banner = TextOf(Descendants<TextBlock>(rows).First(t => TextOf(t).StartsWith("This changes ")));
        Assert.EndsWith(" to 8:45 – 9:30 AM. Continue?", banner);
        Assert.Equal((At(8, 0), At(9, 0)), await TimesOf(first));
        var yes = FindButton(rows, "Yes")!;
        RenderWindow(wide, "wide-timesheet-banner-large", large: true);

        // Yes saves both together, and offers an undo that reverses both
        Click(yes);
        await WaitFor(async () => await TimesOf(first) == (At(8, 0), At(8, 45)));
        Assert.Equal((At(8, 45), At(9, 30)), await TimesOf(second));
        var undoBar = (Grid)wide.FindName("UndoBar");
        await WaitFor(() => undoBar.Visibility == Visibility.Visible);
        Assert.Equal("Entry updated · other entries changed", ((TextBlock)wide.FindName("UndoText")).Text);
        Click(FindButton(undoBar, "Undo")!);
        await WaitFor(async () => await TimesOf(first) == (At(8, 0), At(9, 0)));
        Assert.Equal((At(9, 0), At(9, 30)), await TimesOf(second));
    });

    // Saves the wide window's content as it would show, at its default size for the scale, when previews are asked for
    private static void RenderWindow(Window window, string name, bool large)
    {
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        var scale = large ? WidgetCard.LargeScale : 1;
        var page = new Border { Width = 920 * scale, Height = 560 * scale, Child = root, Background = Brushes.White };
        page.Measure(new Size(page.Width, page.Height));
        page.Arrange(new Rect(0, 0, page.Width, page.Height));
        page.UpdateLayout();
        RenderElement(page, name);
        page.Child = null;
        window.Content = root;
    }

    [Fact]
    public Task SignIn_AsksForTheCodeOnlyWhenTheDashboardNeedsOne() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        host.Http.RequiresCode = true;
        var signedIn = false;
        host.Flow.ShowSignIn(() => { signedIn = true; return Task.CompletedTask; }, () => { });
        var view = Assert.IsType<SignInView>(host.Window.Screen);
        var codeLine = (FrameworkElement)view.FindName("CodeLine");
        var button = (Button)view.FindName("SignInButton");
        var error = (TextBlock)view.FindName("ErrorText");
        var code = (TextBox)view.FindName("CodeBox");
        Assert.Equal(Visibility.Collapsed, codeLine.Visibility);
        ((TextBox)view.FindName("EmailBox")).Text = "admin@example.com";
        ((PasswordBox)view.FindName("PasswordBox")).Password = "a-password";

        //the password is right but a code is needed: the box appears, nothing is signed in
        Click(button);
        await WaitFor(() => codeLine.Visibility == Visibility.Visible && button.IsEnabled);
        Assert.Contains("two-factor code is required", error.Text);
        Assert.False(signedIn);

        //a wrong code is refused and the box stays
        code.Text = "000000";
        Click(button);
        await WaitFor(() => error.Text.Contains("not right") && button.IsEnabled);
        Assert.False(signedIn);

        code.Text = "123456";
        Click(button);
        await WaitFor(() => signedIn);
    });

    [Fact]
    public Task SignIn_WithoutTwoFactor_NeverShowsTheCodeBox() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        var signedIn = false;
        host.Flow.ShowSignIn(() => { signedIn = true; return Task.CompletedTask; }, () => { });
        var view = Assert.IsType<SignInView>(host.Window.Screen);
        ((TextBox)view.FindName("EmailBox")).Text = "developer@example.com";
        ((PasswordBox)view.FindName("PasswordBox")).Password = "a-password";

        Click((Button)view.FindName("SignInButton"));
        await WaitFor(() => signedIn);

        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("CodeLine")).Visibility);
    });

    [Fact]
    public Task EditThatCoversAnEntry_AsksBeforeRemovingIt() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        var userId = host.Flow.Session.User.UserId;
        var day = DateOnly.FromDateTime(host.Clock.LocalNow());
        DateTime At(int hour, int minute) => day.ToDateTime(new TimeOnly(hour, minute));
        string[] project = ["Client A", "Project A"];
        var first = (await host.Flow.AddEntryAsync(new EntryEdit(At(8, 0), At(9, 0), project, ["Coding"], "first"))).EntryId!.Value;
        await host.Flow.AddEntryAsync(new EntryEdit(At(9, 0), At(9, 30), project, ["Coding"], "second"));

        var slot = (await host.Flow.GetTimesheetAsync(userId, day)).Single(s => s.EntryId == first);
        await host.Flow.ShowEntryEditorAsync(day, slot, slot.Start, slot.End, () => { });
        var editor = Assert.IsType<EntryEditView>(host.Window.Screen);
        var banner = (Border)editor.FindName("ConfirmBanner");
        ((TextBox)editor.FindName("EndBox")).Text = "09:45";

        Click((Button)editor.FindName("SaveButton"));
        await WaitFor(() => banner.Visibility == Visibility.Visible);

        Assert.Equal("This removes Coding · Project A (9:00 AM–9:30 AM). Continue?", ((TextBlock)editor.FindName("BannerText")).Text);
        Assert.Equal(2, (await host.Flow.GetTimesheetAsync(userId, day)).Count(s => s.EntryId != null));
    });

    [Fact]
    public Task SendThatFails_WaitsShowsItInTheTimesheet_AndGoesThroughLater() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.EndDayAsync();
        var day = host.Flow.Reminder.Today;
        var log = host.Services.GetRequiredService<TimeLogService>();
        var userId = host.Flow.Session.User.UserId;

        host.Http.Succeeds = false;
        var failed = await host.Flow.SendDayAsync(day);
        Assert.True(failed.Retryable);
        Assert.Equal(day, Assert.Single(host.Pending.Days).Day);
        Assert.Empty(host.History.Days);

        await host.Flow.ShowTimesheetAsync(day, () => { });
        var view = Assert.IsType<TimesheetReviewView>(host.Window.Screen);
        Assert.Equal("Waiting to send · will retry", ((TextBlock)view.FindName("StatusText")).Text);

        //the retry is not due yet, so a tick does not call the dashboard again
        var requests = host.Http.Requests;
        Assert.Empty(await log.RetryPendingSendsAsync(userId));
        Assert.Equal(requests, host.Http.Requests);

        host.Http.Succeeds = true;
        host.Clock.UtcNow += TimeSpan.FromMinutes(2);
        Assert.Equal(new[] { day }, await log.RetryPendingSendsAsync(userId, force: true));
        Assert.Empty(host.Pending.Days);
        Assert.Single(host.History.Days);
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
        Assert.Equal(new CardStatus("Friday 23 Oct", "Day ended", false), host.Flow.Status);

        // The PC date moves on, and the widget is opened again
        host.Clock.UtcNow += TimeSpan.FromDays(1);
        var (scheduler, flow) = Restart(host);
        using (scheduler)
        {
            await flow.LoadAsync();
            flow.ShowStart();
            var setup = Assert.IsType<SetupView>(host.Window.Screen);
            Assert.Equal("Start today", ((Button)setup.FindName("PrimaryButton")).Content);
            Assert.Equal(new CardStatus("Saturday 24 Oct", "Not started", false), flow.Status);
            Assert.Equal(flow.Status, WidgetCard.GetStatus(setup));

            var notice = (UnsentEarlierDayView)setup.FindName("EarlierNotice");
            await WaitUntil(() => notice.Visibility == Visibility.Visible);
            Assert.Equal("Yesterday isn't sent yet", ((TextBlock)notice.FindName("TitleText")).Text);
            Render(setup, "start-today");

            // Resume can't reopen yesterday: today starts as a day of its own
            await flow.ResumeDayAsync();
            Assert.Equal(new CardStatus("Saturday 24 Oct", "Tracking", true), flow.Status);
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
        // Saturday is a working day here, so Day ended shows straight away
        host.Flow.Session.Preferences.WorkingDays.Add(DayOfWeek.Saturday);
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
        Assert.Equal("Fri 23 Oct", ((TextBlock)sheet.FindName("DayText")).Text);
        Assert.Equal("30m", ((TextBlock)sheet.FindName("TotalText")).Text);
        Assert.Equal("Yesterday, Friday 23 Oct · 30m", ((Button)sheet.FindName("DayButton")).ToolTip);
        Render(sheet, "timesheet-yesterday");

        host.History.Days.Add(new SentDay(friday, host.Clock.LocalNow(), 1, 0.5));
        await host.Flow.ShowTimesheetAsync(friday, () => { });
        Assert.Equal("Send again", ((Button)((TimesheetReviewView)host.Window.Screen!).FindName("SendButton")).Content);

        await host.Flow.ShowTimesheetAsync(DateOnly.FromDateTime(host.Clock.LocalNow()), () => { });
        Assert.Equal(Visibility.Collapsed, ((Button)((TimesheetReviewView)host.Window.Screen!).FindName("TodayButton")).Visibility);
    });

    [Fact]
    public Task TrackingPastMidnightIntoADayOff_GoesQuietlyToTheTray() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        await host.SeedEntryAsync();
        var screen = host.Window.Screen;

        // Friday's day runs on into Saturday, a day off by default
        host.Clock.UtcNow += TimeSpan.FromDays(1);
        await host.Flow.Scheduler.RefreshAsync();
        await WaitUntil(() => host.Window.Hides > 0);

        Assert.Same(screen, host.Window.Screen);
        Assert.False(host.Flow.Scheduler.IsRunning);

        // Opened from the tray, Day ended is waiting
        host.Flow.ShowFromTray();
        var ended = Assert.IsType<DayEndedView>(host.Window.Screen);
        Assert.Equal(Visibility.Visible, ((Border)ended.FindName("OvernightNote")).Visibility);
    });

    [Fact]
    public Task SignInOnADayOff_WaitsInTheTray_AndADayCanStillBeStartedByHand() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        host.Clock.UtcNow += TimeSpan.FromDays(1);
        var (scheduler, flow) = Restart(host);
        using (scheduler)
        {
            await flow.LoadAsync();
            flow.ShowStartAtSignIn();
            Assert.Null(host.Window.Screen);
            Assert.Equal(1, host.Window.Hides);

            // From the tray the day starts as usual, but check-ins only happen on ticked days
            flow.ShowFromTray();
            Assert.IsType<SetupView>(host.Window.Screen);
            await flow.StartTrackingAsync();
            Assert.True(flow.Scheduler.IsRunning);
            Assert.True(flow.Scheduler.IsDayOff);
            host.Clock.UtcNow += TimeSpan.FromMinutes(flow.Session.Settings.CheckInIntervalMinutes * 2);
            await flow.Scheduler.RefreshAsync();
            Assert.IsNotType<CheckInAlertView>(host.Window.Screen);
            Assert.Null(flow.Scheduler.TimeLeft);

            // The timer counts up what isn't logged yet instead
            flow.ShowTimer();
            var timer = Assert.IsType<TimerView>(host.Window.Screen);
            Assert.Equal("03:00", ((TextBlock)timer.FindName("TimerText")).Text);
            Assert.Equal("not logged yet", ((TextBlock)timer.FindName("StatusText")).Text);
            Render(timer, "timer-day-off");
        }
    });

    [Fact]
    public Task SignInOnAWorkingDay_ShowsSetup() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        host.Flow.ShowStartAtSignIn();
        Assert.IsType<SetupView>(host.Window.Screen);
        Assert.Equal(0, host.Window.Hides);
    });

    [Fact]
    public Task Settings_SaveWorkingDaysAndCheckInTiming() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        host.Flow.ShowSettings();
        var settings = Assert.IsType<SettingsView>(host.Window.Screen);
        Assert.Equal(Visibility.Visible, ((Border)settings.FindName("LargerTextRow")).Visibility);
        Render(settings, "settings");

        // Timing is on the main screen, the same drop-down with the tick box as Setup; the working-days screen stays in More options.
        Assert.Equal(Visibility.Visible, ((StackPanel)settings.FindName("MainView")).Visibility);
        var interval = (ComboBox)settings.FindName("IntervalSelect");
        Assert.NotNull(interval.Tag);
        ((CheckBox)settings.FindName("HalfHourBox")).IsChecked = true;
        await WaitUntil(() => host.Flow.Session.Preferences.CheckInTiming == CheckInTiming.OnTheHalfHour);
        Assert.Equal(-1, interval.SelectedIndex);
        Assert.Equal(Visibility.Visible, ((StackPanel)settings.FindName("IntervalField")).Visibility);
        Render(settings, "settings-clock-times");
        interval.SelectedItem = "30m";
        await WaitUntil(() => host.Flow.Session.Preferences.CheckInTiming == CheckInTiming.FromLastCheckIn);
        Assert.False(((CheckBox)settings.FindName("HalfHourBox")).IsChecked);
        settings.ShowAdvanced();
        Assert.Equal("Mon – Fri", ((TextBlock)settings.FindName("WorkingDaysSummary")).Text);
        Render(settings, "more-options");

        // Working days opens its own list, Monday first
        Click((Button)settings.FindName("WorkingDaysRow"));
        var days = Assert.IsType<WorkingDaysView>(host.Window.Screen);
        var boxes = ((Panel)days.FindName("DaysList")).Children.OfType<CheckBox>().ToList();
        Assert.Equal(["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"], boxes.Select(b => (string)b.Content));
        Assert.Equal([true, true, true, true, true, false, false], boxes.Select(b => b.IsChecked == true));
        Assert.Null(WidgetCard.GetStatus(days));
        Render(days, "working-days");

        boxes[5].IsChecked = true;
        boxes[0].IsChecked = false;
        Assert.Equal([DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday],
            host.Flow.Session.Preferences.WorkingDays);

        // Back on More options, the summary follows
        Click((Button)days.FindName("BackButton"));
        var back = Assert.IsType<SettingsView>(host.Window.Screen);
        Assert.Equal(Visibility.Visible, ((StackPanel)back.FindName("AdvancedView")).Visibility);
        Assert.Equal("Tue – Sat", ((TextBlock)back.FindName("WorkingDaysSummary")).Text);
        Assert.Equal("Mon, Wed, Fri", Format.Days([DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday]));
        Assert.Equal("Every day", Format.Days(Format.Week));
        Assert.Equal("None", Format.Days([]));
    });

    [Fact]
    public Task OnTheHalfHour_TimerCountsDownToTheMark() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.ChangeIntervalAsync(60);
        host.Clock.UtcNow += TimeSpan.FromMinutes(10);
        await host.Flow.StartTrackingAsync();
        var started = host.Clock.LocalNow();
        Assert.Equal(started.AddMinutes(60), host.Flow.Scheduler.PromptAt);

        // Started at 10:10, so clock timing waits twenty minutes for 10:30.
        await host.Flow.ChangeTimingAsync(CheckInTiming.OnTheHalfHour);
        Assert.Equal(started.Date.AddHours(10.5), host.Flow.Scheduler.PromptAt);
        Assert.Equal(TimeSpan.FromMinutes(20), host.Flow.Scheduler.TimeLeft);
        Assert.Equal(started.Date.AddHours(10.5), host.Flow.Scheduler.NextCheckIn(TimeSpan.FromMinutes(60)));
    });

    [Fact]
    public Task SetupScreen_ClockTimesLiveInTheIntervalDropDown() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        var setup = host.Flow.ShowSetup();
        var interval = (ComboBox)setup.FindName("IntervalSelect");
        var halfHour = (CheckBox)setup.FindName("HalfHourBox");
        var help = (TextBlock)setup.FindName("IntervalHelp");

        // The date and state sit in the banner, not in a status line on the card; Larger text and buttons sits above More options
        Assert.Null(WidgetCard.GetStatus((WidgetCard)setup.FindName("Card")));
        Assert.Equal(Visibility.Visible, ((Border)setup.FindName("LargerTextRow")).Visibility);
        Render(setup, "setup");
        Assert.Equal(host.Flow.Status, ((Grid)setup.FindName("BannerStatus")).DataContext);

        // Ticking the box leaves the interval for clock times, from the next half hour
        halfHour.IsChecked = true;
        await WaitUntil(() => host.Flow.Session.Preferences.CheckInTiming == CheckInTiming.OnTheHalfHour);
        Assert.Equal(-1, interval.SelectedIndex);
        Assert.Equal("First check-in at 10:30 AM", help.Text);

        // Picking an interval goes back to it
        interval.SelectedItem = "20m";
        await WaitUntil(() => host.Flow.Session.Preferences.CheckInTiming == CheckInTiming.FromLastCheckIn);
        Assert.False(halfHour.IsChecked);
        Assert.Equal(20, host.Flow.Session.Settings.CheckInIntervalMinutes);
        Assert.Equal(["15m", "20m", "30m", "45m", "1h", "1h 30m", "2h", "3h"], interval.Items.Cast<string>());

        // Settings has the same drop-down, with the tick box above the intervals
        host.Flow.ShowSettings();
        var settings = Assert.IsType<SettingsView>(host.Window.Screen);
        Assert.NotNull(((ComboBox)settings.FindName("IntervalSelect")).Tag);
    });

    [Fact]
    public Task SetupScreen_DropDownOpensWithTheTickBoxAboveTheIntervals() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        var setup = host.Flow.ShowSetup();
        var window = new Window { Content = setup, SizeToContent = SizeToContent.WidthAndHeight, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        window.Show();
        window.Activate();
        try
        {
            var interval = (ComboBox)setup.FindName("IntervalSelect");
            var halfHour = (CheckBox)setup.FindName("HalfHourBox");
            interval.Focus();
            interval.IsDropDownOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            // The example marks are the next two half hours: 10:30 and 11:00 at 10:00
            Assert.Equal(" (10:30 & 11:00)", ((Run)setup.FindName("HalfHourMarks")).Text);

            // Up from the first interval reaches the tick box, Down goes back
            var first = (ComboBoxItem)interval.ItemContainerGenerator.ContainerFromIndex(0);
            first.Focus();
            PressKey(first, Key.Up);
            Assert.True(halfHour.IsKeyboardFocused);
            Assert.True(interval.IsDropDownOpen);
            PressKey(halfHour, Key.Down);
            Assert.True(first.IsKeyboardFocused);

            // The popup's root holds the panel with its shadow room around it
            var popup = (Popup)interval.Template.FindName("PART_Popup", interval);
            RenderElement((FrameworkElement)VisualTreeHelper.GetParent(popup.Child), "setup-interval-dropdown");
            interval.IsDropDownOpen = false;
        }
        finally
        {
            window.Close();
        }
    });

    private static void PressKey(UIElement target, Key key) =>
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });

    // Saves an element already laid out on screen, such as an open drop-down, when previews are asked for
    private static void RenderElement(FrameworkElement element, string name)
    {
        var folder = Environment.GetEnvironmentVariable("TIMEPLANNER_REMINDER_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(folder))
            return;
        Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }

    // A day with one entry logged to Acme › Portal, Coding at 10:30, and the next check-in shown at 12:00
    private static async Task<CheckInAlertView> CheckInAfterAnEntryAsync(TestHost host)
    {
        await host.Flow.StartTrackingAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.SaveEntryAsync(LogEntryMode.LogNow, host.Flow.GetLogPeriod(), ["Acme", "Portal"], ["Coding"], note: null);
        host.Clock.UtcNow += TimeSpan.FromMinutes(host.Flow.Session.Settings.CheckInIntervalMinutes);
        await host.Flow.Scheduler.RefreshAsync();
        host.Flow.CheckIn();
        return Assert.IsType<CheckInAlertView>(host.Window.Screen);
    }

    [Fact]
    public Task StillOnTheSameTask_LogsThePeriodWithTheLastChoice() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        var checkIn = await CheckInAfterAnEntryAsync(host);
        var since = host.Flow.GetLogPeriod().Start;
        var now = host.Clock.LocalNow();
        Assert.True(host.Flow.Scheduler.IsDue);

        // The button names what it will log
        var button = (Button)checkIn.FindName("SameTaskButton");
        await WaitUntil(() => ((TextBlock)checkIn.FindName("SameTaskText")).Text == "Portal · Coding");
        Assert.Equal(Visibility.Visible, button.Visibility);
        Assert.Equal("Acme › Portal · Coding", button.ToolTip);
        Render(checkIn, "check-in-same-task");

        Click(button);
        await WaitUntil(() => host.Window.Screen is EntrySavedView);
        var saved = (EntrySavedView)host.Window.Screen!;
        Assert.Equal("Logged to Portal · Coding.", ((TextBlock)saved.FindName("SavedText")).Text);
        Render(saved, "entry-saved-same-task");

        // The period since the check-in went to the same project and activity, as a check-in answer
        var day = await host.Services.GetRequiredService<TimeLogService>().GetDayAsync(host.Flow.Session.User.UserId, now);
        var latest = day.Entries.OrderBy(e => e.StartTime).Last();
        Assert.Equal(2, day.Entries.Count);
        Assert.Equal((since, now), (latest.StartTime, latest.EndTime));
        Assert.Equal("Portal", latest.Task!.Project!.Name);
        Assert.Equal("Coding", latest.Task.Category!.Name);
        Assert.Equal(EntryMethod.AutoPrompted, latest.Method);
        Assert.False(host.Flow.Scheduler.IsDue);
    });

    [Fact]
    public Task StillOnTheSameTask_NotOfferedOnceTheLastChoiceIsHidden() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        host.Clock.UtcNow += TimeSpan.FromMinutes(30);
        await host.Flow.SaveEntryAsync(LogEntryMode.LogNow, host.Flow.GetLogPeriod(), ["Acme", "Portal"], ["Coding"], note: null);
        var portal = await host.Flow.FindListItemAsync(activity: false, ["Acme", "Portal"]);
        Assert.True((await host.Flow.SetListItemHiddenAsync(ListKind.Project, portal!.Value.Id, hidden: true)).Ok);

        host.Flow.CheckIn();
        var checkIn = Assert.IsType<CheckInAlertView>(host.Window.Screen);
        await WaitUntil(() => ((Button)checkIn.FindName("SameTaskButton")).Visibility == Visibility.Collapsed);

        // Asked anyway, it opens the form rather than bringing the hidden project back
        await host.Flow.LogSameTaskAsync();
        Assert.IsType<LogEntryView>(host.Window.Screen);
        Assert.Null(await host.Flow.GetSameTaskAsync());
    });

    [Fact]
    public Task StillOnTheSameTask_NotOfferedBeforeTheFirstEntry() => OnUi(async () =>
    {
        using var host = await TestHost.CreateAsync();
        await host.Flow.StartTrackingAsync();
        host.Flow.CheckIn();
        var checkIn = Assert.IsType<CheckInAlertView>(host.Window.Screen);
        Assert.Equal(Visibility.Collapsed, ((Button)checkIn.FindName("SameTaskButton")).Visibility);
        Assert.Null(await host.Flow.GetSameTaskAsync());
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
        public int Hides { get; private set; }
        public void HideWidget() => Hides++;
        public int SizeApplied { get; private set; }
        public void ApplySize() => SizeApplied++;
    }

    private sealed class FakeDashboard : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public bool Succeeds { get; set; } = true;
        public bool StallVersionCheck { get; set; }
        //an account with two-factor on: the login only works when the request carries the code 123456
        public bool RequiresCode { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/widget/version")
                return StallVersionCheck ? WaitForCancellation(cancellationToken) : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new WidgetRelease("2.0.0")) });
            if (request.RequestUri.AbsolutePath == "/api/v1/auth/login" && RequiresCode)
            {
                using var body = System.Text.Json.JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                var otp = body.RootElement.TryGetProperty("otp", out var sent) && sent.ValueKind == System.Text.Json.JsonValueKind.String ? sent.GetString() : null;
                if (otp != "123456")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = JsonContent.Create(new Dictionary<string, object>
                        {
                            ["title"] = otp == null ? "A two-factor code is required." : "The two-factor code is not right.",
                            ["twoFactorRequired"] = true
                        })
                    });
            }
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
        // Voice notes and keys go in a throwaway folder, so the tests never touch the real ones
        private readonly string _aiFolder = Path.Combine(Path.GetTempPath(), "TimePlannerTests", Guid.NewGuid().ToString("N"));
        public ReminderClock Clock { get; } = new();
        public ReminderHistory History { get; } = new();
        public ReminderPending Pending { get; } = new();
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
            services.AddSingleton<IPendingSendStore>(host.Pending);
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
            services.AddSingleton<IVoiceNoteStore>(_ => new FileVoiceNoteStore(Path.Combine(host._aiFolder, "voice")));
            services.AddSingleton<WhisperModelManager>();
            services.AddSingleton(provider => new LocalWhisperTranscriber(provider.GetRequiredService<WhisperModelManager>(),
                provider.GetRequiredService<WidgetSession>().Preferences.Ai));
            services.AddSingleton(new AiKeyStore(host._aiFolder));
            services.AddSingleton<VoiceTranscriptionQueue>();
            services.AddSingleton<MicrophoneCatalog>();
            services.AddSingleton(provider => new OllamaHost(new HttpClient(), provider.GetRequiredService<WidgetSession>().Preferences.Ai));
            services.AddSingleton(provider => new OllamaSetup(new HttpClient(), provider.GetRequiredService<WidgetSession>().Preferences.Ai));
            services.AddSingleton(provider => new OllamaInstaller(new HttpClient(), provider.GetRequiredService<OllamaSetup>(),
                provider.GetRequiredService<OllamaHost>(), provider.GetRequiredService<WidgetSession>().Preferences.Ai, provider.GetRequiredService<PreferencesStore>()));
            services.AddSingleton<AiService>();
            services.AddSingleton<AiSetupService>();
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
            try { Directory.Delete(_aiFolder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
