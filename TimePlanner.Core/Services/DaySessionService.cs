using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Core.Services
{
    //-----------------------------
    //starts pauses resumes and ends the tracked day
    public class DaySessionService
    {
        private readonly IDaySessionRepository _sessions;
        private readonly ITimeEntryRepository _entries;

        public DaySessionService(IDaySessionRepository sessions, ITimeEntryRepository entries)
        {
            _sessions = sessions;
            _entries = entries;
        }

        //-----------------------------
        //open day with pauses or null
        public Task<DaySession?> GetOpenSessionAsync(int userId) => _sessions.GetOpenAsync(userId);

        //-----------------------------
        //opens a day or returns the open one
        public async Task<DaySession> StartDayAsync(int userId, DateTime now)
        {
            var open = await _sessions.GetOpenAsync(userId);

            //starting twice keeps the first day
            if (open != null)
            {
                return open;
            }

            var session = new DaySession { UserId = userId, StartedAt = now };
            await _sessions.AddAsync(session);
            return session;
        }

        //-----------------------------
        //starts a break unless one is running
        public async Task PauseAsync(int userId, DateTime now)
        {
            var session = await RequireOpenAsync(userId);

            //pausing twice keeps the first break
            if (session.Pauses.Any(p => p.EndedAt == null))
            {
                return;
            }

            session.Pauses.Add(new SessionPause { DaySessionId = session.DaySessionId, StartedAt = now });
            await _sessions.UpdateAsync(session);
        }

        //-----------------------------
        //ends the running break if any
        public async Task ResumeAsync(int userId, DateTime now)
        {
            var session = await RequireOpenAsync(userId);
            var pause = session.Pauses.FirstOrDefault(p => p.EndedAt == null);

            //resuming when not paused does nothing
            if (pause == null)
            {
                return;
            }

            pause.EndedAt = now;
            await _sessions.UpdateAsync(session);
        }

        //-----------------------------
        //ends any break then ends the day
        public async Task<DaySession> EndDayAsync(int userId, DateTime now)
        {
            var session = await RequireOpenAsync(userId);
            CloseAt(session, now);
            await _sessions.UpdateAsync(session);
            return session;
        }

        //-----------------------------
        //closes a day left open from an earlier date at its last activity and gives it back, or null when none was open
        public async Task<DaySession?> CloseStaleSessionsAsync(int userId, DateTime now)
        {
            var open = await _sessions.GetOpenAsync(userId);

            //only days started before today are stale
            if (open == null || !DayRules.IsLeftOpen(open, now))
            {
                return null;
            }

            var latest = await _entries.GetLatestForUserAsync(userId);
            CloseAt(open, DayRules.LastActivity(open, latest?.EndTime));
            await _sessions.UpdateAsync(open);
            return open;
        }

        //-----------------------------
        //sets end times on the day and a running break
        private static void CloseAt(DaySession session, DateTime end)
        {
            //a running break ends with the day
            foreach (var pause in session.Pauses.Where(p => p.EndedAt == null))
            {
                pause.EndedAt = end;
            }

            session.EndedAt = end;
        }

        //-----------------------------
        //open day or an error
        private async Task<DaySession> RequireOpenAsync(int userId)
        {
            var session = await _sessions.GetOpenAsync(userId);

            //pause resume and end need a started day
            if (session == null)
            {
                throw new InvalidOperationException("No day session is open.");
            }

            return session;
        }
    }
}
//------------------------------EOF-----------------------------\\
