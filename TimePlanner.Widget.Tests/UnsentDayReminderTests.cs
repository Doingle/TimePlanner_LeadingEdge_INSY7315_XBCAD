using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using TimePlanner.Core.Sync;
using TimePlanner.Widget.Services;

namespace TimePlanner.Widget.Tests;

public sealed class ReminderClock : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => UtcNow;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Test local", TimeSpan.FromHours(2), "Test local", "Test local");
}

public sealed class ReminderHistory : ISendHistoryStore
{
    public List<SentDay> Days { get; } = [];
    public int Reads { get; private set; }
    public Func<Task<IReadOnlyList<SentDay>>>? Read { get; set; }
    public Task<IReadOnlyList<SentDay>> GetAsync()
    {
        Reads++;
        return Read?.Invoke() ?? Task.FromResult<IReadOnlyList<SentDay>>(Days.ToArray());
    }
    public Task AddAsync(SentDay sent) { Days.Add(sent); return Task.CompletedTask; }
    public Task MarkChangedAsync(DateOnly day, DateTime at) => Task.CompletedTask;
}

public class UnsentDayReminderTests
{
    private readonly ReminderClock _clock = new();
    private readonly ReminderHistory _history = new();
    private UnsentDayReminder Create() => new(_history, _clock, NullLogger<UnsentDayReminder>.Instance);

    [Fact]
    public async Task OnlyShowsOnceTheDayHasEnded_WithoutReadingHistoryBefore()
    {
        var reminder = Create();
        await reminder.RefreshAsync(false);
        Assert.False(reminder.IsVisible);
        Assert.Equal(0, _history.Reads);
        await reminder.RefreshAsync(true);
        Assert.True(reminder.IsVisible);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(25)]
    public async Task WeekendsNeverReadHistoryOrShow(int day)
    {
        _clock.UtcNow = new DateTimeOffset(2026, 10, day, 8, 0, 0, TimeSpan.Zero);
        var reminder = Create();
        await reminder.RefreshAsync(true);
        Assert.False(reminder.IsVisible);
        Assert.Equal(0, _history.Reads);
    }

    [Fact]
    public async Task UsesLocalDateAcrossMidnight_NotUtcDate()
    {
        var reminder = Create();
        await reminder.RefreshAsync(true);
        Assert.True(reminder.IsVisible);
        _clock.UtcNow = new DateTimeOffset(2026, 10, 23, 22, 0, 0, TimeSpan.Zero);
        await reminder.RefreshAsync(true);
        Assert.Equal(new DateOnly(2026, 10, 24), reminder.Today);
        Assert.False(reminder.IsVisible);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SentTodayStaysHidden_EvenIfEditedAfterSending(bool changed)
    {
        _history.Days.Add(new(new DateOnly(2026, 10, 23), _clock.LocalNow(), 2, 1,
            changed ? _clock.LocalNow().AddMinutes(5) : null));
        var reminder = Create();
        await reminder.RefreshAsync(true);
        Assert.False(reminder.IsVisible);
    }

    [Fact]
    public async Task SendingYesterdayDoesNotSuppressToday()
    {
        var reminder = Create();
        await reminder.RefreshAsync(true);
        reminder.DaySent(reminder.Today.AddDays(-1));
        Assert.True(reminder.IsVisible);
        reminder.DaySent(reminder.Today);
        Assert.False(reminder.IsVisible);
        await reminder.RefreshAsync(true);
        Assert.False(reminder.IsVisible);
    }

    [Fact]
    public async Task PollingDoesNotRepeatNotifications_AndNoticesHistoryChanges()
    {
        var reminder = Create();
        var changes = 0;
        reminder.Changed += (_, _) => changes++;
        for (var i = 0; i < 30; i++)
        {
            await reminder.RefreshAsync(true);
            _clock.UtcNow += TimeSpan.FromSeconds(1);
        }
        Assert.Equal(1, changes);
        Assert.Equal(1, _history.Reads);
        _history.Days.Add(new(reminder.Today, _clock.LocalNow(), 1, 1));
        _clock.UtcNow += TimeSpan.FromMinutes(1);
        await reminder.RefreshAsync(true);
        Assert.False(reminder.IsVisible);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task ResumeHidesIt_EndingAgainChecksStraightAway()
    {
        var reminder = Create();
        await reminder.RefreshAsync(true);
        Assert.True(reminder.IsVisible);

        await reminder.RefreshAsync(false);
        Assert.False(reminder.IsVisible);

        // Ended again within the minute: read again rather than wait
        await reminder.RefreshAsync(true);
        Assert.True(reminder.IsVisible);
        Assert.Equal(2, _history.Reads);
    }

    [Fact]
    public async Task UnavailableHistoryIsQuiet_AndRetriesAfterOneMinute()
    {
        _history.Read = () => throw new IOException("Test unavailable history");
        var reminder = Create();
        await reminder.RefreshAsync(true);
        Assert.False(reminder.IsVisible);
        await reminder.RefreshAsync(true);
        Assert.Equal(1, _history.Reads);
        _history.Read = null;
        _clock.UtcNow += TimeSpan.FromMinutes(1);
        await reminder.RefreshAsync(true);
        Assert.True(reminder.IsVisible);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingReadDoesNotBlockOtherTicks_OrUndoSendOrResume(bool resume)
    {
        var pending = new TaskCompletionSource<IReadOnlyList<SentDay>>();
        _history.Read = () => pending.Task;
        var reminder = Create();
        var read = reminder.RefreshAsync(true);
        Assert.False(read.IsCompleted);
        await reminder.RefreshAsync(true);
        Assert.Equal(1, _history.Reads);
        if (resume)
            await reminder.RefreshAsync(false);
        else
            reminder.DaySent(reminder.Today);
        pending.SetResult([]);
        await read;
        Assert.False(reminder.IsVisible);
    }

    [Fact]
    public async Task PendingReadFromYesterdayCannotShowOnWeekend()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<SentDay>>();
        _history.Read = () => pending.Task;
        var reminder = Create();
        var read = reminder.RefreshAsync(true);
        _clock.UtcNow = _clock.UtcNow.AddDays(1);
        await reminder.RefreshAsync(true);
        pending.SetResult([]);
        await read;
        Assert.False(reminder.IsVisible);
    }
}
