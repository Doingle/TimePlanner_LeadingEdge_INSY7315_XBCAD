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

        // The "Larger text and buttons" switch in Settings: large view, every screen 1.5 times the size. Off for a first run
        public bool LargerText { get; set; }

        // The Timesheet opens the way it was last left, wherever it is opened from: the small page in the widget, or the
        // wide window at its saved size, place and maximised state. The small page for a first run
        public bool TimesheetWide { get; set; }
        public WindowPlace? TimesheetWindow { get; set; }
    }

    // Where a resizable window was left, in the desktop's units. A maximised window keeps the size it goes back to as well
    public sealed record WindowPlace(double Left, double Top, double Width, double Height, bool Maximised)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsValid => double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Width) && double.IsFinite(Height)
            && Width > 0 && Height > 0;
    }
}
