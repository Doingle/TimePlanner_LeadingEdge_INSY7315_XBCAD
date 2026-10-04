using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Services
{
    /// <summary>
    /// The widget's view of the working day, on top of Core: the day and its pauses are Core's day
    /// session (<see cref="DaySessionService"/>), so they survive a restart, and Core's
    /// <see cref="CheckInEngine"/> decides when a check-in is due and handles snoozes. Screens read
    /// the latest state from here every second; changes go through the async methods.
    /// </summary>
    public sealed class CheckInScheduler : IDisposable
    {
        private readonly WidgetSession _session;
        private readonly CheckInEngine _engine;
        private readonly IServiceScopeFactory _scopes;
        private readonly TimeProvider _clock;
        private readonly ILogger<CheckInScheduler> _logger;
        private readonly DispatcherTimer _timer;

        // The engine is called from the UI thread, one call at a time
        private readonly SemaphoreSlim _gate = new(1, 1);

        // Today's open day with its pauses, or null before the day starts and after it ends
        private DaySession? _day;

        // The day that was just ended, so Resume day can carry it on
        private DaySession? _ended;

        public CheckInScheduler(WidgetSession session, CheckInEngine engine, IServiceScopeFactory scopes, TimeProvider clock,
            ILogger<CheckInScheduler> logger)
        {
            _session = session;
            _engine = engine;
            _scopes = scopes;
            _clock = clock;
            _logger = logger;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
        }

        /// <summary>Every second, and after every change, so the idle shape and timer stay current and a due check-in shows.</summary>
        public event EventHandler? Ticked;

        public DateTime Now => _clock.LocalNow();

        /// <summary>What the engine said last.</summary>
        public CheckInStatus Status { get; private set; } = new(CheckInPhase.NotTracking, null, null, null, 0, 0, false, false);

        /// <summary>The day has started and has not ended.</summary>
        public bool IsRunning => _day != null;

        /// <summary>Where the time not logged yet begins: the end of the last entry, or the start of the day.</summary>
        public DateTime PeriodStart { get; private set; }

        public DateTime? PausedAt => _day?.Pauses.FirstOrDefault(p => p.EndedAt == null)?.StartedAt;

        public bool IsPaused => PausedAt != null;

        public bool IsSnoozed => Status.Phase == CheckInPhase.Snoozed;

        private int UserId => _session.User.UserId;

        private TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(1, _session.Settings.CheckInIntervalMinutes));

        // ------------------------------------------------------------------ the day

        /// <summary>After sign-in: picks up a day that is still open (the widget was restarted) and starts the clock.</summary>
        public async Task InitialiseAsync()
        {
            await ReloadDayAsync();
            await RunAsync(() => _engine.InitialiseAsync(UserId));
            _timer.Start();
        }

        public Task StartAsync() => ChangeDayAsync(days => days.StartDayAsync(UserId, Now));

        public async Task PauseAsync()
        {
            if (IsRunning && !IsPaused)
                await ChangeDayAsync(days => days.PauseAsync(UserId, Now));
        }

        public async Task ResumeAsync()
        {
            if (IsPaused)
                await ChangeDayAsync(days => days.ResumeAsync(UserId, Now));
        }

        public async Task EndDayAsync()
        {
            if (IsRunning)
                await ChangeDayAsync(async days => _ended = await days.EndDayAsync(UserId, Now));
        }

        /// <summary>Carries on after Day ended, as if the day had not been ended.</summary>
        public async Task ResumeDayAsync()
        {
            if (IsRunning)
                return;

            await using (var scope = _scopes.CreateAsyncScope())
            {
                if (_ended is { } day)
                {
                    day.EndedAt = null;
                    await scope.ServiceProvider.GetRequiredService<IDaySessionRepository>().UpdateAsync(day);
                }
                else
                {
                    await scope.ServiceProvider.GetRequiredService<DaySessionService>().StartDayAsync(UserId, Now);
                }
            }
            _ended = null;
            await ReloadDayAsync();
            await RunAsync(_engine.NotifySessionChangedAsync);
        }

        /// <summary>The user changed their interval, which is saved already.</summary>
        public Task IntervalChangedAsync() => RunAsync(_engine.ReloadSettingsAsync);

        /// <summary>An entry was saved, so the next period starts where it ended and the interval counts from now.</summary>
        public async Task LoggedAsync()
        {
            await ReloadDayAsync();
            await RunAsync(() => _engine.NotifyEntryLoggedAsync(restartTimer: true));
        }

        public async Task SnoozeAsync()
        {
            await RefreshAsync();
            if (Status.CanSnooze)
                await RunAsync(_engine.SnoozeAsync);
        }

        /// <summary>Asks the engine where things stand now.</summary>
        public Task RefreshAsync() => RunAsync(_engine.TickAsync);

        private async Task ChangeDayAsync(Func<DaySessionService, Task> change)
        {
            await using (var scope = _scopes.CreateAsyncScope())
                await change(scope.ServiceProvider.GetRequiredService<DaySessionService>());
            await ReloadDayAsync();
            await RunAsync(_engine.NotifySessionChangedAsync);
        }

        private async Task ReloadDayAsync()
        {
            await using var scope = _scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            _day = await services.GetRequiredService<DaySessionService>().GetOpenSessionAsync(UserId);

            // Where Core starts the time still to log
            var latest = await services.GetRequiredService<ITimeEntryRepository>().GetLatestForUserAsync(UserId);
            PeriodStart = _day == null ? Now
                : latest != null && latest.EndTime > _day.StartedAt ? latest.EndTime
                : _day.StartedAt;
        }

        private async Task RunAsync(Func<Task<CheckInStatus>> step)
        {
            await _gate.WaitAsync();
            try
            {
                Status = await step();
            }
            finally
            {
                _gate.Release();
            }
            Ticked?.Invoke(this, EventArgs.Empty);
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            // A change still being saved updates the screens itself when it is done
            if (_gate.CurrentCount == 0)
                return;

            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                // One missed second is not worth interrupting the user; the next tick tries again
                _logger.LogWarning(ex, "The check-in engine could not update.");
            }
        }

        // ------------------------------------------------------------------ working time

        /// <summary>The day's pauses between two times. A pause still running has no end yet.</summary>
        public List<(DateTime Start, DateTime End)> BreaksBetween(DateTime from, DateTime to) =>
            (_day?.Pauses ?? [])
                .Select(p => (Start: p.StartedAt, End: p.EndedAt ?? DateTime.MaxValue))
                .Where(p => p.End > from && p.Start < to)
                .ToList();

        public TimeSpan WorkedBetween(DateTime from, DateTime to) =>
            to > from ? WorkingSpans(from, to, BreaksBetween(from, to)).Aggregate(TimeSpan.Zero, (total, span) => total + (span.End - span.Start))
                : TimeSpan.Zero;

        /// <summary>Work since the last check-in that is not logged yet.</summary>
        public TimeSpan Unlogged => IsRunning ? WorkedBetween(PeriodStart, Now) : TimeSpan.Zero;

        /// <summary>
        /// The stretches of work between two times once the breaks are taken out, split the same way
        /// Core splits a day around its pauses, so a period with a pause in it becomes two entries.
        /// </summary>
        public static IReadOnlyList<(DateTime Start, DateTime End)> WorkingSpans(DateTime from, DateTime to,
            IEnumerable<(DateTime Start, DateTime End)> breaks) =>
            EntryService.SplitAroundPauses(
                breaks.Where(b => b.End > b.Start).Select(b => new SessionPause { StartedAt = b.Start, EndedAt = b.End }), from, to);

        // ------------------------------------------------------------------ the next check-in

        /// <summary>When the check-in shows: when the interval is up, or when a snooze ends. Null while paused.</summary>
        public DateTime? PromptAt => Status.Phase switch
        {
            CheckInPhase.Waiting => Status.NextCheckInAt,
            CheckInPhase.Snoozed => Status.SnoozedUntil,
            CheckInPhase.Due => Status.DueSince ?? Now,
            _ => null,
        };

        /// <summary>A check-in is waiting to be answered. The engine says so on its next tick; this already knows.</summary>
        public bool IsDue => Status.Phase switch
        {
            CheckInPhase.Due => true,
            CheckInPhase.Waiting or CheckInPhase.Snoozed => PromptAt <= Now,
            _ => false,
        };

        /// <summary>Until the check-in shows; while paused, the work left in the interval. Null when the day is not running.</summary>
        public TimeSpan? TimeLeft
        {
            get
            {
                if (!IsRunning)
                    return null;
                if (PausedAt is { } pausedAt)
                    return Longer(TimeSpan.Zero, Interval - WorkedBetween(PeriodStart, pausedAt));
                return Longer(TimeSpan.Zero, PromptAt is { } at ? at - Now : Interval - Unlogged);
            }
        }

        /// <summary>How much of the interval has been worked, 0 to 1: the idle ring.</summary>
        public double Elapsed => IsRunning ? Math.Clamp(WorkedBetween(PeriodStart, PausedAt ?? Now) / Interval, 0, 1) : 0;

        /// <summary>
        /// When a check-in would come due with <paramref name="interval"/>. Before the day starts it
        /// counts from now; during it, the work since the last check-in counts towards it. Null while paused.
        /// </summary>
        public DateTime? NextCheckIn(TimeSpan interval)
        {
            if (!IsRunning)
                return Now + interval;
            if (IsPaused)
                return null;
            return Now + Longer(TimeSpan.Zero, interval - Unlogged);
        }

        private static TimeSpan Longer(TimeSpan a, TimeSpan b) => a > b ? a : b;

        public void Dispose() => _timer.Stop();
    }
}
