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

        //null when the filter was not valid, Error then says why
        public HoursReport? Report { get; set; }
        public string? Error { get; set; }

        //a timesheet is one person's, so the export needs a single user in scope
        public bool CanExport => Report != null && (!CanPickUser || UserId != null);
    }
}
//------------------------------EOF-----------------------------\\
