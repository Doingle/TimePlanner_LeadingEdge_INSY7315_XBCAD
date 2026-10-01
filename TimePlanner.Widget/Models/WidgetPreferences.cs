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
    }
}
