using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Sync;
using TimePlanner.Widget.Services;
using TimePlanner.Widget.Views;

namespace TimePlanner.Widget.Tests;

public class WidgetUpdateTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Latest { get; set; } = "2.0.0";
        public bool Stall { get; set; }
        public bool Offline { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Offline) throw new HttpRequestException("Offline test");
            if (Stall) await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new WidgetRelease(Latest)) };
        }
    }

    [Fact]
    public async Task OlderVersionShowsOnce_AndDismissalSuppressesSignInRechecks()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        using var checker = new WidgetUpdateChecker(new DashboardClient(http), "1.0.0");
        var changes = 0;
        checker.Changed += (_, _) => changes++;
        await checker.CheckAsync();
        Assert.Equal("2.0.0", checker.Available!.LatestVersion);
        await checker.CheckAsync();
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, changes);
        checker.Dismiss();
        await checker.CheckAsync();
        Assert.Null(checker.Available);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.9.0")]
    [InlineData("invalid")]
    [InlineData(null)]
    public async Task CurrentNewerOrUnknownVersionsDoNotShow(string? latest)
    {
        using var handler = new Handler { Latest = latest };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        using var checker = new WidgetUpdateChecker(new DashboardClient(http), "1.0.0");
        await checker.CheckAsync();
        Assert.Null(checker.Available);
    }

    [Fact]
    public async Task OfflineStartupCanRetryAtSignIn()
    {
        using var handler = new Handler { Offline = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        using var checker = new WidgetUpdateChecker(new DashboardClient(http), "1.0.0");
        await checker.CheckAsync();
        Assert.Null(checker.Available);
        handler.Offline = false;
        await checker.CheckAsync();
        Assert.NotNull(checker.Available);
    }

    [Fact]
    public async Task SlowCheckDoesNotBlockCallerOrDuplicateRequests_AndTimesOutQuietly()
    {
        using var handler = new Handler { Stall = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        using var checker = new WidgetUpdateChecker(new DashboardClient(http), "1.0.0");
        var pending = checker.CheckAsync();
        Assert.False(pending.IsCompleted);
        await checker.CheckAsync();
        Assert.Equal(1, handler.Calls);
        await pending.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Null(checker.Available);
    }

    [Fact]
    public Task OverlayCloseBackdropAndUpdateWork_WithoutLaunchingAnything() => ReminderFlowTests.OnUi(async () =>
    {
        var opens = 0;
        var dismissals = 0;
        var view = new UpdateAvailableView(() => opens++);
        view.Dismissed += (_, _) => dismissals++;
        ((Button)view.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Button)view.FindName("DismissBackdrop")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, dismissals);
        ((Button)view.FindName("UpdateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, opens);
        Assert.Equal(3, dismissals);
        view.Measure(new Size(320, 400));
        view.Arrange(new Rect(0, 0, 320, 400));
        Assert.InRange(view.DesiredSize.Width, 0, 320);
        await Task.CompletedTask;
    });

    [Fact]
    public Task BrowserFailureLeavesNoticeDismissible() => ReminderFlowTests.OnUi(async () =>
    {
        var view = new UpdateAvailableView(() => throw new InvalidOperationException("Test browser failure"));
        var dismissed = false;
        view.Dismissed += (_, _) => dismissed = true;
        ((Button)view.FindName("UpdateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Visibility.Visible, ((TextBlock)view.FindName("ProblemText")).Visibility);
        ((Button)view.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(dismissed);
        await Task.CompletedTask;
    });

    //-----------------------------
    //fake host with notice explicitly open
    private sealed class NoticeOpenHost : IWidgetHost
    {
        public void Present(FrameworkElement screen, bool activate = true) { }
        public void HideWidget() { }
        public bool IsUpdateNoticeOpen => true;
    }

    //-----------------------------
    //fake host relying on default interface implementation
    private sealed class DefaultHost : IWidgetHost
    {
        public void Present(FrameworkElement screen, bool activate = true) { }
        public void HideWidget() { }
    }

    [Fact]
    public Task NoticeOpen_ExposesHostNoticeStateOnWidgetFlow() => ReminderFlowTests.OnUi(async () =>
    {
        using var host = await ReminderFlowTests.TestHost.CreateAsync();
        IWidgetHost openHost = new NoticeOpenHost();
        IWidgetHost defaultHost = new DefaultHost();

        Assert.True(openHost.IsUpdateNoticeOpen);
        Assert.False(defaultHost.IsUpdateNoticeOpen);

        var openFlow = ActivatorUtilities.CreateInstance<WidgetFlow>(host.Services, openHost);
        var defaultFlow = ActivatorUtilities.CreateInstance<WidgetFlow>(host.Services, defaultHost);

        Assert.True(openFlow.IsUpdateNoticeOpen);
        Assert.False(defaultFlow.IsUpdateNoticeOpen);
    });

    [Fact]
    public Task MainWindowAndSlot_ReflectUpdateNoticeVisibility() => ReminderFlowTests.OnUi(async () =>
    {
        var window = new MainWindow();
        var slot = window.OpenSlot();
        var updateNotice = (FrameworkElement)window.FindName("UpdateNotice");

        Assert.False(window.IsUpdateNoticeOpen);
        Assert.False(slot.IsUpdateNoticeOpen);

        updateNotice.Visibility = Visibility.Visible;
        Assert.True(window.IsUpdateNoticeOpen);
        Assert.True(slot.IsUpdateNoticeOpen);

        updateNotice.Visibility = Visibility.Collapsed;
        Assert.False(window.IsUpdateNoticeOpen);
        Assert.False(slot.IsUpdateNoticeOpen);

        await Task.CompletedTask;
    });
}
//------------------------------EOF-----------------------------\\
