using System;
using System.Collections.Generic;
using System.Text;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;

//-----------------------------
//this class represents a time entry which is connected to a task within TimePlanner
namespace TimePlanner.Core.Domain.Entities
{
    public class TimeEntry
    {
        public int TimeEntryId { get; set; }

        public int UserId { get; set; }
        public AppUser? User { get; set; }

        public int TaskId { get; set; }
        public WorkTask? Task { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string? Note { get; set; }
        public EntryMethod Method { get; set; }

        //-----------------------------
        //calculates the duration of a time entry
        public TimeSpan GetDuration() => EndTime - StartTime;
    }
}
//------------------------------EOF-----------------------------\\
