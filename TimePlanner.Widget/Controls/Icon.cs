using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace TimePlanner.Widget.Controls
{

    public enum IconKind
    {
        None,Check,

        ChevronDown, ChevronRight, ChevronLeft,

        Search,

        Plus,Minus,

        Close,
        Bell,
        Clock,
        Alert,
        Lock,
        Settings,
        Pause, Play,
        Download,
        ShapePill, ShapeRing, ShapeDot,
        ClockHands,
    }

    public sealed class Icon : FrameworkElement
    {
        private const double ViewBox = 24;
        private const double Stroke = 1.75;

        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(IconKind), typeof(Icon),
            new FrameworkPropertyMetadata(IconKind.None, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
            nameof(Size), typeof(double), typeof(Icon),
            new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
            typeof(Icon),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

        public IconKind Kind
        {
            get => (IconKind)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        public double Size
        {
            get => (double)GetValue(SizeProperty);
            set => SetValue(SizeProperty, value);
        }

        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) => new(Size, Size);

        protected override void OnRender(DrawingContext dc)
        {
            if (Kind == IconKind.None || !Glyphs.TryGetValue(Kind, out var parts))
                return;

            var scale = Size / ViewBox;
            dc.PushTransform(new ScaleTransform(scale, scale));
            foreach (var part in parts)
            {
                if (part.Opacity < 1)
                    dc.PushOpacity(part.Opacity);

                if (part.Filled)
                {
                    dc.DrawGeometry(Foreground, null, part.Geometry);
                }
                else
                {
                    var pen = new Pen(Foreground, part.StrokeWidth)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round,
                        LineJoin = PenLineJoin.Round,
                    };
                    dc.DrawGeometry(null, pen, part.Geometry);
                }

                if (part.Opacity < 1)
                    dc.Pop();
            }
            dc.Pop();
        }

        private sealed record Part(Geometry Geometry, bool Filled = false, double StrokeWidth = Stroke, double Opacity = 1);

        private static Part P(string data) => new(Freeze(Geometry.Parse(data)));

        private static Part Circle(double cx, double cy, double r, double strokeWidth = Stroke, double opacity = 1) =>
            new(Freeze(new EllipseGeometry(new Point(cx, cy), r, r)), StrokeWidth: strokeWidth, Opacity: opacity);

        private static Geometry Freeze(Geometry g)
        {
            g.Freeze();
            return g;
        }

        internal static Geometry Arc(double cx, double cy, double r, double fraction)
        {
            fraction = Math.Clamp(fraction, 0, 1);

            if (fraction >= 0.9999)
                return Freeze(new EllipseGeometry(new Point(cx, cy), r, r));

            var angle = fraction * 2 * Math.PI;
            var start = new Point(cx, cy - r);
            var end = new Point(cx + r * Math.Sin(angle), cy - r * Math.Cos(angle));
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new ArcSegment(end, new System.Windows.Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return Freeze(geometry);
        }

        private static readonly Dictionary<IconKind, Part[]> Glyphs = new()
        {
            [IconKind.Check] = [P("M20,6 L9,17 L4,12")],
            [IconKind.ChevronDown] = [P("M6,9 L12,15 L18,9")],
            [IconKind.ChevronRight] = [P("M9,6 L15,12 L9,18")],
            [IconKind.ChevronLeft] = [P("M15,6 L9,12 L15,18")],
            [IconKind.Search] = [Circle(11, 11, 7), P("M20,20 L16,16")],
            [IconKind.Plus] = [P("M12,5 V19"), P("M5,12 H19")],
            [IconKind.Minus] = [P("M5,12 H19")],
            [IconKind.Close] = [P("M6,6 L18,18"), P("M18,6 L6,18")],
            [IconKind.Bell] = [P("M18,8 A6,6 0 0 0 6,8 C6,15 3,17 3,17 H21 S18,15 18,8"), P("M13.73,21 A2,2 0 0 1 10.27,21"),],
            [IconKind.Clock] = [Circle(12, 12, 9), P("M12,7 V12 L15,14")],
            [IconKind.Alert] = [Circle(12, 12, 9), P("M12,8 V13"), P("M12,16 H12.01")],
            [IconKind.Lock] =
            [
                new(Freeze(new RectangleGeometry(new Rect(5, 11, 14, 10), 2, 2))),
            P("M8,11 V7 A4,4 0 0 1 16,7 V11"),
        ],

            [IconKind.Settings] =
            [Circle(12, 12, 3), P("M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.6 1.65 1.65 0 0 0 10 3.09V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"),],

            [IconKind.Pause] =
            [
                new(Freeze(new RectangleGeometry(new Rect(7, 5, 3.5, 14), 1.25, 1.25)), Filled: true),
            new(Freeze(new RectangleGeometry(new Rect(13.5, 5, 3.5, 14), 1.25, 1.25)), Filled: true),
        ],

            [IconKind.Play] = [new(Freeze(Geometry.Parse("M8,5.2 V18.8 A1,1 0 0 0 9.5,19.66 L20.7,12.86 A1,1 0 0 0 20.7,11.14 L9.5,4.34 A1,1 0 0 0 8,5.2 Z")), Filled: true)],

            [IconKind.Download] = [P("M12,4 V15"), P("M7,10 L12,15 L17,10"), P("M5,20 H19")],

            [IconKind.ShapePill] = [new(Freeze(new RectangleGeometry(new Rect(1, 7, 22, 10), 5, 5)))],
            [IconKind.ShapeRing] = [Circle(12, 12, 8, strokeWidth: 2.5, opacity: 0.25), new(Arc(12, 12, 8, (50.3 - 15) / (2 * Math.PI * 8)), StrokeWidth: 2.5),],

            [IconKind.ShapeDot] = [new(Freeze(new EllipseGeometry(new Point(12, 12), 4.5, 4.5)), Filled: true)],

            [IconKind.ClockHands] = [new(Freeze(Geometry.Parse("M12,7 V12 L15,14")), StrokeWidth: 2.75)],
        };
    }
}
