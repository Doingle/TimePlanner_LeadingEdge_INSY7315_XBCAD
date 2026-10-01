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
    }
}
