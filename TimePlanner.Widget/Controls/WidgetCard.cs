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

        /// <summary>
        /// The status line at the top of the time-tracking cards: a dot, green while tracking, and the date.
        /// Set on a screen, it flows down to the card inside it; null hides it.
        /// </summary>
        public static readonly DependencyProperty StatusProperty = DependencyProperty.RegisterAttached(
            "Status", typeof(CardStatus), typeof(WidgetCard), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

        public static CardStatus? GetStatus(DependencyObject d) => (CardStatus?)d.GetValue(StatusProperty);

        public static void SetStatus(DependencyObject d, CardStatus? value) => d.SetValue(StatusProperty, value);

        /// <summary>How much bigger everything is in large view, the "Larger text and buttons" setting.</summary>
        public const double LargeScale = 1.5;

        /// <summary>
        /// Large view: set once on the window and inherited by every screen, so icon buttons show their word and a
        /// screen can make room for it. The 1.5 times itself comes from the window's scale.
        /// </summary>
        public static readonly DependencyProperty IsLargeProperty = DependencyProperty.RegisterAttached(
            "IsLarge", typeof(bool), typeof(WidgetCard), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        public static bool GetIsLarge(DependencyObject d) => (bool)d.GetValue(IsLargeProperty);

        public static void SetIsLarge(DependencyObject d, bool value) => d.SetValue(IsLargeProperty, value);

        /// <summary>The short word an icon-only button shows under its icon in large view ("Settings", "Exit"); its tooltip stays.</summary>
        public static readonly DependencyProperty WordProperty = DependencyProperty.RegisterAttached(
            "Word", typeof(string), typeof(WidgetCard), new PropertyMetadata(null));

        public static string? GetWord(DependencyObject d) => (string?)d.GetValue(WordProperty);

        public static void SetWord(DependencyObject d, string? value) => d.SetValue(WordProperty, value);


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
