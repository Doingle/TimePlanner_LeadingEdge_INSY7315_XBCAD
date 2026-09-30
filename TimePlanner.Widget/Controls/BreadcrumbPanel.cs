using System.Windows;
using System.Windows.Controls;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Controls
{
    public sealed class BreadcrumbPanel : Panel
    {
        private const double Gap = 4;

        public static readonly DependencyProperty ShrinkProperty = DependencyProperty.RegisterAttached(
            "Shrink", typeof(double), typeof(BreadcrumbPanel),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

        public static double GetShrink(DependencyObject d) => (double)d.GetValue(ShrinkProperty);

        public static void SetShrink(DependencyObject d, double value) => d.SetValue(ShrinkProperty, value);

        private double[] _natural = [];

        protected override Size MeasureOverride(Size availableSize)
        {
            var count = InternalChildren.Count;
            _natural = new double[count];
            double height = 0;
            for (var i = 0; i < count; i++)
            {
                var child = InternalChildren[i];
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                _natural[i] = child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }

            var widths = Fit(availableSize.Width);
            for (var i = 0; i < count; i++)
                if (widths[i] < _natural[i] - 0.5)
                    InternalChildren[i].Measure(new Size(widths[i], availableSize.Height));

            return new Size(Math.Min(Total(widths), availableSize.Width), height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var widths = Fit(finalSize.Width);
            double x = 0;
            for (var i = 0; i < InternalChildren.Count; i++)
            {
                InternalChildren[i].Arrange(new Rect(x, 0, widths[i], finalSize.Height));
                x += widths[i] + Gap;
            }
            return finalSize;
        }

        private double Total(double[] widths) => widths.Sum() + Gap * Math.Max(0, widths.Length - 1);

        private double[] Fit(double available)
        {
            var widths = (double[])_natural.Clone();
            if (double.IsInfinity(available))
                return widths;

            var overflow = Total(widths) - available;
            var frozen = new bool[widths.Length];
            while (overflow > 0.01)
            {
                double weight = 0;
                for (var i = 0; i < widths.Length; i++)
                    if (!frozen[i]) weight += GetShrink(InternalChildren[i]) * _natural[i];
                if (weight <= 0)
                    break;

                var clamped = false;
                for (var i = 0; i < widths.Length; i++)
                {
                    if (frozen[i]) continue;
                    var cut = overflow * GetShrink(InternalChildren[i]) * _natural[i] / weight;
                    if (cut >= widths[i])
                    {
                        widths[i] = 0;
                        frozen[i] = true;
                        clamped = true;
                    }
                }

                if (clamped)
                {
                    overflow = Total(widths) - available;
                    continue;
                }

                var shared = overflow;
                for (var i = 0; i < widths.Length; i++)
                    if (!frozen[i]) widths[i] -= shared * GetShrink(InternalChildren[i]) * _natural[i] / weight;
                break;
            }
            return widths;
        }
    }

}
