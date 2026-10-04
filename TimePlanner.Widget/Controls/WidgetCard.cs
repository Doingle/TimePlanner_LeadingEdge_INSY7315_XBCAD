using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TimePlanner.Widget.Controls
{
    [TemplatePart(Name = PartDragArea, Type = typeof(FrameworkElement))]
    [TemplatePart(Name = PartGrip, Type = typeof(FrameworkElement))]
    public class WidgetCard : ContentControl
    {
        private const string PartDragArea = "PART_DragArea";
        private const string PartGrip = "PART_Grip";

        public static readonly DependencyProperty IsRaisedProperty = DependencyProperty.Register(
            nameof(IsRaised), typeof(bool), typeof(WidgetCard), new PropertyMetadata(false));

        public static readonly DependencyProperty CornerContentProperty = DependencyProperty.Register(
            nameof(CornerContent), typeof(object), typeof(WidgetCard), new PropertyMetadata(null));

        public static readonly DependencyProperty BannerProperty = DependencyProperty.Register(
            nameof(Banner), typeof(object), typeof(WidgetCard), new PropertyMetadata(null));

        public static readonly DependencyProperty GripBrushProperty = DependencyProperty.Register(
            nameof(GripBrush), typeof(Brush), typeof(WidgetCard), new PropertyMetadata(null));


        public bool IsRaised
        {
            get => (bool)GetValue(IsRaisedProperty);
            set => SetValue(IsRaisedProperty, value);
        }

        public object? CornerContent
        {
            get => GetValue(CornerContentProperty);
            set => SetValue(CornerContentProperty, value);
        }

       
        public object? Banner
        {
            get => GetValue(BannerProperty);
            set => SetValue(BannerProperty, value);
        }

        public Brush? GripBrush
        {
            get => (Brush?)GetValue(GripBrushProperty);
            set => SetValue(GripBrushProperty, value);
        }

        private FrameworkElement? _dragArea;
        private FrameworkElement? _grip;

        public override void OnApplyTemplate()
        {
            if (_dragArea != null) _dragArea.MouseLeftButtonDown -= OnDragStart;
            if (_grip != null) _grip.MouseLeftButtonDown -= OnDragStart;

            base.OnApplyTemplate();

            _dragArea = GetTemplateChild(PartDragArea) as FrameworkElement;
            _grip = GetTemplateChild(PartGrip) as FrameworkElement;
            if (_dragArea != null) _dragArea.MouseLeftButtonDown += OnDragStart;
            if (_grip != null) _grip.MouseLeftButtonDown += OnDragStart;
        }

        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
                return;

            var window = Window.GetWindow(this);
            if (window == null)
                return;

            e.Handled = true;
            window.DragMove();
        }
    }
}
