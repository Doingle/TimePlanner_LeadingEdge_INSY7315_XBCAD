using System;
using System.Collections.Generic;
using System.Text;

namespace TimePlanner.Widget.Models
{
    public sealed class WidgetPreferences
    {
        public IdleVisibility IdleVisibility { get; set; } = IdleVisibility.Visible;
        public IdleShape IdleShape { get; set; } = IdleShape.Pill;
        public bool SoundOnCheckIn { get; set; } = true;
        // On for a first run and for a widget.json saved before the setting existed; a saved choice is kept on every update
        public bool StartWithWindows { get; set; } = true;
    }
}
