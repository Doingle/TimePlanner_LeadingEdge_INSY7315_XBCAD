using System.IO;
using System.Windows;
using TimePlanner.Widget.Models;
using TimePlanner.Widget.Services;

namespace TimePlanner.Widget.Tests.Services;

public sealed class IdleInteractionTests
{
    private sealed class HoverClock : TimeProvider
    {
        public long Milliseconds { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }

    [Fact]
    public void HoverRequiresThreeContinuousSeconds_LeavingOrDraggingResetsIt()
    {
        var clock = new HoverClock();
        var hover = new IdleHover(clock);
        hover.Start();
        clock.Milliseconds = 1500;
        Assert.Equal(0.5, hover.Fraction);
        clock.Milliseconds = 2999;
        Assert.True(hover.Fraction < 1);
        hover.Cancel();
        clock.Milliseconds = 6000;
        Assert.Equal(0, hover.Fraction);
        hover.Start();
        clock.Milliseconds = 8999;
        Assert.True(hover.Fraction < 1);
        clock.Milliseconds = 9000;
        Assert.Equal(1, hover.Fraction);
        hover.Cancel();
        Assert.Equal(0, hover.Fraction);
    }

    [Fact]
    public void SeparatePositionsSurviveRestart_WithoutChangingStartupPreference()
    {
        var path = Path.Combine(Path.GetTempPath(), $"widget-position-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"StartWithWindows\":false,\"IdleShape\":\"Ring\"}");
            var store = new PreferencesStore(path);
            var prefs = store.Load();
            Assert.Null(prefs.IdlePosition);
            Assert.Null(prefs.MainPosition);
            prefs.IdlePosition = new WidgetPosition(1800, 16, true, false);
            prefs.MainPosition = new WidgetPosition(600, 700, false, true);
            store.Save(prefs);
            var restarted = new PreferencesStore(path).Load();
            Assert.Equal(prefs.IdlePosition, restarted.IdlePosition);
            Assert.Equal(prefs.MainPosition, restarted.MainPosition);
            Assert.False(restarted.StartWithWindows);
            Assert.Equal(IdleShape.Ring, restarted.IdleShape);
            restarted.IdlePosition = new WidgetPosition(150, 30, false, false);
            store.Save(restarted);
            Assert.Equal(prefs.MainPosition, store.Load().MainPosition);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(-5000, -5000)]
    [InlineData(5000, 5000)]
    [InlineData(100, 100)]
    public void RestoredPositionStaysInsideAvailableDesktop(double x, double y)
    {
        var area = new Rect(0, 0, 1920, 1040);
        var shadow = MainWindow.ShadowMargin;
        var size = new Size(416, 320);
        var location = new WidgetPosition(x, y, true, true).Locate(area, size, shadow);
        Assert.InRange(location.X + shadow.Left, area.Left, area.Right - (size.Width - shadow.Left - shadow.Right));
        Assert.InRange(location.Y + shadow.Top, area.Top, area.Bottom - (size.Height - shadow.Top - shadow.Bottom));
    }

    [Fact]
    public void MainCardTopStaysStillWhenScreensChangeHeight()
    {
        var area = new Rect(0, 0, 1920, 1040);
        var shadow = MainWindow.ShadowMargin;

        // A card saved by its bottom edge near the taskbar now hangs from a top with room for the tallest screen
        var pinned = new WidgetPosition(1904, 1024, true, true).PinnedAtTop(MainWindow.MainRoom);
        Assert.Equal(new WidgetPosition(1904, 1024 - MainWindow.MainRoom, true, false), pinned);

        // Timer, Settings and the tallest screens all start at the same top
        foreach (var height in new[] { 210d, 450, 650 })
        {
            var size = new Size(320 + shadow.Left + shadow.Right, height + shadow.Top + shadow.Bottom);
            Assert.Equal(1024 - MainWindow.MainRoom, pinned.Locate(area, size, shadow).Y + shadow.Top);
        }

        // A position already held by its top is left as it is
        var top = new WidgetPosition(600, 120, false, false);
        Assert.Same(top, top.PinnedAtTop(MainWindow.MainRoom));
    }

    [Fact]
    public void AnchoredCornerStaysStillWhenShapeSizeChanges()
    {
        var anchor = new WidgetPosition(1904, 16, true, false);
        var area = new Rect(0, 0, 1920, 1040);
        foreach (var width in new[] { 16d, 40, 112 })
        {
            var size = new Size(width + 96, 160);
            var location = anchor.Locate(area, size, MainWindow.ShadowMargin);
            Assert.Equal(1904, location.X + size.Width - 48);
            Assert.Equal(16, location.Y + 48);
        }
    }
}
