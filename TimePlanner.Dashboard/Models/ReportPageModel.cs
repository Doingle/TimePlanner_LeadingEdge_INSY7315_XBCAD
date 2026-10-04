using TimePlanner.Dashboard.Services.Reports;

namespace TimePlanner.Dashboard.Models
{
    //-----------------------------
    //everything My Reports shows: the signed in person's own week, month or range, with the billable split and the breakdowns.
    //the detailed report (any grouping, any person) is an admin tool and lives on the team's Submissions page
    public class ReportPageModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        //set when the page was opened for a whole week or month (?view=week|month&date=), null for a custom range
        public string? View { get; set; }
        public string? PeriodLabel { get; set; }
        public DateTime? Previous { get; set; }
        public DateTime? Next { get; set; }

        //totals, billable against non-billable, and the breakdowns by category and project for the same range
        public ReportSummary? Summary { get; set; }

        //how many days of the range the person submitted
        public int SubmittedDays { get; set; }

        //null when the range was not valid, Error then says why
        public string? Error { get; set; }
    }
}
//------------------------------EOF-----------------------------\\
