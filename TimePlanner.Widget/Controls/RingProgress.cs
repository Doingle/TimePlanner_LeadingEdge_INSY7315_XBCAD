using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace TimePlanner.Widget.Controls
{
    public sealed class RingProgress : FrameworkElement
    {
        private const double Box = 32;
        private const double Radius = 13;
        private const double StrokeWidth = 3;

        public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
            nameof(Fraction), typeof(double), typeof(RingProgress),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
            nameof(TrackBrush), typeof(Brush), typeof(RingProgress),
            new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
            nameof(FillBrush), typeof(Brush), typeof(RingProgress),
            new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

      
        public double Fraction
        {
            get => (double)GetValue(FractionProperty);
            set => SetValue(FractionProperty, value);
        }

        public Brush TrackBrush
        {
            get => (Brush)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        public Brush FillBrush
        {
            get => (Brush)GetValue(FillBrushProperty);
            set => SetValue(FillBrushProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize) => new(Box, Box);

        protected override void OnRender(DrawingContext dc)
        {
            var center = new Point(Box / 2, Box / 2);
            dc.DrawEllipse(null, new Pen(TrackBrush, StrokeWidth), center, Radius, Radius);

            if (Fraction <= 0)
                return;

            var pen = new Pen(FillBrush, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, Icon.Arc(center.X, center.Y, Radius, Fraction));
        }
    }

}
