using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;

namespace TimePlanner.Core.Services
{
    public class TimeEntryFactory
    {
        public static readonly TimeSpan MinimumLength = TimeSpan.FromMinutes(1);


        public List<TimeEntry> Create(int userId, int taskId, DateTime start, DateTime end, string? note, EntryMethod method,IEnumerable<(DateTime Start, DateTime End)> breaks)
        {
            return WorkingSpans(start, end, breaks)
                .Where(span => span.End - span.Start >= MinimumLength)
                .Select(span => new TimeEntry
                {
                    UserId = userId,
                    TaskId = taskId,
                    StartTime = span.Start,
                    EndTime = span.End,
                    Note = note,
                    Method = method
                })
                .ToList();
        }



        public static List<(DateTime Start, DateTime End)> WorkingSpans(DateTime start, DateTime end, IEnumerable<(DateTime Start, DateTime End)> breaks)
        {
            var spans = new List<(DateTime Start, DateTime End)>();
            var cursor = start;

            foreach (var gap in breaks.Where(b => b.End > b.Start && b.End > start && b.Start < end).OrderBy(b => b.Start))
            {
                if (gap.Start > cursor)
                    spans.Add((cursor, gap.Start));

                if (gap.End > cursor)
                    cursor = gap.End;
            }

            if (end > cursor)
                spans.Add((cursor, end));

            return spans;
        }



        public static TimeSpan WorkedTime(DateTime start, DateTime end, IEnumerable<(DateTime Start, DateTime End)> breaks)
        {
            return WorkingSpans(start, end, breaks).Aggregate(TimeSpan.Zero, (total, span) => total + (span.End - span.Start));
        }
    }
}
