using System;
using System.Text;

namespace TimePlanner.Core.Domain.Entities
{
    //-----------------------------
    //this class represents a timesheet object
    public class TimeSheet
    {
        public int TimeSheetId { get; set; }

        public int UserId { get; set; }
        public AppUser? User { get; set; }

        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal TotalHours { get; set; }

        //-----------------------------
        //included simple csv export method for timesheet data, subject to change
        public string ExportToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("TimeSheetId,UserId,ProjectId,PeriodStart,PeriodEnd,TotalHours");
            sb.AppendLine($"{TimeSheetId},{UserId},{ProjectId},{PeriodStart:O},{PeriodEnd:O},{TotalHours}");
            return sb.ToString();
        }

        //-----------------------------
        //this method escapes CSV fields prevents issues regarding special characters
        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return field;

            if ("=+-@".Contains(field[0]))
                field = "'" + field;

            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
                field = "\"" + field.Replace("\"", "\"\"") + "\"";

            return field;
        }
    }
}
//------------------------------EOF-----------------------------\\
