using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace TimePlanner.Widget.Controls
{
    public static class DeviceLines
    {
        public static void Apply(DpiScale dpi)
        {
            var scale = dpi.PixelsPerDip;
            if (scale <= 0 || Application.Current == null)
                return;

            var hairline = Math.Max(1, Math.Floor(scale + 0.001)) / scale;

            var resources = Application.Current.Resources;
            resources["Hairline"] = hairline;
            resources["BorderWidth"] = new Thickness(hairline);

            resources["BorderWidthSelected"] = new Thickness(hairline + 1);
        }
    }
}
