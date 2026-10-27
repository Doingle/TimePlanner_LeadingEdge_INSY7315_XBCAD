using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace TimePlanner.Widget.Controls
{
    public static class DeviceLines
    {
        // In large view a line is still a whole number of screen pixels, not 1.5 of them, so it stays sharp
        public static void Apply(DpiScale dpi, double viewScale = 1)
        {
            var scale = dpi.PixelsPerDip * viewScale;
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
