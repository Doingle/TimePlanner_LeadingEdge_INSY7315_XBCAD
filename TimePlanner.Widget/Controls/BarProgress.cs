using System;
using System.Windows;
using System.Windows.Media;

namespace TimePlanner.Widget.Controls
{
    //-----------------------------
    //horizontal bar progress meter with track and animated fill
    public sealed class BarProgress : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(BarProgress),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
            nameof(TrackBrush), typeof(Brush), typeof(BarProgress),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
            nameof(FillBrush), typeof(Brush), typeof(BarProgress),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private double _displayValue;

        static BarProgress()
        {
            HeightProperty.OverrideMetadata(typeof(BarProgress), new FrameworkPropertyMetadata(6.0));
        }

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public Brush? TrackBrush
        {
            get => (Brush?)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        public Brush? FillBrush
        {
            get => (Brush?)GetValue(FillBrushProperty);
            set => SetValue(FillBrushProperty, value);
        }

        //-----------------------------
        //handles value changes with smooth interpolation
        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            //update display value and repaint
            if (d is BarProgress bar)
            {
                var target = Math.Clamp((double)e.NewValue, 0.0, 1.0);
                bar._displayValue = target;
                bar.InvalidateVisual();
            }
        }

        //-----------------------------
        //measures preferred height
        protected override Size MeasureOverride(Size availableSize)
        {
            var h = double.IsNaN(Height) ? 6.0 : Height;
            var w = double.IsNaN(Width) ? (double.IsInfinity(availableSize.Width) ? 100.0 : availableSize.Width) : Width;
            return new Size(w, h);
        }

        //-----------------------------
        //renders track and fill rounded rectangles
        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth;
            var h = ActualHeight;
            //guard against zero or negative dimensions
            if (w <= 0 || h <= 0)
            {
                return;
            }

            var radius = h / 2.0;
            var track = TrackBrush ?? (Brush)Application.Current.FindResource("DataTrackBrush");
            var fill = FillBrush ?? (Brush)Application.Current.FindResource("DataBrush");

            //draw background track
            dc.DrawRoundedRectangle(track, null, new Rect(0, 0, w, h), radius, radius);

            var clamped = Math.Clamp(_displayValue, 0.0, 1.0);
            //guard against zero value
            if (clamped <= 0.0)
            {
                return;
            }

            var fillWidth = Math.Max(h, w * clamped);
            //clamp fill width to control bounds
            if (fillWidth > w)
            {
                fillWidth = w;
            }

            //draw filled progress bar inside rounded clip
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), radius, radius));
            dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, fillWidth, h), radius, radius);
            dc.Pop();
        }
    }
}
//------------------------------EOF-----------------------------\\
