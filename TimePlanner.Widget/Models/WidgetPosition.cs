using System.Windows;

namespace TimePlanner.Widget.Models;

public sealed record WidgetPosition(double X, double Y, bool Right, bool Bottom)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y);

    /// <summary>
    /// The same place held by its top edge. A position held by its bottom edge moves its anchor up by
    /// <paramref name="room"/>, leaving that much space under the top for taller screens, so changing screens
    /// grows or shrinks the card downwards and the top stays where it is.
    /// </summary>
    public WidgetPosition PinnedAtTop(double room) => Bottom ? this with { Y = Y - room, Bottom = false } : this;

    public Point Locate(Rect workArea, Size windowSize, Thickness shadow)
    {
        var left = Right ? X - windowSize.Width + shadow.Right : X - shadow.Left;
        var top = Bottom ? Y - windowSize.Height + shadow.Bottom : Y - shadow.Top;
        return new Point(
            Math.Max(workArea.Left - shadow.Left, Math.Min(left, workArea.Right - windowSize.Width + shadow.Right)),
            Math.Max(workArea.Top - shadow.Top, Math.Min(top, workArea.Bottom - windowSize.Height + shadow.Bottom)));
    }
}
