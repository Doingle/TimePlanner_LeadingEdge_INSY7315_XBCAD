using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace TimePlanner.Widget.Controls
{
    public static class Field
    {
        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
            "Placeholder", typeof(string), typeof(Field), new FrameworkPropertyMetadata(string.Empty));

        public static readonly DependencyProperty IsInvalidProperty = DependencyProperty.RegisterAttached(
            "IsInvalid", typeof(bool), typeof(Field), new FrameworkPropertyMetadata(false));

        public static readonly DependencyProperty IsOpenProperty = DependencyProperty.RegisterAttached(
            "IsOpen", typeof(bool), typeof(Field), new FrameworkPropertyMetadata(false));

        public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
            "Glyph", typeof(IconKind), typeof(Field), new FrameworkPropertyMetadata(IconKind.None));

        public static string GetPlaceholder(DependencyObject d) => (string)d.GetValue(PlaceholderProperty);
        public static void SetPlaceholder(DependencyObject d, string value) => d.SetValue(PlaceholderProperty, value);

        public static bool GetIsInvalid(DependencyObject d) => (bool)d.GetValue(IsInvalidProperty);
        public static void SetIsInvalid(DependencyObject d, bool value) => d.SetValue(IsInvalidProperty, value);

        public static bool GetIsOpen(DependencyObject d) => (bool)d.GetValue(IsOpenProperty);
        public static void SetIsOpen(DependencyObject d, bool value) => d.SetValue(IsOpenProperty, value);

        public static IconKind GetGlyph(DependencyObject d) => (IconKind)d.GetValue(GlyphProperty);
        public static void SetGlyph(DependencyObject d, IconKind value) => d.SetValue(GlyphProperty, value);
    }
