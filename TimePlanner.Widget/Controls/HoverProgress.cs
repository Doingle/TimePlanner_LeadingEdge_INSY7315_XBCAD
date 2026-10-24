using System.Windows;
using System.Windows.Media;

namespace TimePlanner.Widget.Controls;

public sealed class HoverProgress : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(HoverProgress),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public bool Round { get; set; }
    public Brush Fill { get; set; } = Brushes.Transparent;

    protected override void OnRender(DrawingContext dc)
    {
        if (Fraction <= 0 || ActualWidth < 4 || ActualHeight < 4) return;
        if (Round)
            dc.DrawGeometry(null, new Pen(Fill, 2), Icon.Arc(ActualWidth / 2, ActualHeight / 2,
                Math.Min(ActualWidth, ActualHeight) / 2 - 1, Fraction));
        else
        {
            // Match the pill silhouette so the bottom fill cannot protrude past its curved ends.
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), ActualHeight / 2, ActualHeight / 2));
            dc.DrawRectangle(Fill, null, new Rect(0, ActualHeight - 3, ActualWidth * Math.Clamp(Fraction, 0, 1), 3));
            dc.Pop();
        }
    }
}
