using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TimePlanner.Widget.Models
{
    public static class Format
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public static string Clock(DateTime time) => time.ToString("h:mm tt", Culture);

        public static string Interval(int minutes)
        {
            var h = minutes / 60;
            var m = minutes % 60;
            return h == 0 ? $"{m}m" : m == 0 ? $"{h}h" : $"{h}h {m}m";
        }

        public static string Duration(TimeSpan length) => Interval(Math.Max(0, (int)length.TotalMinutes));

        public static string Range(DateTime start, DateTime end) =>
            start.Date == end.Date && start.Hour < 12 == end.Hour < 12
                ? $"{start.ToString("h:mm", Culture)} – {Clock(end)}"
                : $"{Clock(start)} – {Clock(end)}";

        public static string Countdown(TimeSpan left)
        {
            var minutes = (int)Math.Ceiling(Math.Max(0, left.TotalMinutes));
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }

        public static string Spoken(TimeSpan left)
        {
            var minutes = (int)Math.Ceiling(Math.Max(0, left.TotalMinutes));
            var h = minutes / 60;
            var m = minutes % 60;
            var hours = h == 1 ? "1 hour" : $"{h} hours";
            var mins = m == 1 ? "1 minute" : $"{m} minutes";
            return h == 0 ? mins : m == 0 ? hours : $"{hours} {mins}";
        }

        public static string Day(DateTime day) => day.ToString("dddd, MMM d", Culture);

        /// <summary>"Friday 23 Oct": the date in the status line and the timesheet's day list.</summary>
        public static string Date(DateOnly day) => day.ToString("dddd d MMM", Culture);

        /// <summary>"Yesterday", a weekday in the last week, or "Fri 9 Oct" further back.</summary>
        public static string RelativeDay(DateOnly day, DateOnly today) => (today.DayNumber - day.DayNumber) switch
        {
            0 => "Today",
            1 => "Yesterday",
            > 1 and < 7 => day.ToString("dddd", Culture),
            _ => day.ToString("ddd d MMM", Culture),
        };
    }
}
