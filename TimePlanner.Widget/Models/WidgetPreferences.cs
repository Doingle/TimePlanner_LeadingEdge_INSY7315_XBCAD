using System;
using System.Collections.Generic;
using System.Text;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Ai;

namespace TimePlanner.Widget.Models
{
    public sealed class WidgetPreferences
    {
        public IdleVisibility IdleVisibility { get; set; } = IdleVisibility.Visible;
        public IdleShape IdleShape { get; set; } = IdleShape.Pill;
        public bool SoundOnCheckIn { get; set; } = true;
        public AiSettings Ai { get; set; } = new();
        // On for a first run and for a widget.json saved before the setting existed; a saved choice is kept on every update
        public bool StartWithWindows { get; set; } = true;
        public bool VoiceTipDismissed { get; set; }
        public WidgetPosition? IdlePosition { get; set; }
        public WidgetPosition? MainPosition { get; set; }

        // The widget opens at sign-in only on these days; on others it waits in the tray, and a day can still be started by hand.
        // Mon–Fri for a first run and for a widget.json saved before the setting existed
        public List<DayOfWeek> WorkingDays { get; set; } =
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

        // Every interval from the last check-in, or on the hour and half hour
        public CheckInTiming CheckInTiming { get; set; } = CheckInTiming.FromLastCheckIn;

        public bool IsWorkingDay(DateTime day) => WorkingDays.Contains(day.DayOfWeek);

        // The "Larger text and buttons" switch in Settings. Kept, but it doesn't change the widget yet
        public bool LargerText { get; set; }
    }
}
