using System;
using System.Collections.Generic;
using System.Text;
using TimePlanner.Core.Services;

namespace TimePlanner.Widget.Models
{
    public static class InputLimits
    {
        public const int ActivityName = ActivityService.MaxNameLength;

        public const int Note = 500;

        // › separates levels in the widget and > in the dashboard's CSV, and the dashboard refuses a
        // name that starts like a spreadsheet formula, so an exported timesheet can be uploaded again
        private const string FormulaStarts = "=+-@";

        public static bool IsValidActivityName(string name) =>
            name.Trim().Length > 0
            && name.Length <= ActivityName
            && !name.Contains('›')
            && !name.Contains('>')
            && !FormulaStarts.Contains(name.Trim()[0])
            && !name.Any(char.IsControl);

        public static string CleanActivityName(string typed)
        {
            var cleaned = new string(typed.Select(c => char.IsControl(c) ? ' ' : c is '›' or '>' ? '-' : c).ToArray())
                .Trim().TrimStart(FormulaStarts.ToCharArray()).TrimStart();
            return cleaned.Length > ActivityName ? cleaned[..ActivityName].TrimEnd() : cleaned;
        }
    }
}
