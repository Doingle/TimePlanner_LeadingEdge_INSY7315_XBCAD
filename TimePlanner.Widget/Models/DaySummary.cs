using System;
using System.Collections.Generic;
using System.Text;
using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Widget.Models
{
    public sealed record DaySummary(DateTime Day, IReadOnlyList<TimeEntry> Entries)
    {
        public TimeSpan Logged => Entries.Aggregate(TimeSpan.Zero, (total, entry) => total + entry.GetDuration());
    }
}
