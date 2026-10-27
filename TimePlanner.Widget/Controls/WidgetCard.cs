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
        /// The bottom buttons (Done, Start today, Save entry, Send day). They stay pinned and in view; when the card is
        /// taller than the screen allows, the middle scrolls above them.
        /// </summary>
        public static readonly DependencyProperty FootProperty = DependencyProperty.Register(
            nameof(Foot), typeof(object), typeof(WidgetCard), new PropertyMetadata(null, OnPaddingChanged));

        /// <summary>A hairline across the card above the foot, always; otherwise it only shows while the middle scrolls.</summary>
        public static readonly DependencyProperty FootDividerProperty = DependencyProperty.Register(
            nameof(FootDivider), typeof(bool), typeof(WidgetCard), new PropertyMetadata(false));

        private static readonly DependencyPropertyKey ContentPaddingKey = DependencyProperty.RegisterReadOnly(
            nameof(ContentPadding), typeof(Thickness), typeof(WidgetCard), new PropertyMetadata(default(Thickness)));

        private static readonly DependencyPropertyKey FootPaddingKey = DependencyProperty.RegisterReadOnly(
            nameof(FootPadding), typeof(Thickness), typeof(WidgetCard), new PropertyMetadata(default(Thickness)));

        public static readonly DependencyProperty ContentPaddingProperty = ContentPaddingKey.DependencyProperty;

        public static readonly DependencyProperty FootPaddingProperty = FootPaddingKey.DependencyProperty;

        static WidgetCard()
        {
            PaddingProperty.OverrideMetadata(typeof(WidgetCard), new FrameworkPropertyMetadata(default(Thickness), OnPaddingChanged));
        }

        // The card's padding goes round the scrolling middle, except its bottom, which goes under the foot when there is one.
        // The middle keeps 4px at its bottom, which the foot overlaps, so a focus ring there isn't cut off by the scroll area
        private const double FocusRoom = 4;

        private static void OnPaddingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var card = (WidgetCard)d;
            var p = card.Padding;
            card.SetValue(ContentPaddingKey, card.Foot == null ? p : new Thickness(p.Left, p.Top, p.Right, FocusRoom));
            card.SetValue(FootPaddingKey, new Thickness(p.Left, -FocusRoom, p.Right, p.Bottom));
        }

        public object? Foot
        {
            get => GetValue(FootProperty);
            set => SetValue(FootProperty, value);
        }

        public bool FootDivider
        {
            get => (bool)GetValue(FootDividerProperty);
            set => SetValue(FootDividerProperty, value);
        }

        public Thickness ContentPadding => (Thickness)GetValue(ContentPaddingProperty);

        public Thickness FootPadding => (Thickness)GetValue(FootPaddingProperty);

        /// <summary>
        /// The status line at the top of the time-tracking cards: a dot, green while tracking, and the date.
        /// Set on a screen, it flows down to the card inside it; null hides it.
        /// </summary>
        public static readonly DependencyProperty StatusProperty = DependencyProperty.RegisterAttached(
            "Status", typeof(CardStatus), typeof(WidgetCard), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

        public static CardStatus? GetStatus(DependencyObject d) => (CardStatus?)d.GetValue(StatusProperty);

        public static void SetStatus(DependencyObject d, CardStatus? value) => d.SetValue(StatusProperty, value);


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
