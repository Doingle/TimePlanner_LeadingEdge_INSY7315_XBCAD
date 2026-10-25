namespace TimePlanner.Core.Services
{
    //-----------------------------
    //time rules for minute comparison
    public static class TimeRules
    {
        //-----------------------------
        //drops seconds so times compare by the minute
        public static DateTime ToMinute(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind);
    }
}
//------------------------------EOF-----------------------------\\
