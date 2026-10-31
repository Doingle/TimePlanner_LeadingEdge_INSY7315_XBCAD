using Microsoft.Extensions.Logging;
using TimePlanner.Core.Sync;

namespace TimePlanner.Widget.Services;

/// <summary>
/// "Timesheet not sent yet" on Day ended: shown once the user ends a working day that isn't sent, until it is.
/// Never sends data.
/// </summary>
public sealed class UnsentDayReminder(ISendHistoryStore history, TimeProvider clock, ILogger<UnsentDayReminder> logger)
{
    private DateOnly? _checkedDay;
    private DateOnly? _sentDay;
    private DateTimeOffset _nextCheck;
    private bool _checking;
    private bool _dayEnded;

    public DateOnly Today => DateOnly.FromDateTime(clock.LocalNow());
    public bool IsVisible { get; private set; }
    public event EventHandler? Changed;

    /// <summary>Which days count as working days. Monday to Friday until the widget's preferences say otherwise.</summary>
    public Func<DayOfWeek, bool> IsWorkingDay { get; set; } = day => day is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    private bool Eligible => _dayEnded
        && IsWorkingDay(Today.DayOfWeek)
        && _sentDay != Today;

    /// <summary>
    /// Uses the existing UI tick. Nothing is read until today has ended; then local history is read at most
    /// once a minute, one read at a time.
    /// </summary>
    public async Task RefreshAsync(bool dayEnded)
    {
        _dayEnded = dayEnded;
        var today = Today;
        if (_checkedDay != today)
        {
            _checkedDay = today;
            _nextCheck = DateTimeOffset.MinValue;
            SetVisible(false);
        }

        if (!Eligible)
        {
            // Ending the day again (after Resume day) checks straight away
            _nextCheck = DateTimeOffset.MinValue;
            SetVisible(false);
            return;
        }
        if (_checking || clock.GetUtcNow() < _nextCheck)
            return;

        _checking = true;
        _nextCheck = clock.GetUtcNow().AddMinutes(1);
        try
        {
            var sent = await history.GetAsync();
            // A read may finish after midnight, a resume or a successful send
            if (Today != today)
                return;
            if (sent.Any(day => day.Day == today))
                _sentDay = today;
            SetVisible(Eligible);
        }
        catch (Exception ex)
        {
            // Unknown status is not evidence of an unsent day. Retry on a later tick.
            SetVisible(false);
            logger.LogWarning(ex, "Could not check the local timesheet send history.");
        }
        finally
        {
            _checking = false;
        }
    }

    public void DaySent(DateOnly day)
    {
        if (day != Today)
            return;
        _sentDay = day;
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (IsVisible == visible)
            return;
        IsVisible = visible;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
