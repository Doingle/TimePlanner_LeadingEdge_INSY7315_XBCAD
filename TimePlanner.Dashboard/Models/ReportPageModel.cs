using TimePlanner.Dashboard.Services.Reports;

namespace TimePlanner.Dashboard.Models
{
    //-----------------------------
    //everything the report page shows: the filter that was applied, the people a privileged user may pick, and the result
    public class ReportPageModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public ReportGrouping GroupBy { get; set; } = ReportGrouping.Project;
        public int? UserId { get; set; }

        public bool CanPickUser { get; set; }
        public List<(int Id, string Name)> Users { get; set; } = new();

        //set when the page was opened for a whole week or month (?view=week|month&date=), null for a custom range
        public string? View { get; set; }
        public string? PeriodLabel { get; set; }
        public DateTime? Previous { get; set; }
        public DateTime? Next { get; set; }

        //totals, billable against non-billable, and the breakdowns by category and project for the same range
        public ReportSummary? Summary { get; set; }

        //null when the filter was not valid, Error then says why
        public HoursReport? Report { get; set; }
        public string? Error { get; set; }

        //a timesheet is one person's, so the export needs a single user in scope
        public bool CanExport => Report != null && (!CanPickUser || UserId != null);
    }
}
//------------------------------EOF-----------------------------\\
