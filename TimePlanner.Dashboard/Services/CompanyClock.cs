namespace TimePlanner.Dashboard.Services
{
    //-----------------------------
    //"today" and "now" for the company, not for the server. A hosted server runs on UTC while the people logging time are two hours ahead, so a day would
    //otherwise roll over at 02:00 for them. The zone comes from Display:TimeZone (default South Africa) and the time from TimeProvider so tests can fix it
    public class CompanyClock
    {
        private readonly TimeProvider _time;
        private readonly TimeZoneInfo _zone;

        public CompanyClock(TimeProvider time, IConfiguration config)
        {
            _time = time;
            _zone = Find(config["Display:TimeZone"] ?? "Africa/Johannesburg");
        }

        public DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

        //the company's wall clock, with no zone attached
        public DateTime Now => TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).DateTime;

        public DateTime Today => Now.Date;

        //-----------------------------
        //a stored UTC time shown on the company's clock, with its offset so a client can format it anywhere
        public DateTimeOffset ToCompanyTime(DateTime utc) =>
            TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), _zone);

        //windows and linux name the same zone differently, so both are tried before settling for UTC
        private static TimeZoneInfo Find(string id)
        {
            foreach (var candidate in new[] { id, "South Africa Standard Time", "Africa/Johannesburg" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.Utc;
        }
    }
}
//------------------------------EOF-----------------------------\\
