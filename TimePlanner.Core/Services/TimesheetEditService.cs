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
        //adds a manual entry after checking it. An added entry never moves the others, so it has to fit between them
        public async Task<EditResult> AddEntryAsync(int userId, EntryEdit edit, DateTime now)
        {
            edit = ToMinutes(edit);
            var error = ValidateFields(edit, now) ?? (await PlanAsync(userId, edit, null)).Error;

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
        //what saving this edit would do to the entries around it, without storing anything. The entry being edited wins: entries that touch it
        //keep touching (moving the shared edge moves both), entries it now overlaps are shortened, and an entry it would cover completely is listed
        //for removal so the user is asked first
        public async Task<EditPreview> PreviewUpdateAsync(int userId, int entryId, EntryEdit edit, DateTime now)
        {
            edit = ToMinutes(edit);
            var entry = await _entries.GetByIdAsync(entryId);

            //only the owner may change an entry
            if (entry == null || entry.UserId != userId)
            {
                return EditPreview.Fail("Entry not found.");
            }

            var error = ValidateFields(edit, now);

            //a failed check changes nothing
            if (error != null)
            {
                return EditPreview.Fail(error);
            }

            var plan = await PlanAsync(userId, edit, entry);

            if (plan.Error != null)
            {
                return EditPreview.Fail(plan.Error);
            }

            var changes = new List<NeighbourChange>();

            //each affected entry is described by its activity and project, so the user can tell which one it is
            foreach (var p in plan.Changes)
            {
                changes.Add(new NeighbourChange(p.Entry.TimeEntryId, await LabelAsync(p.Entry),
                    EntryService.ToMinute(p.Entry.StartTime), EntryService.ToMinute(p.Entry.EndTime), p.NewStart, p.NewEnd));
            }

            return new EditPreview(true, null, changes);
        }

        //-----------------------------
        //changes an entry and returns its old values. When the edit also changes other entries it only goes ahead once the user has agreed (confirmed),
        //and then everything is saved together or not at all. The result carries what undo needs to put all of it back
        public async Task<EditResult> UpdateEntryAsync(int userId, int entryId, EntryEdit edit, DateTime now, bool confirmed = false)
        {
            edit = ToMinutes(edit);
            var entry = await _entries.GetByIdAsync(entryId);

            //only the owner may change an entry
            if (entry == null || entry.UserId != userId)
            {
                return EditResult.Fail("Entry not found.");
            }

            var error = ValidateFields(edit, now);

            //a failed check changes nothing
            if (error != null)
            {
                return EditResult.Fail(error);
            }

            var plan = await PlanAsync(userId, edit, entry);

            if (plan.Error != null)
            {
                return EditResult.Fail(plan.Error);
            }

            //other entries are never changed without being asked
            if (plan.Changes.Count > 0 && !confirmed)
            {
                return EditResult.Fail("This overlaps another entry.");
            }

            var previous = await ToEditAsync(entry);
            var movedBack = plan.Changes.Where(p => !p.IsRemoval).Select(p => new EntrySpan(p.Entry.TimeEntryId, p.Entry.StartTime, p.Entry.EndTime)).ToList();
            var removed = new List<EntryEdit>();

            foreach (var p in plan.Changes.Where(p => p.IsRemoval))
            {
                removed.Add(await ToEditAsync(p.Entry));
            }

            var task = await ResolveTaskAsync(userId, edit);
            entry.StartTime = edit.Start;
            entry.EndTime = edit.End;
            entry.TaskId = task.TaskID;
            entry.Task = null;
            entry.Note = string.IsNullOrWhiteSpace(edit.Note) ? null : edit.Note.Trim();

            var changed = new List<TimeEntry> { entry };

            foreach (var p in plan.Changes.Where(p => !p.IsRemoval))
            {
                p.Entry.Task = null;
                p.Entry.StartTime = p.NewStart!.Value;
                p.Entry.EndTime = p.NewEnd!.Value;
                changed.Add(p.Entry);
            }

            await _entries.ApplyAsync(changed, plan.Changes.Where(p => p.IsRemoval).Select(p => p.Entry.TimeEntryId), Array.Empty<TimeEntry>());
            return new EditResult(true, null, entry.TimeEntryId, previous, movedBack, removed);
        }

        //-----------------------------
        //puts an edited entry and everything that edit changed back as it was, all together. It refuses when the day has changed in the meantime,
        //so an undo can never overwrite something newer
        public async Task<EditResult> RestoreAsync(int userId, int entryId, EntryEdit previous, IReadOnlyList<EntrySpan> neighbours, IReadOnlyList<EntryEdit> removed)
        {
            var entry = await _entries.GetByIdAsync(entryId);
            const string Changed = "The timesheet has changed since, so this can't be undone.";

            //only the owner may change an entry
            if (entry == null || entry.UserId != userId)
            {
                return EditResult.Fail("Entry not found.");
            }

            var moved = new List<TimeEntry>();

            foreach (var span in neighbours)
            {
                var other = await _entries.GetByIdAsync(span.EntryId);

                //a neighbour that is gone or not the user's cannot be put back
                if (other == null || other.UserId != userId)
                {
                    return EditResult.Fail(Changed);
                }

                other.StartTime = span.Start;
                other.EndTime = span.End;
                moved.Add(other);
            }

            var targets = moved.Select(m => (m.StartTime, m.EndTime))
                .Append((previous.Start, previous.End))
                .Concat(removed.Select(r => (r.Start, r.End)))
                .ToList();

            var dayStart = previous.Start.Date;
            var inTheWay = (await _entries.GetForUserAsync(userId, dayStart, dayStart.AddDays(1)))
                .Where(e => e.TimeEntryId != entryId && moved.All(m => m.TimeEntryId != e.TimeEntryId));

            //anything added since that sits where the old times were stops the undo
            if (inTheWay.Any(e => targets.Any(t => Overlaps(e.StartTime, e.EndTime, t.Item1, t.Item2))))
            {
                return EditResult.Fail(Changed);
            }

            var task = await ResolveTaskAsync(userId, previous);
            entry.StartTime = previous.Start;
            entry.EndTime = previous.End;
            entry.TaskId = task.TaskID;
            entry.Task = null;
            entry.Note = string.IsNullOrWhiteSpace(previous.Note) ? null : previous.Note.Trim();

            var added = new List<TimeEntry>();

            foreach (var r in removed)
            {
                var removedTask = await ResolveTaskAsync(userId, r);
                added.Add(_factory.CreateManual(userId, removedTask.TaskID, r.Start, r.End, r.Note));
            }

            await _entries.ApplyAsync(moved.Prepend(entry), Array.Empty<int>(), added);
            return EditResult.Done(entry.TimeEntryId, null);
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
        //the same edit with the seconds dropped. The editor only shows hours and minutes, and entries are compared to the minute,
        //so 10:00 is the end of an entry that finished at 10:00:37 and the next one can start right there
        private static EntryEdit ToMinutes(EntryEdit edit) => edit with { Start = EntryService.ToMinute(edit.Start), End = EntryService.ToMinute(edit.End) };

        //-----------------------------
        //whether two stretches share any time, compared to the minute. Stretches that only touch do not overlap
        private static bool Overlaps(DateTime startA, DateTime endA, DateTime startB, DateTime endB) =>
            EntryService.ToMinute(startA) < EntryService.ToMinute(endB) && EntryService.ToMinute(endA) > EntryService.ToMinute(startB);

        //-----------------------------
        //a task other entries are known by: its activity and its project
        private async Task<string> LabelAsync(TimeEntry entry)
        {
            var values = await ToEditAsync(entry);
            return $"{string.Join(" > ", values.ActivityPath)} · {values.ProjectPath.LastOrDefault()}";
        }

        private sealed record Planned(TimeEntry Entry, DateTime? NewStart, DateTime? NewEnd)
        {
            //no new times means the entry is removed
            public bool IsRemoval => NewStart == null;
        }

        //-----------------------------
        //works out what the other entries of the day must do to make room for an edit. current is the entry being changed, or null for a new one.
        //A new entry cannot move anything, so any overlap is an error for it. All times are compared to the minute
        private async Task<(string? Error, List<Planned> Changes)> PlanAsync(int userId, EntryEdit edit, TimeEntry? current)
        {
            var changes = new List<Planned>();
            var start = edit.Start;
            var end = edit.End;
            var oldStart = current == null ? (DateTime?)null : EntryService.ToMinute(current.StartTime);
            var oldEnd = current == null ? (DateTime?)null : EntryService.ToMinute(current.EndTime);
            var dayStart = edit.Start.Date;
            var others = (await _entries.GetForUserAsync(userId, dayStart, dayStart.AddDays(1))).Where(e => e.TimeEntryId != current?.TimeEntryId);

            foreach (var other in others)
            {
                var otherStart = EntryService.ToMinute(other.StartTime);
                var otherEnd = EntryService.ToMinute(other.EndTime);
                DateTime newStart = otherStart;
                DateTime newEnd = otherEnd;

                //an entry that touched the old start still touches it: the shared edge moves with this entry
                if (oldStart != null && otherEnd == oldStart && start != oldStart)
                {
                    newEnd = start;
                }
                //the same at the old end
                else if (oldEnd != null && otherStart == oldEnd && end != oldEnd)
                {
                    newStart = end;
                }
                //an entry sharing time with the edit
                else if (otherStart < end && otherEnd > start)
                {
                    //a new entry has to fit around the others
                    if (current == null)
                    {
                        return ("This overlaps another entry.", changes);
                    }

                    //the edit would have to cut this entry in two
                    if (otherStart < start && otherEnd > end)
                    {
                        return ("This sits inside another entry. Change that entry first.", changes);
                    }

                    //the edit covers it completely
                    if (start <= otherStart && otherEnd <= end)
                    {
                        changes.Add(new Planned(other, null, null));
                        continue;
                    }

                    //the edit cuts into one end of it
                    if (otherStart < start)
                    {
                        newEnd = start;
                    }
                    else
                    {
                        newStart = end;
                    }
                }
                else
                {
                    continue;
                }

                //what is left of it has to be a real entry, otherwise it is removed
                changes.Add(newEnd - newStart >= EntryService.MinimumPiece
                    ? new Planned(other, newStart, newEnd)
                    : new Planned(other, null, null));
            }

            return (null, changes);
        }

        //-----------------------------
        //returns the first broken rule of the edit itself, or null
        private static string? ValidateFields(EntryEdit edit, DateTime now)
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
