using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;

namespace TimePlanner.Core.Services
{
    //-----------------------------
    //builds entries for each capture path in one place
    public class TimeEntryFactory
    {
        //longest note accepted
        public const int MaxNoteLength = 2000;

        //-----------------------------
        //entry typed in by the user
        public TimeEntry CreateManual(int userId, int taskId, DateTime start, DateTime end, string? note) =>
            Build(userId, taskId, start, end, note, EntryMethod.Manual);

        //-----------------------------
        //entry saved from a check in prompt
        public TimeEntry CreateAutoPrompted(int userId, int taskId, DateTime start, DateTime end, string? note) =>
            Build(userId, taskId, start, end, note, EntryMethod.AutoPrompted);

        //-----------------------------
        //background entry with no note
        public TimeEntry CreateAutoTracked(int userId, int taskId, DateTime start, DateTime end) =>
            Build(userId, taskId, start, end, null, EntryMethod.AutoTracked);

        //-----------------------------
        //checks times and note then creates the entry
        private static TimeEntry Build(int userId, int taskId, DateTime start, DateTime end, string? note, EntryMethod method)
        {
            //an entry must cover some time
            if (end <= start)
            {
                throw new ArgumentException("End must be after start.", nameof(end));
            }

            //blank notes are stored as null
            var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

            //long notes are rejected not cut
            if (cleanNote != null && cleanNote.Length > MaxNoteLength)
            {
                throw new ArgumentException("Note is too long.", nameof(note));
            }

            return new TimeEntry
            {
                UserId = userId,
                TaskId = taskId,
                StartTime = start,
                EndTime = end,
                Note = cleanNote,
                Method = method
            };
        }
    }
}
//------------------------------EOF-----------------------------\\
