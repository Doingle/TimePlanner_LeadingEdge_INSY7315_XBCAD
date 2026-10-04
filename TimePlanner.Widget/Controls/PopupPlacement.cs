using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TimePlanner.Widget.Controls
{

    public enum PopupPlacementKind
    {
        None,
        Dropdown,
        Submenu,
    }

    public static class PopupPlacement
    {
        public static readonly Thickness ShadowMargin = new(24);

        private const double DropdownGap = 4;  
        private const double SubmenuLift = 8;  

        public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
            "Kind", typeof(PopupPlacementKind), typeof(PopupPlacement),
            new PropertyMetadata(PopupPlacementKind.None, OnKindChanged));

        public static PopupPlacementKind GetKind(DependencyObject element) => (PopupPlacementKind)element.GetValue(KindProperty);

        public static void SetKind(DependencyObject element, PopupPlacementKind value) => element.SetValue(KindProperty, value);

        private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var kind = (PopupPlacementKind)e.NewValue;
            if (kind == PopupPlacementKind.None)
                return;

            switch (d)
            {
                case Popup popup:
                    popup.Placement = PlacementMode.Custom;
                    popup.CustomPopupPlacementCallback = (popupSize, targetSize, _) => Place(kind, popup.PlacementTarget, popupSize, targetSize);
                    break;
                case ContextMenu menu:
                    menu.Placement = PlacementMode.Custom;
                    menu.CustomPopupPlacementCallback = (popupSize, targetSize, _) => Place(kind, menu.PlacementTarget, popupSize, targetSize);
                    break;
            }
        }

        private static CustomPopupPlacement[] Place(PopupPlacementKind kind, UIElement? target, Size popupSize, Size targetSize)
        {
            var s = target is FrameworkElement fe && fe.ActualWidth > 0 && targetSize.Width > 0
                ? targetSize.Width / fe.ActualWidth
                : 1.0;
            var m = ShadowMargin;

            if (kind == PopupPlacementKind.Dropdown)
            {
                var x = -m.Left * s;
                return
                [
                    new CustomPopupPlacement(new Point(x, targetSize.Height + (DropdownGap - m.Top) * s), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(x, -popupSize.Height + (m.Bottom - DropdownGap) * s), PopupPrimaryAxis.Horizontal),
            ];
            }

            var y = -(SubmenuLift + m.Top) * s;
            return
            [
                new CustomPopupPlacement(new Point(targetSize.Width - m.Left * s, y), PopupPrimaryAxis.Vertical),
                new CustomPopupPlacement(new Point(-popupSize.Width + m.Right * s, y), PopupPrimaryAxis.Vertical),
            ];
        }
    }
}