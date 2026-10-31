using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace TimePlanner.Widget
{
    public interface IWidgetHost
    {
        void Present(FrameworkElement screen, bool activate = true);

        void HideWidget();

        void ConfigurePositions(Models.WidgetPreferences preferences, Action save) { }

        // Applies the "Larger text and buttons" preference straight away, to the screen that is showing
        void ApplySize() { }
    }
}
