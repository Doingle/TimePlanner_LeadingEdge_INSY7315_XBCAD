using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TimePlanner.Widget.Controls
{
    /// <summary>
    /// Settings → Larger text and buttons. The same screens, 1.5× bigger, scaled once at the root of a window so layout,
    /// click areas and pop-ups all grow; icon-only buttons also show a short word beside or under their icon.
    /// </summary>
    public static class LargeView
    {
        public const double Factor = 1.5;

        /// <summary>On for everything inside a window in large view. Set once at the root; templates read it.</summary>
        public static readonly DependencyProperty IsOnProperty = DependencyProperty.RegisterAttached(
            "IsOn", typeof(bool), typeof(LargeView), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnWordChanged));

        public static bool GetIsOn(DependencyObject d) => (bool)d.GetValue(IsOnProperty);

        public static void SetIsOn(DependencyObject d, bool value) => d.SetValue(IsOnProperty, value);

        /// <summary>The short word an icon-only button shows in large view: Back, Close, Edit, Delete, Add, Next…</summary>
        public static readonly DependencyProperty WordProperty = DependencyProperty.RegisterAttached(
            "Word", typeof(string), typeof(LargeView), new FrameworkPropertyMetadata(null, OnWordChanged));

        public static string? GetWord(DependencyObject d) => (string?)d.GetValue(WordProperty);

        public static void SetWord(DependencyObject d, string? value) => d.SetValue(WordProperty, value);

        /// <summary>The word goes under the icon rather than beside it: the card's corner buttons.</summary>
        public static readonly DependencyProperty WordBelowProperty = DependencyProperty.RegisterAttached(
            "WordBelow", typeof(bool), typeof(LargeView), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        public static bool GetWordBelow(DependencyObject d) => (bool)d.GetValue(WordBelowProperty);

        public static void SetWordBelow(DependencyObject d, bool value) => d.SetValue(WordBelowProperty, value);

        /// <summary>The word comes before the icon, as in "Next ›".</summary>
        public static readonly DependencyProperty WordFirstProperty = DependencyProperty.RegisterAttached(
            "WordFirst", typeof(bool), typeof(LargeView), new FrameworkPropertyMetadata(false));

        public static bool GetWordFirst(DependencyObject d) => (bool)d.GetValue(WordFirstProperty);

        public static void SetWordFirst(DependencyObject d, bool value) => d.SetValue(WordFirstProperty, value);

        private static readonly DependencyPropertyKey ShowsWordKey = DependencyProperty.RegisterAttachedReadOnly(
            "ShowsWord", typeof(bool), typeof(LargeView), new FrameworkPropertyMetadata(false));

        /// <summary>A button in large view with a word to show. The icon button templates trigger on it.</summary>
        public static readonly DependencyProperty ShowsWordProperty = ShowsWordKey.DependencyProperty;

        public static bool GetShowsWord(DependencyObject d) => (bool)d.GetValue(ShowsWordProperty);

        private static void OnWordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ButtonBase)
                d.SetValue(ShowsWordKey, GetIsOn(d) && !string.IsNullOrEmpty(GetWord(d)));
        }

        public static double ScaleOf(bool on) => on ? Factor : 1;

        /// <summary>Scales a window's root, and everything in and popping up from it, by 1.5 or back to 1.</summary>
        public static void Apply(FrameworkElement root, bool on)
        {
            SetIsOn(root, on);
            root.LayoutTransform = on ? new ScaleTransform(Factor, Factor) : Transform.Identity;
        }
    }
}
