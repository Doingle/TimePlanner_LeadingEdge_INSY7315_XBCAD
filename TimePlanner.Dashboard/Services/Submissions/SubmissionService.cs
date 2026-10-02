using Microsoft.EntityFrameworkCore;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Dashboard.Services.Submissions
{
    //-----------------------------
    //records and reads which days each person has submitted
    public class SubmissionService
    {
        private readonly AuthDbContext _db;
        private readonly CompanyClock _clock;

        public SubmissionService(AuthDbContext db, CompanyClock clock)
        {
            _db = db;
            _clock = clock;
        }

        //-----------------------------
        //marks every given day as submitted now. Sending a day again moves its time forward, so the record always says when the data last arrived.
        //it is called after every accepted import, including one where every entry was already stored, so a retry after a failure still records the day
        public async Task RecordAsync(int appUserId, IEnumerable<DateTime> days)
        {
            var dates = days.Select(d => d.Date).Distinct().ToList();
            if (dates.Count == 0)
                return;

            var existing = await _db.DaySubmissions.Where(s => s.AppUserId == appUserId && dates.Contains(s.Date)).ToListAsync();
            var now = _clock.UtcNow;
            foreach (var date in dates)
            {
                var row = existing.FirstOrDefault(s => s.Date == date);
                if (row == null)
                    _db.DaySubmissions.Add(new DaySubmission { AppUserId = appUserId, Date = date, SubmittedAtUtc = now });
                else
                    row.SubmittedAtUtc = now;
            }
            await _db.SaveChangesAsync();
        }

        //-----------------------------
        //day -> when it was last submitted (UTC), for one person
        public async Task<Dictionary<DateTime, DateTime>> ForUserAsync(int appUserId, DateTime from, DateTime to) =>
            await _db.DaySubmissions.AsNoTracking()
                .Where(s => s.AppUserId == appUserId && s.Date >= from.Date && s.Date <= to.Date)
                .ToDictionaryAsync(s => s.Date, s => s.SubmittedAtUtc);

        //-----------------------------
        //every submission in the period, for the whole team
        public async Task<List<DaySubmission>> InPeriodAsync(DateTime from, DateTime to) =>
            await _db.DaySubmissions.AsNoTracking().Where(s => s.Date >= from.Date && s.Date <= to.Date).ToListAsync();

        //-----------------------------
        //the most recent day the person submitted, with when
        public async Task<DaySubmission?> LatestForUserAsync(int appUserId) =>
            await _db.DaySubmissions.AsNoTracking().Where(s => s.AppUserId == appUserId).OrderByDescending(s => s.Date).FirstOrDefaultAsync();
    }
}
//------------------------------EOF-----------------------------\\
