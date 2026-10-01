using System;
using System.Collections.Generic;
using System.Text;

namespace TimePlanner.Widget.Models
{
    public static class InputLimits
    {
        public const int ActivityName = 100;

        public const int Note = 500;

        public static bool IsValidActivityName(string name) =>
            name.Trim().Length > 0
            && name.Length <= ActivityName
            && !name.Contains('›')
            && !name.Any(char.IsControl);

        public static string CleanActivityName(string typed)
        {
            var cleaned = new string(typed.Select(c => char.IsControl(c) ? ' ' : c == '›' ? '>' : c).ToArray()).Trim();
            return cleaned.Length > ActivityName ? cleaned[..ActivityName].TrimEnd() : cleaned;
        }
    }
}
