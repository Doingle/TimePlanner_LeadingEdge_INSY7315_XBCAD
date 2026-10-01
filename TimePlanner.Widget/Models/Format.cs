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
    }
}
