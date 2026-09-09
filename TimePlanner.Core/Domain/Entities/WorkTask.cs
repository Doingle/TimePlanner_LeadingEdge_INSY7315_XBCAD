using System;
using System.Collections.Generic;
using System.Text;

namespace TimePlanner.Core.Domain.Entities
{
    //-----------------------------
    //this class represents a task which is connected to a project and company within TimePlanner
    //design decision to change planned name Task to WorkTask avoiding system naming conflict
    public class WorkTask
    {
        public int TaskID { get; set; }
        public int ProjectID { get; set; }
        public Project? Project { get; set; }
        public int AssignedUserID { get; set; }
        public AppUser? AssignedUser { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        //this collection holds the time entries associated with each task
        public ICollection<TimeEntry> TimeEntries { get; set; } = new List<TimeEntry>();

        //-----------------------------
        //this method adds time entries to a task, sets taskid and reference 
        public void AddTimeEntry(TimeEntry entry)
        {
            entry.TaskId = TaskID;
            entry.Task = this;
            TimeEntries.Add(entry);
        }

        //-----------------------------
        //this reporting method calculates an aggregate of all logged time for a specific task
        public TimeSpan GetTotalLoggedTime()
        {
            var total = TimeSpan.Zero;

            foreach (var entry in TimeEntries)
            {
                total += entry.GetDuration();
            }
            return total;
        }
    }
}
//------------------------------EOF-----------------------------\\
