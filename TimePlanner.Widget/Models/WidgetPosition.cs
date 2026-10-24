using System.Windows;

namespace TimePlanner.Widget.Models;

public sealed record WidgetPosition(double X, double Y, bool Right, bool Bottom)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y);

    public Point Locate(Rect workArea, Size windowSize, Thickness shadow)
    {
        var left = Right ? X - windowSize.Width + shadow.Right : X - shadow.Left;
        var top = Bottom ? Y - windowSize.Height + shadow.Bottom : Y - shadow.Top;
        return new Point(
            Math.Max(workArea.Left - shadow.Left, Math.Min(left, workArea.Right - windowSize.Width + shadow.Right)),
            Math.Max(workArea.Top - shadow.Top, Math.Min(top, workArea.Bottom - windowSize.Height + shadow.Bottom)));
    }
}
