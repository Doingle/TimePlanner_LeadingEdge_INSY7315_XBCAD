using TimePlanner.Dashboard.Services.Overview;
using TimePlanner.Dashboard.Services.Reports;

namespace TimePlanner.Dashboard.Models
{
    //-----------------------------
    //the My Timesheet page: the period laid out day by day, with the previous and next period to move to.
    //Data is null when the login has no time tracking profile (NoProfile) or the period asked for was not valid (Error)
    public class TimesheetPageModel
    {
        public TimesheetView? Data { get; set; }
        public DateTime Previous { get; set; }
        public DateTime Next { get; set; }
        public bool NoProfile { get; set; }
        public string? Error { get; set; }
    }

    //-----------------------------
    //the admin overview, submissions and exports pages share the period navigation
    public class AdminPageModel
    {
        public string View { get; set; } = "week";
        public string Label { get; set; } = "";
        public DateTime Previous { get; set; }
        public DateTime Next { get; set; }
        public string? Error { get; set; }

        public AdminOverview? Overview { get; set; }
        public SubmissionGrid? Grid { get; set; }

        //the people the exports page offers, one csv each (or everyone in a zip)
        public List<(int Id, string Name)> People { get; set; } = new();
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        //-----------------------------
        //the detailed report under the submissions grid: any range, grouped by project, company, person, day or activity, for everyone or one person.
        //the range starts as the grid's week or month. Report is null when the filter was not valid, ReportError then says why
        public DateTime ReportFrom { get; set; }
        public DateTime ReportTo { get; set; }
        public ReportGrouping GroupBy { get; set; } = ReportGrouping.Project;
        public int? ReportUserId { get; set; }
        public HoursReport? Report { get; set; }
        public string? ReportError { get; set; }

        //a timesheet is one person's, so the download needs a person picked
        public bool CanExport => Report != null && ReportUserId != null;
    }
}
//------------------------------EOF-----------------------------\\
