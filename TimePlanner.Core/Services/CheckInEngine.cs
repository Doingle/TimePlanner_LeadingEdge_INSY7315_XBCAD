using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services.Models;

namespace TimePlanner.Core.Services
{
    //-----------------------------
    //times check ins from the system clock
    //one instance per widget used from the ui thread
    public sealed class CheckInEngine
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IClock _clock;

        private int _userId;
        private UserSettings _settings = new();
        private DaySession? _session;
        private DateTime _anchor;
        private DateTime _clockTimingFrom;
        private int _skipsUsedToday;
        private DateOnly _skipDay;
        private DateTime? _dueSince;
        private DateTime? _snoozedUntil;
        private int _snoozesUsed;

        public CheckInEngine(IServiceScopeFactory scopes, IClock clock)
        {
            _scopes = scopes;
            _clock = clock;
        }

        //the hour and half hour marks clock timing lands on
        private static readonly TimeSpan HalfHour = TimeSpan.FromMinutes(30);

        private TimeSpan Interval => TimeSpan.FromMinutes(_settings.CheckInIntervalMinutes);

        //-----------------------------
        //when check ins fall, set before initialising and changed with SetTimingAsync
        public CheckInTiming Timing { get; set; } = CheckInTiming.FromLastCheckIn;

        //-----------------------------
        //whether a date gets check ins, every day unless the widget's working days say otherwise
        public Func<DateTime, bool> IsCheckInDay { get; set; } = _ => true;

        private int SnoozesLeft => Math.Max(0, _settings.MaxSnoozes - _snoozesUsed);

        private int SkipsLeft => Math.Max(0, _settings.MaxSkipsPerDay - _skipsUsedToday);

        private bool IsPaused => _session?.Pauses.Any(p => p.EndedAt == null) == true;

        private bool PromptOpen => _dueSince != null || _snoozedUntil != null;

        //-----------------------------
        //loads settings and the day then rebuilds the timer
        public async Task<CheckInStatus> InitialiseAsync(int userId)
        {
            _userId = userId;
            await LoadSettingsAsync();
            await LoadSessionAsync();
            await RebuildAnchorAsync();
            _clockTimingFrom = _anchor;
            ResolvePrompt();
            return await TickAsync();
        }

        //-----------------------------
        //reloads settings after the user saves them
        public async Task<CheckInStatus> ReloadSettingsAsync()
        {
            var previousInterval = _settings.CheckInIntervalMinutes;
            await LoadSettingsAsync();
            if (_settings.CheckInIntervalMinutes != previousInterval && Timing == CheckInTiming.FromLastCheckIn)
            {
                // Recalculate from the same work anchor; an old due/snoozed prompt must not pin the timer.
                // Keep the snooze/skip allowance and all unlogged work intact.
                _dueSince = null;
                _snoozedUntil = null;
            }
            return await TickAsync();
        }

        //-----------------------------
        //switches between counting from the last check in and the hour and half hour
        public Task<CheckInStatus> SetTimingAsync(CheckInTiming timing)
        {
            //an old due or snoozed prompt must not pin the new timing, the work since the anchor stays
            if (timing != Timing)
            {
                Timing = timing;
                _clockTimingFrom = _clock.Now;
                _dueSince = null;
                _snoozedUntil = null;
            }

            return TickAsync();
        }

        //-----------------------------
        //The next local :00 or :30, strictly after starting, answering or resuming.
        public static DateTime NextHalfHour(DateTime from) =>
            new((from.Ticks / HalfHour.Ticks + 1) * HalfHour.Ticks, from.Kind);

        //-----------------------------
        //reloads the day after start pause resume or end
        public async Task<CheckInStatus> NotifySessionChangedAsync()
        {
            var hadSession = _session != null;
            await LoadSessionAsync();

            //a newly started day gets a fresh timer
            if (!hadSession && _session != null)
            {
                await RebuildAnchorAsync();
            }

            //a break or ended day clears the prompt
            if (_session == null || IsPaused)
            {
                ResolvePrompt();
            }

            return await TickAsync();
        }

        //-----------------------------
        //moves state to now and returns what to show
        public async Task<CheckInStatus> TickAsync()
        {
            var now = _clock.Now;
            await RefreshSkipDayAsync(now);

            //no open day means nothing to prompt
            if (_session == null)
            {
                return Status(CheckInPhase.NotTracking, now);
            }

            //no prompts during a break
            if (IsPaused)
            {
                return Status(CheckInPhase.Paused, now);
            }

            //no prompts on a day off, so nothing can come due, be snoozed or be skipped for being ignored
            if (!IsCheckInDay(now))
            {
                ResolvePrompt();
                return Status(CheckInPhase.DayOff, now);
            }

            //a snooze holds the prompt back
            if (_snoozedUntil != null)
            {
                //still inside the snooze
                if (now < _snoozedUntil.Value)
                {
                    return Status(CheckInPhase.Snoozed, now);
                }

                _snoozedUntil = null;
                _dueSince = now;
            }

            //Only explicit day-session pauses suspend check-ins.
            if (_dueSince == null && now >= DueAt(now))
            {
                _dueSince = now;
            }

            //an open prompt may hit the ignored rule
            if (_dueSince != null)
            {
                await ApplyIgnoredRuleAsync(now);
            }

            //the rule may have closed the prompt
            if (_dueSince != null)
            {
                return Status(CheckInPhase.Due, now);
            }

            return Status(CheckInPhase.Waiting, now);
        }

        //-----------------------------
        //delays the open prompt while snoozes remain
        public async Task<CheckInStatus> SnoozeAsync()
        {
            var now = _clock.Now;

            //only a due prompt with snoozes left
            if (_dueSince == null || SnoozesLeft <= 0)
            {
                throw new InvalidOperationException("This check in cannot be snoozed.");
            }

            _snoozesUsed++;
            _dueSince = null;
            _snoozedUntil = now.AddMinutes(_settings.SnoozeMinutes);
            return await TickAsync();
        }

        //-----------------------------
        //gives up the time since the anchor
        public async Task<CheckInStatus> SkipAsync()
        {
            var now = _clock.Now;
            await RefreshSkipDayAsync(now);

            //needs an open prompt and a skip left today
            if (!PromptOpen || SkipsLeft <= 0)
            {
                throw new InvalidOperationException("This check in cannot be skipped.");
            }

            await RecordSkipAsync(now);
            return await TickAsync();
        }

        //-----------------------------
        //follows a saved entry
        public async Task<CheckInStatus> NotifyEntryLoggedAsync(bool restartTimer)
        {
            var now = _clock.Now;

            //answering a prompt always restarts
            //an early log restarts only on request
            if (PromptOpen || restartTimer)
            {
                _anchor = now;
            }

            ResolvePrompt();
            return await TickAsync();
        }

        //-----------------------------
        //handles a prompt left unanswered
        private async Task ApplyIgnoredRuleAsync(DateTime now)
        {
            //the user still has time to answer
            if (now - _dueSince!.Value < TimeSpan.FromMinutes(_settings.IgnoredCheckInMinutes))
            {
                return;
            }

            //each setting handles the silence its own way
            switch (_settings.IgnoredCheckInAction)
            {
                case IgnoredCheckInAction.LogAsUntracked:
                    _anchor = now;
                    ResolvePrompt();
                    break;

                case IgnoredCheckInAction.AutoSkip:
                    //with no skips left the prompt stays
                    if (SkipsLeft > 0)
                    {
                        await RecordSkipAsync(now);
                    }

                    break;
            }
        }

        //-----------------------------
        //saves a skip and restarts the timer
        private async Task RecordSkipAsync(DateTime now)
        {
            using var scope = _scopes.CreateScope();
            var skips = scope.ServiceProvider.GetRequiredService<ICheckInSkipRepository>();
            await skips.AddAsync(new CheckInSkip { DaySessionId = _session!.DaySessionId, SkippedAt = now });

            _skipsUsedToday++;
            _anchor = now;
            ResolvePrompt();
        }

        //-----------------------------
        //clears prompt and snooze state
        private void ResolvePrompt()
        {
            _dueSince = null;
            _snoozedUntil = null;
            _snoozesUsed = 0;
        }

        //-----------------------------
        //working time since from with breaks removed
        private TimeSpan ActiveTimeSince(DateTime from, DateTime now)
        {
            var total = now - from;

            //each overlapping break is taken off
            foreach (var pause in _session?.Pauses ?? Enumerable.Empty<SessionPause>())
            {
                var start = pause.StartedAt > from ? pause.StartedAt : from;
                var pauseEnd = pause.EndedAt ?? now;
                var end = pauseEnd < now ? pauseEnd : now;

                //only a real overlap counts
                if (end > start)
                {
                    total -= end - start;
                }
            }

            return total < TimeSpan.Zero ? TimeSpan.Zero : total;
        }

        //-----------------------------
        //Clock timing is independent of the interval and paused work duration.
        private DateTime DueAt(DateTime now)
        {
            if (Timing != CheckInTiming.OnTheHalfHour)
                return now + (Interval - ActiveTimeSince(_anchor, now));

            var from = _anchor > _clockTimingFrom ? _anchor : _clockTimingFrom;
            foreach (var pause in _session?.Pauses ?? Enumerable.Empty<SessionPause>())
                if (pause.EndedAt is { } resumed && resumed > from)
                    from = resumed;
            return NextHalfHour(from);
        }

        //-----------------------------
        //next prompt time from the chosen interval, timing and explicit pauses
        private DateTime NextDueAt(DateTime now)
        {
            var due = DueAt(now);
            return due > now ? due : now;
        }

        //-----------------------------
        //packs the state for the widget
        private CheckInStatus Status(CheckInPhase phase, DateTime now)
        {
            var due = phase == CheckInPhase.Due;
            var snoozed = phase == CheckInPhase.Snoozed;

            return new CheckInStatus(
                phase,
                phase == CheckInPhase.Waiting ? NextDueAt(now) : null,
                due ? _dueSince : null,
                snoozed ? _snoozedUntil : null,
                SnoozesLeft,
                SkipsLeft,
                due && SnoozesLeft > 0,
                (due || snoozed) && SkipsLeft > 0);
        }

        //-----------------------------
        //loads settings or defaults
        private async Task LoadSettingsAsync()
        {
            using var scope = _scopes.CreateScope();
            _settings = await scope.ServiceProvider.GetRequiredService<SettingsService>().GetAsync(_userId);
        }

        //-----------------------------
        //loads the open day with its breaks
        private async Task LoadSessionAsync()
        {
            using var scope = _scopes.CreateScope();
            _session = await scope.ServiceProvider.GetRequiredService<IDaySessionRepository>().GetOpenAsync(_userId);
        }

        //-----------------------------
        //anchor from day start last entry and last skip
        private async Task RebuildAnchorAsync()
        {
            var now = _clock.Now;

            //no open day gives nothing to time
            if (_session == null)
            {
                _anchor = now;
                await RefreshSkipDayAsync(now, force: true);
                return;
            }

            using var scope = _scopes.CreateScope();
            var entries = scope.ServiceProvider.GetRequiredService<ITimeEntryRepository>();
            var skips = scope.ServiceProvider.GetRequiredService<ICheckInSkipRepository>();

            var anchor = _session.StartedAt;
            var latest = await entries.GetLatestForUserAsync(_userId);

            //an entry from this day moves the anchor
            if (latest != null && latest.EndTime > anchor)
            {
                anchor = latest.EndTime;
            }

            var lastSkip = await skips.GetLatestForSessionAsync(_session.DaySessionId);

            //a later skip moves the anchor
            if (lastSkip != null && lastSkip.Value > anchor)
            {
                anchor = lastSkip.Value;
            }

            _anchor = anchor;
            await RefreshSkipDayAsync(now, force: true);
        }

        //-----------------------------
        //reloads the skip count on a new day
        private async Task RefreshSkipDayAsync(DateTime now, bool force = false)
        {
            var today = DateOnly.FromDateTime(now);

            //the count only changes when the date does
            if (!force && today == _skipDay)
            {
                return;
            }

            using var scope = _scopes.CreateScope();
            _skipsUsedToday = await scope.ServiceProvider.GetRequiredService<ICheckInSkipRepository>().CountForUserOnDayAsync(_userId, today);
            _skipDay = today;
        }
    }
}
//------------------------------EOF-----------------------------\\
