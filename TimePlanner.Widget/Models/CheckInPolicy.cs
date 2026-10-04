using System;
using System.Collections.Generic;
using System.Text;

namespace TimePlanner.Widget.Models
{
    public static class CheckInPolicy
    {
        public static readonly IReadOnlyList<int> IntervalMinutes = [15, 20, 30, 45, 60, 90, 120, 180];

        public static List<int> ChoicesIncluding(int minutes) => [.. IntervalMinutes.Append(minutes).Distinct().Order()];
    }
}
