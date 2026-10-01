using System;
using System.Collections.Generic;
using System.Text;

namespace TimePlanner.Widget.Models
{
    public enum IdleVisibility
    {
        Visible,
        Faded,
        Hidden,
    }

    public enum IdleShape
    {
        Pill,
        Ring,
        Dot,
    }

    public sealed class WidgetSession
    {
        public int IntervalMinutes { get; set; } = CheckInPolicy.DefaultIntervalMinutes;
        public IdleVisibility IdleVisibility { get; set; } = IdleVisibility.Visible;
        public IdleShape IdleShape { get; set; } = IdleShape.Pill;
        public bool SoundOnCheckIn { get; set; } = true;
        public WorkDay Day { get; } = SampleData.CreateDay();
    }
}
