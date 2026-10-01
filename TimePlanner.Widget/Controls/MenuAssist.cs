using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace TimePlanner.Widget.Controls
{
    public static class MenuAssist
    {
        public static readonly DependencyProperty IsEmptyBranchProperty = DependencyProperty.RegisterAttached(
            "IsEmptyBranch", typeof(bool), typeof(MenuAssist), new FrameworkPropertyMetadata(false));

        public static bool GetIsEmptyBranch(DependencyObject d) => (bool)d.GetValue(IsEmptyBranchProperty);
        public static void SetIsEmptyBranch(DependencyObject d, bool value) => d.SetValue(IsEmptyBranchProperty, value);
    }

}
