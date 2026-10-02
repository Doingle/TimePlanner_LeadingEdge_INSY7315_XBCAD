using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services.Models;

namespace TimePlanner.Core.Services
{
    //-----------------------------
    //reads and corrects one day of the user's timesheet
    public class TimesheetEditService
    {
        //shortest untracked stretch shown as a gap
        public static readonly TimeSpan MinimumGap = TimeSpan.FromMinutes(5);

        //longest note an edit accepts
        public const int MaxNoteLength = 500;

        private readonly ITimeEntryRepository _entries;
        private readonly IWorkTaskRepository _tasks;
        private readonly IProjectRepository _projects;
        private readonly IDaySessionRepository _sessions;
        private readonly ActivityService _activities;
        private readonly EntryService _entryService;
        private readonly TimeEntryFactory _factory;

        public TimesheetEditService(
            ITimeEntryRepository entries,
            IWorkTaskRepository tasks,
            IProjectRepository projects,
            IDaySessionRepository sessions,
            ActivityService activities,
            EntryService entryService,
            TimeEntryFactory factory)
        {
            _entries = entries;
            _tasks = tasks;
            _projects = projects;
            _sessions = sessions;
            _activities = activities;
            _entryService = entryService;
            _factory = factory;
        }

        //-----------------------------
        //entries gaps and breaks for one day in time order
        public async Task<IReadOnlyList<TimesheetSlot>> GetDayAsync(int userId, DateOnly day, DateTime now)
        {
            var from = day.ToDateTime(TimeOnly.MinValue);
            var to = from.AddDays(1);
            var entries = await _entries.GetForUserAsync(userId, from, to);
            var sessions = await _sessions.GetForUserBetweenAsync(userId, from, to);
            var lookup = await _activities.GetActivityLookupAsync();
            var companies = (await _projects.GetAllAsync()).ToDictionary(p => p.ProjectID, p => p.Company?.Name ?? string.Empty);
            var slots = new List<TimesheetSlot>();

            //each entry becomes a row
            foreach (var e in entries)
            {
                var info = lookup[e.Task!.CategoryId];
                var company = companies.GetValueOrDefault(e.Task.ProjectID, string.Empty);
                slots.Add(new TimesheetSlot(SlotKind.Entry, e.StartTime, e.EndTime, e.TimeEntryId, company,
                    e.Task.Project?.Name ?? string.Empty, info.Path, e.Note, BillingRules.IsBillable(company, info.IsBillable)));
            }

            //each tracked stretch is checked for breaks and gaps
            foreach (var session in sessions)
            {
                var start = session.StartedAt > from ? session.StartedAt : from;
                var end = (session.EndedAt ?? now) < to ? (session.EndedAt ?? now) : to;

                //finished breaks show as their own rows
                foreach (var pause in session.Pauses.Where(p => p.EndedAt != null))
                {
                    var pStart = pause.StartedAt > start ? pause.StartedAt : start;
                    var pEnd = pause.EndedAt!.Value < end ? pause.EndedAt.Value : end;

                    //only breaks inside this day count
                    if (pEnd > pStart)
                    {
                        slots.Add(Empty(SlotKind.Break, pStart, pEnd));
                    }
                }

                //untracked time inside working pieces becomes a gap
                foreach (var piece in EntryService.SplitAroundPauses(session.Pauses, start, end))
                {
                    var cursor = piece.Start;

                    //entries in the piece move the cursor forward
                    foreach (var e in entries.Where(e => e.EndTime > piece.Start && e.StartTime < piece.End).OrderBy(e => e.StartTime))
                    {
                        //space before this entry is a gap
                        if (e.StartTime - cursor >= MinimumGap)
                        {
                            slots.Add(Empty(SlotKind.Gap, cursor, e.StartTime));
                        }

                        //overlapping entries never move it back
                        if (e.EndTime > cursor)
                        {
                            cursor = e.EndTime;
                        }
                    }

                    //space after the last entry is a gap
                    if (piece.End - cursor >= MinimumGap)
                    {
                        slots.Add(Empty(SlotKind.Gap, cursor, piece.End));
                    }
                }
            }

            return slots.OrderBy(s => s.Start).ThenBy(s => s.Kind).ToList();
        }

        //-----------------------------
        //adds a manual entry after checking it
        public async Task<EditResult> AddEntryAsync(int userId, EntryEdit edit, DateTime now)
        {
            var error = await ValidateAsync(userId, edit, now, null);

            //a failed check stores nothing
            if (error != null)
            {
                return EditResult.Fail(error);
            }

            var task = await ResolveTaskAsync(userId, edit);
            var entry = _factory.CreateManual(userId, task.TaskID, edit.Start, edit.End, edit.Note);
            await _entries.AddAsync(entry);
            return EditResult.Done(entry.TimeEntryId, null);
        }

        //-----------------------------
        //changes an entry and returns its old values
        public async Task<EditResult> UpdateEntryAsync(int userId, int entryId, EntryEdit edit, DateTime now)
        {
            var entry = await _entries.GetByIdAsync(entryId);

            //only the owner may change an entry
            if (entry == null || entry.UserId != userId)
            {
                return EditResult.Fail("Entry not found.");
            }

            var error = await ValidateAsync(userId, edit, now, entryId);

            //a failed check changes nothing
            if (error != null)
            {
                return EditResult.Fail(error);
            }

            var previous = await ToEditAsync(entry);
            var task = await ResolveTaskAsync(userId, edit);
            entry.StartTime = edit.Start;
            entry.EndTime = edit.End;
            entry.TaskId = task.TaskID;
            entry.Task = null;
            entry.Note = string.IsNullOrWhiteSpace(edit.Note) ? null : edit.Note.Trim();
            await _entries.UpdateAsync(entry);
            return EditResult.Done(entry.TimeEntryId, previous);
        }

        //-----------------------------
        //deletes an entry and returns its old values
        public async Task<EditResult> DeleteEntryAsync(int userId, int entryId)
        {
            var entry = await _entries.GetByIdAsync(entryId);

            //only the owner may delete an entry
            if (entry == null || entry.UserId != userId)
            {
                return EditResult.Fail("Entry not found.");
            }

            var previous = await ToEditAsync(entry);
            await _entries.DeleteAsync(entryId);
            return EditResult.Done(null, previous);
        }

        //-----------------------------
        //returns the first broken rule or null
        private async Task<string?> ValidateAsync(int userId, EntryEdit edit, DateTime now, int? ignoreId)
        {
            //end must come after start
            if (edit.End <= edit.Start)
            {
                return "End must be after start.";
            }

            //very short pieces are not entries
            if (edit.End - edit.Start < EntryService.MinimumPiece)
            {
                return "An entry must be at least one minute.";
            }

            //an entry stays inside one day
            if (edit.End.Date != edit.Start.Date && edit.End != edit.Start.Date.AddDays(1))
            {
                return "An entry must stay within one day.";
            }

            //time cannot be logged ahead of the clock
            if (edit.End > now)
            {
                return "An entry can't end in the future.";
            }

            //client and project must both be valid
            if (edit.ProjectPath.Count != 2 || !edit.ProjectPath.All(NameRules.IsValidCompanyOrProjectName))
            {
                return "Choose a client and project.";
            }

            //an activity is required
            if (edit.ActivityPath.Count == 0)
            {
                return "Choose an activity.";
            }

            //long notes are refused
            if (edit.Note?.Trim().Length > MaxNoteLength)
            {
                return $"A note can be at most {MaxNoteLength} characters.";
            }

            var dayStart = edit.Start.Date;
            var others = await _entries.GetForUserAsync(userId, dayStart, dayStart.AddDays(1));

            //two entries cannot cover the same minute
            if (others.Any(e => e.TimeEntryId != ignoreId && e.StartTime < edit.End && e.EndTime > edit.Start))
            {
                return "This overlaps another entry.";
            }

            return null;
        }

        //-----------------------------
        //finds or creates the task for an edit
        private async Task<WorkTask> ResolveTaskAsync(int userId, EntryEdit edit)
        {
            var project = await _entryService.AddProjectAsync(edit.ProjectPath[0].Trim(), edit.ProjectPath[1].Trim(), null);
            var categoryId = await _activities.FindOrAddPathAsync(edit.ActivityPath);
            return await _entryService.FindOrCreateTaskAsync(userId, project.ProjectID, categoryId);
        }

        //-----------------------------
        //the current values of an entry for undo
        private async Task<EntryEdit> ToEditAsync(TimeEntry entry)
        {
            var task = await _tasks.GetByIdAsync(entry.TaskId);
            var project = task == null ? null : await _projects.GetByIdAsync(task.ProjectID);
            var activity = task == null ? Array.Empty<string>() : await _activities.GetPathAsync(task.CategoryId);
            IReadOnlyList<string> projectPath = project == null ? Array.Empty<string>() : new[] { project.Company?.Name ?? string.Empty, project.Name };
            return new EntryEdit(entry.StartTime, entry.EndTime, projectPath, activity, entry.Note);
        }

        //-----------------------------
        //a gap or break row with no entry
        private static TimesheetSlot Empty(SlotKind kind, DateTime start, DateTime end) =>
            new(kind, start, end, null, string.Empty, string.Empty, Array.Empty<string>(), null, false);
    }
}
//------------------------------EOF-----------------------------\\
