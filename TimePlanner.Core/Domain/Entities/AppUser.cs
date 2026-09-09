using System;
using System.Collections.Generic;
using System.Linq;
using TimePlanner.Core.Domain.Enums;

namespace TimePlanner.Core.Domain.Entities
{
    //-----------------------------
    //this class represents a TimePlanner user. 
    public class AppUser
    {
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public UserRole Role { get; set; }

        public ICollection<WorkTask> AssignedTasks { get; set; } = new List<WorkTask>();
        public ICollection<TimeEntry> TimeEntries { get; set; } = new List<TimeEntry>();
        public ICollection<TimeSheet> TimeSheets { get; set; } = new List<TimeSheet>();

        //-----------------------------
        //this list returns currently active tasks to the user
        public List<WorkTask> GetActiveTasks()
        {
            return AssignedTasks
                .Where(t => !string.Equals(t.Status, "Done", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        //-----------------------------
        //this method builds a TimeSheet from this user's TimeEntries within the given period
        public TimeSheet GenerateTimeSheet(DateTime start, DateTime end)
        {
            var relevantEntries = TimeEntries
                .Where(e => e.StartTime >= start && e.EndTime <= end)
                .ToList();

            var totalHours = relevantEntries.Sum(e => e.GetDuration().TotalHours);

            var projectId = relevantEntries
                .Select(e => e.Task?.ProjectID)
                .FirstOrDefault() ?? 0;

            return new TimeSheet
            {
                UserId = UserId,
                ProjectId = projectId,
                PeriodStart = start,
                PeriodEnd = end,
                TotalHours = (decimal)totalHours
            };
        }
    }
}
//------------------------------EOF-----------------------------\\