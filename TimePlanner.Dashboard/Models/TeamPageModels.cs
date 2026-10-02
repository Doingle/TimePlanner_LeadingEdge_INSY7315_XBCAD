using TimePlanner.Dashboard.Services.Overview;

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
    }
}
//------------------------------EOF-----------------------------\\
