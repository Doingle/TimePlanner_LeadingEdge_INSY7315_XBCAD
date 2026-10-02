namespace TimePlanner.Dashboard.Data
{
    //-----------------------------
    //the record that a person's day reached the dashboard. A day is submitted when its entries arrive by the widget or by an uploaded file, and
    //sending the day again only moves SubmittedAtUtc to the latest time. It is a record only: there is no approval, nothing to reject or send back.
    //AppUserId is the id of the time tracking profile. There is no foreign key because the profile lives in the other database context
    public class DaySubmission
    {
        public int Id { get; set; }
        public int AppUserId { get; set; }

        //the day the submitted entries are for, midnight with no time of day
        public DateTime Date { get; set; }

        public DateTime SubmittedAtUtc { get; set; }
    }
}
//------------------------------EOF-----------------------------\\
