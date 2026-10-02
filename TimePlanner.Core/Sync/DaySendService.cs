using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services;

namespace TimePlanner.Core.Sync
{
    //-----------------------------
    //previews and sends finished days chosen by the user
    public class DaySendService
    {
        private readonly DashboardClient _client;
        private readonly ITokenStore _tokens;
        private readonly ISendHistoryStore _history;
        private readonly ITimeEntryRepository _entries;
        private readonly IProjectRepository _projects;
        private readonly IDaySessionRepository _sessions;
        private readonly ActivityService _activities;

        public DaySendService(
            DashboardClient client,
            ITokenStore tokens,
            ISendHistoryStore history,
            ITimeEntryRepository entries,
            IProjectRepository projects,
            IDaySessionRepository sessions,
            ActivityService activities)
        {
            _client = client;
            _tokens = tokens;
            _history = history;
            _entries = entries;
            _projects = projects;
            _sessions = sessions;
            _activities = activities;
        }

        //-----------------------------
        //true while a saved token is still valid
        public async Task<bool> IsSignedInAsync(DateTime nowUtc)
        {
            var token = await _tokens.LoadAsync();
            return token != null && token.ExpiresAtUtc > nowUtc.AddMinutes(1);
        }

        //-----------------------------
        //signs in and keeps only the token
        public async Task<SignInOutcome> SignInAsync(string email, string password)
        {
            var outcome = await _client.SignInAsync(email.Trim(), password);

            //only a successful sign in is saved
            if (outcome.Status == SignInStatus.SignedIn)
            {
                await _tokens.SaveAsync(outcome.Token!);
            }

            return outcome;
        }

        //-----------------------------
        //forgets the saved token
        public Task SignOutAsync() => _tokens.ClearAsync();

        //-----------------------------
        //days already sent newest first
        public Task<IReadOnlyList<SentDay>> GetHistoryAsync() => _history.GetAsync();

        //-----------------------------
        //shows exactly what sending would upload
        public async Task<DayPreview> PreviewDayAsync(int userId, DateOnly day, DateTime now)
        {
            var rows = await BuildRowsAsync(userId, day);
            var ended = await HasEndedAsync(userId, day, now);
            return new DayPreview(day, rows, Hours(rows), ended && rows.Count > 0);
        }

        //-----------------------------
        //sends a finished day and records it locally
        public async Task<SendOutcome> SendDayAsync(int userId, DateOnly day, DateTime now, DateTime nowUtc)
        {
            //only finished days may leave the pc
            if (!await HasEndedAsync(userId, day, now))
            {
                return SendOutcome.Of(SendStatus.NotEnded, "End the day before sending it.");
            }

            var rows = await BuildRowsAsync(userId, day);

            //an empty day has nothing to send
            if (rows.Count == 0)
            {
                return SendOutcome.Of(SendStatus.NothingToSend, "There are no entries for this day.");
            }

            var token = await _tokens.LoadAsync();

            //a missing or expired token needs a fresh sign in
            if (token == null || token.ExpiresAtUtc <= nowUtc.AddMinutes(1))
            {
                return SendOutcome.Of(SendStatus.NeedsSignIn, "Please sign in to the dashboard.");
            }

            var outcome = await _client.SendDayAsync(token.AccessToken, rows);

            //a refused token is dropped so the user signs in again
            if (outcome.Status == SendStatus.NeedsSignIn)
            {
                await _tokens.ClearAsync();
            }

            //only a stored day joins the history
            if (outcome.Status == SendStatus.Sent)
            {
                await _history.AddAsync(new SentDay(day, now, rows.Count, Hours(rows)));
            }

            return outcome;
        }

        //-----------------------------
        //past days and today after end day count as finished
        private async Task<bool> HasEndedAsync(int userId, DateOnly day, DateTime now)
        {
            var today = DateOnly.FromDateTime(now);

            //earlier days are always finished
            if (day < today)
            {
                return true;
            }

            //future days cannot be sent
            if (day > today)
            {
                return false;
            }

            var open = await _sessions.GetOpenAsync(userId);
            return open == null || DateOnly.FromDateTime(open.StartedAt) != today;
        }

        //-----------------------------
        //turns the day's local entries into upload rows
        private async Task<List<DayEntryPayload>> BuildRowsAsync(int userId, DateOnly day)
        {
            var from = day.ToDateTime(TimeOnly.MinValue);
            var entries = await _entries.GetForUserAsync(userId, from, from.AddDays(1));
            var lookup = await _activities.GetActivityLookupAsync();
            var companies = new Dictionary<int, string>();
            var rows = new List<DayEntryPayload>();

            //each entry becomes one row
            foreach (var entry in entries)
            {
                var projectId = entry.Task!.ProjectID;

                //company names are looked up once per project
                if (!companies.TryGetValue(projectId, out var company))
                {
                    var project = await _projects.GetByIdAsync(projectId);
                    company = project?.Company?.Name ?? string.Empty;
                    companies[projectId] = company;
                }

                rows.Add(new DayEntryPayload(
                    company,
                    entry.Task.Project?.Name ?? string.Empty,
                    string.Join(" > ", lookup[entry.Task.CategoryId].Path),
                    DateTime.SpecifyKind(entry.StartTime, DateTimeKind.Unspecified),
                    DateTime.SpecifyKind(entry.EndTime, DateTimeKind.Unspecified),
                    entry.Note,
                    entry.Method.ToString()));
            }

            return rows;
        }

        //-----------------------------
        //total hours across the rows
        private static double Hours(IEnumerable<DayEntryPayload> rows) =>
            Math.Round(rows.Sum(r => (r.End - r.Start).TotalHours), 2);
    }
}
//------------------------------EOF-----------------------------\\
