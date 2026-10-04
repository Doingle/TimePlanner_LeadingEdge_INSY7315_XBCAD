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

        public static bool IsValidActivityName(string name) => NameRules.IsValidActivityName(name);

        public static string CleanActivityName(string typed)
        {
            var cleaned = new string(typed.Select(c => char.IsControl(c) ? ' ' : c is '›' or '>' ? '-' : c).ToArray())
                .Trim().TrimStart(FormulaStarts.ToCharArray()).TrimStart();
            return cleaned.Length > ActivityName ? cleaned[..ActivityName].TrimEnd() : cleaned;
        }

        //longest company or project name
        public const int CompanyOrProject = NameRules.MaxCompanyOrProjectLength;

        //-----------------------------
        //valid company or project name
        public static bool IsValidCompanyOrProjectName(string name) => NameRules.IsValidCompanyOrProjectName(name);

        //-----------------------------
        //tidies a typed company or project name
        public static string CleanCompanyOrProjectName(string typed)
        {
            var cleaned = new string(typed.Select(c => char.IsControl(c) ? ' ' : c).ToArray())
                .Trim().TrimStart("=+-@".ToCharArray()).TrimStart();
            return cleaned.Length > CompanyOrProject ? cleaned[..CompanyOrProject].TrimEnd() : cleaned;
        }
    }
}
