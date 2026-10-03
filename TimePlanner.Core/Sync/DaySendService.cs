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

        //one refresh at a time because tokens rotate
        private static readonly SemaphoreSlim RefreshGate = new(1, 1);

        //-----------------------------
        //true while a saved token or refresh token is still valid
        public async Task<bool> IsSignedInAsync(DateTime nowUtc)
        {
            var token = await _tokens.LoadAsync();
            return token != null && (token.AccessValid(nowUtc) || token.CanRefresh(nowUtc));
        }

        //-----------------------------
        //stored email when signed in or refreshed
        public async Task<string?> GetSignedInEmailAsync(DateTime nowUtc)
        {
            var token = await _tokens.LoadAsync();

            //returns the email while the user remains signed in or refreshable
            if (token != null && (token.AccessValid(nowUtc) || token.CanRefresh(nowUtc)))
            {
                return token.Email;
            }

            return null;
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
        //ends the session on the server and forgets the local token
        public async Task SignOutAsync()
        {
            var token = await _tokens.LoadAsync();

            //revokes the refresh token on the server before clearing local state
            if (token?.RefreshToken != null)
            {
                await _client.LogoutAsync(token.RefreshToken);
            }

            await _tokens.ClearAsync();
        }

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
        //gets a valid access token or refreshes it silently using the refresh token
        private async Task<(StoredToken? Token, SendOutcome? Problem)> GetTokenAsync(DateTime nowUtc, bool forceRefresh)
        {
            var token = await _tokens.LoadAsync();

            //a missing token needs a fresh sign in
            if (token == null)
            {
                return (null, SendOutcome.Of(SendStatus.NeedsSignIn, "Please sign in to the dashboard."));
            }

            //a valid access token is returned directly when force refresh is false
            if (!forceRefresh && token.AccessValid(nowUtc))
            {
                return (token, null);
            }

            //an expired refresh token clears the store
            if (!token.CanRefresh(nowUtc))
            {
                await _tokens.ClearAsync();
                return (null, SendOutcome.Of(SendStatus.NeedsSignIn, "Please sign in to the dashboard."));
            }

            await RefreshGate.WaitAsync();
            try
            {
                var reloaded = await _tokens.LoadAsync();

                //another caller refreshed the token while waiting
                if (reloaded != null && reloaded.AccessValid(nowUtc) && reloaded.AccessToken != token.AccessToken)
                {
                    return (reloaded, null);
                }

                var outcome = await _client.RefreshAsync(reloaded ?? token);

                //a successful refresh saves the rotated token
                if (outcome.Status == SignInStatus.SignedIn)
                {
                    await _tokens.SaveAsync(outcome.Token!);
                    return (outcome.Token, null);
                }

                //refused refresh clears the store so the user signs in again
                if (outcome.Status == SignInStatus.InvalidDetails)
                {
                    await _tokens.ClearAsync();
                    return (null, SendOutcome.Of(SendStatus.NeedsSignIn, "Your dashboard session ended. Please sign in again."));
                }

                //being offline keeps the stored token
                if (outcome.Status == SignInStatus.Offline)
                {
                    return (null, SendOutcome.Of(SendStatus.Offline, outcome.Message));
                }

                return (null, SendOutcome.Of(SendStatus.Failed, outcome.Message));
            }
            finally
            {
                RefreshGate.Release();
            }
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

            var (token, problem) = await GetTokenAsync(nowUtc, false);

            //a problem loading or refreshing the token is returned directly
            if (problem != null)
            {
                return problem;
            }

            var outcome = await _client.SendDayAsync(token!.AccessToken, rows);

            //a refused send retries once after a refresh if the refresh token can be used
            if (outcome.Status == SendStatus.NeedsSignIn && token.CanRefresh(nowUtc))
            {
                var (refreshedToken, refreshProblem) = await GetTokenAsync(nowUtc, true);

                //if refresh yielded a problem return it
                if (refreshProblem != null)
                {
                    return refreshProblem;
                }

                outcome = await _client.SendDayAsync(refreshedToken!.AccessToken, rows);
            }

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
