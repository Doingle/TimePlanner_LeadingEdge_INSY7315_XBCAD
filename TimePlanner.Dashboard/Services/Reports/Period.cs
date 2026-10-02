using System.Globalization;

namespace TimePlanner.Dashboard.Services.Reports
{
    //-----------------------------
    //a week (Monday to Sunday) or a calendar month, both ends included. Every screen that offers "this week / this month" uses this one definition
    public record Period(string View, DateTime From, DateTime To, string Label)
    {
        //Monday to Friday. The company has no public holiday calendar, so a holiday simply shows as a missing day
        public static bool IsWorkingDay(DateTime day) => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        public IEnumerable<DateTime> Days()
        {
            for (var day = From; day <= To; day = day.AddDays(1))
                yield return day;
        }

        //-----------------------------
        //turns "week" or "month" and a date inside it into the period. Blank means the week. Returns false for anything else
        public static bool TryResolve(string? view, DateTime? date, DateTime today, out Period period)
        {
            period = null!;
            var day = (date ?? today).Date;

            switch ((view ?? "week").Trim().ToLowerInvariant())
            {
                case "week":
                    var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
                    period = new Period("week", monday, monday.AddDays(6),
                        $"{monday.ToString("d MMM", CultureInfo.InvariantCulture)} to {monday.AddDays(6).ToString("d MMM yyyy", CultureInfo.InvariantCulture)}");
                    return true;
                case "month":
                    var first = new DateTime(day.Year, day.Month, 1);
                    period = new Period("month", first, first.AddMonths(1).AddDays(-1), first.ToString("MMMM yyyy", CultureInfo.InvariantCulture));
                    return true;
                default:
                    return false;
            }
        }

        public static string InvalidViewMessage => "view must be week or month.";
    }
}
//------------------------------EOF-----------------------------\\
