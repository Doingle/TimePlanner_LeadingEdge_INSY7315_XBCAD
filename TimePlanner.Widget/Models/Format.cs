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

        /// <summary>Monday first, the order the working days are listed in.</summary>
        public static readonly IReadOnlyList<DayOfWeek> Week =
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

        /// <summary>"Mon – Fri" for a run of three or more days, "Mon, Wed, Fri" otherwise, "Every day" or "None".</summary>
        public static string Days(IEnumerable<DayOfWeek> days)
        {
            var positions = days.Select(day => Week.ToList().IndexOf(day)).Where(i => i >= 0).Distinct().Order().ToList();
            string Short(int i) => Week[i].ToString()[..3];

            if (positions.Count == 0)
                return "None";
            if (positions.Count == Week.Count)
                return "Every day";
            if (positions.Count >= 3 && positions[^1] - positions[0] == positions.Count - 1)
                return $"{Short(positions[0])} – {Short(positions[^1])}";
            return string.Join(", ", positions.Select(Short));
        }

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
