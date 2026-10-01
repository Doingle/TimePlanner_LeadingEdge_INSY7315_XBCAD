using System.Windows.Threading;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Services;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Services
{
    /// <summary>
    /// The check-in engine. It counts working time from the last check-in and works out when the
    /// next one is due: after the user's interval of work, never during lunch, and later if they
    /// snoozed it. Tracking runs until the user ends the day. Lunch and pauses stop the count and
    /// are left out of the time that gets logged.
    /// </summary>
    public sealed class CheckInScheduler : IDisposable
    {
        private readonly WidgetSession _session;
        private readonly LunchBreakDetector _lunch;
        private readonly SnoozeManager _snooze;
        private readonly TimeProvider _clock;
        private readonly DispatcherTimer _timer;

        // Pauses that have ended; one still running is PausedAt
        private readonly List<(DateTime Start, DateTime End)> _pauses = [];

        // Where the count runs from: the last check-in, or when the interval was last changed
        private DateTime _countFrom;

        public CheckInScheduler(WidgetSession session, LunchBreakDetector lunch, SnoozeManager snooze, TimeProvider clock)
        {
            _session = session;
            _lunch = lunch;
            _snooze = snooze;
            _clock = clock;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => Ticked?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Every second while the day runs, so the idle shape and timer stay current and a due check-in shows.</summary>
        public event EventHandler? Ticked;

        public DateTime Now => _clock.LocalNow();

        /// <summary>The day has started and has not ended.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Where the time not logged yet begins: the last check-in, or the start of the day.</summary>
        public DateTime PeriodStart { get; private set; }

        public DateTime? PausedAt { get; private set; }

        public bool IsPaused => PausedAt != null;

        private TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(1, _session.Settings.CheckInIntervalMinutes));

        // ------------------------------------------------------------------ the day

        public void Start()
        {
            PeriodStart = _countFrom = Now;
            _pauses.Clear();
            PausedAt = null;
            _snooze.Reset();
            ResumeDay();
        }

        public void EndDay()
        {
            Resume();
            IsRunning = false;
            _timer.Stop();
        }

        /// <summary>Carries on after Day ended, as if the day had not been ended.</summary>
        public void ResumeDay()
        {
            IsRunning = true;
            _timer.Start();
        }

        public void Pause()
        {
            if (IsRunning && PausedAt == null)
                PausedAt = Now;
        }

        public void Resume()
        {
            if (PausedAt is not { } from)
                return;
            _pauses.Add((from, Now));
            PausedAt = null;
        }

        /// <summary>A new interval counts from now, so the next check-in moves to a whole interval away.</summary>
        public void IntervalChanged()
        {
            if (IsRunning)
                _countFrom = Now;
        }

        /// <summary>Time up to <paramref name="end"/> was logged, so the next period starts there.</summary>
        public void Logged(DateTime end)
        {
            PeriodStart = _countFrom = end;
            _pauses.RemoveAll(p => p.End <= end);
            _snooze.Reset();
        }

        // ------------------------------------------------------------------ working time

        /// <summary>Lunch breaks and pauses between two times. A pause still running has no end yet.</summary>
        public List<(DateTime Start, DateTime End)> BreaksBetween(DateTime from, DateTime to)
        {
            var breaks = _lunch.BreaksBetween(from, to).ToList();
            breaks.AddRange(_pauses);
            if (PausedAt is { } pausedAt)
                breaks.Add((pausedAt, DateTime.MaxValue));
            return breaks;
        }

        public TimeSpan WorkedBetween(DateTime from, DateTime to) =>
            to > from ? WorkingSpans(from, to, BreaksBetween(from, to)).Aggregate(TimeSpan.Zero, (total, span) => total + (span.End - span.Start))
                : TimeSpan.Zero;

        /// <summary>Work since the last check-in that is not logged yet.</summary>
        public TimeSpan Unlogged => WorkedBetween(PeriodStart, Now);

        /// <summary>
        /// The stretches of work between two times once the breaks are taken out, split the same way
        /// Core splits a day around its pauses, so a period that runs over lunch becomes two entries.
        /// </summary>
        public static IReadOnlyList<(DateTime Start, DateTime End)> WorkingSpans(DateTime from, DateTime to,
            IEnumerable<(DateTime Start, DateTime End)> breaks) =>
            EntryService.SplitAroundPauses(
                breaks.Where(b => b.End > b.Start).Select(b => new SessionPause { StartedAt = b.Start, EndedAt = b.End }), from, to);

        // ------------------------------------------------------------------ the next check-in

        /// <summary>When the current period closes. Null while paused.</summary>
        public DateTime? CheckInAt => IsRunning && !IsPaused ? NextCheckIn(_countFrom, Interval) : null;

        /// <summary>
        /// When a check-in would come due, counting <paramref name="interval"/> of work from
        /// <paramref name="from"/>. Lunch and pauses do not count. Setup shows this before the day starts.
        /// </summary>
        public DateTime? NextCheckIn(DateTime from, TimeSpan interval) => AfterWork(from, interval);

        /// <summary>When the check-in shows: when it is due, or when a snooze ends.</summary>
        public DateTime? PromptAt =>
            CheckInAt is { } due
                ? _snooze.SnoozedUntil is { } until && until > due ? until : due
                : null;

        /// <summary>A check-in is waiting to be answered.</summary>
        public bool IsDue => PromptAt is { } at && at <= Now;

        /// <summary>Until the check-in shows; while paused, the work left in the interval. Null when none is coming.</summary>
        public TimeSpan? TimeLeft
        {
            get
            {
                if (PausedAt is { } pausedAt)
                    return Longer(TimeSpan.Zero, Interval - WorkedBetween(_countFrom, pausedAt));
                return PromptAt is { } at ? Longer(TimeSpan.Zero, at - Now) : null;
            }
        }

        /// <summary>How much of the interval has been worked, 0 to 1: the idle ring.</summary>
        public double Elapsed => Math.Clamp(WorkedBetween(_countFrom, PausedAt ?? Now) / Interval, 0, 1);

        /// <summary>The moment <paramref name="work"/> has been worked since <paramref name="from"/>; null while paused.</summary>
        private DateTime? AfterWork(DateTime from, TimeSpan work)
        {
            var cursor = from;
            var left = work;
            foreach (var gap in BreaksBetween(from, from.AddDays(2)).Where(b => b.End > from).OrderBy(b => b.Start))
            {
                if (gap.Start > cursor)
                {
                    if (gap.Start - cursor >= left)
                        return cursor + left;
                    left -= gap.Start - cursor;
                }
                if (gap.End > cursor)
                    cursor = gap.End;
            }
            return cursor == DateTime.MaxValue ? null : cursor + left;
        }

        private static TimeSpan Longer(TimeSpan a, TimeSpan b) => a > b ? a : b;

        public void Dispose() => _timer.Stop();
    }
}
