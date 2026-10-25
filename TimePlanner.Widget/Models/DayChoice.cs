using TimePlanner.Core.Sync;

namespace TimePlanner.Widget.Models
{
    /// <summary>One day in the timesheet's day list: what was logged, and its last send if it was sent.</summary>
    public sealed record DayChoice(DateOnly Day, TimeSpan Logged, int Entries, SentDay? Sent);

    /// <summary>An earlier day with logged time that was never sent.</summary>
    public sealed record UnsentDay(DateOnly Day, TimeSpan Logged, int Entries);
}
