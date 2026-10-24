using TimePlanner.Core.Services.Models;

namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //assembles prompt inputs from store data
    public static class DayBuildRequestFactory
    {
        //-----------------------------
        //builds a compilation request
        public static DayBuildRequest Create(DateOnly day, IReadOnlyList<VoiceNote> notes, IReadOnlyList<TimesheetSlot> slots,
            IReadOnlyList<(string Company, string Project)> projects, IReadOnlyList<IReadOnlyList<string>> activityPaths)
        {
            var busy = slots
                .Where(s => s.Kind is SlotKind.Entry or SlotKind.Break)
                .Select(s => new TimeRange(s.Start, s.End))
                .ToList();

            var eligibleNotes = notes
                .Where(n => n.Day == day && n.Status == VoiceNoteStatus.Transcribed && !string.IsNullOrWhiteSpace(n.Transcript))
                .OrderBy(n => n.WindowStart)
                .ToList();

            var windows = new List<VoiceWindow>(eligibleNotes.Count);

            //give each eligible note a sequential window identifier
            for (var i = 0; i < eligibleNotes.Count; i++)
            {
                var note = eligibleNotes[i];
                var id = $"W{i + 1}";
                var free = FreeTime.Within(new TimeRange(note.WindowStart, note.WindowEnd), busy);
                windows.Add(new VoiceWindow(id, note.Id, note.WindowStart, note.WindowEnd, free, note.Transcript!.Trim()));
            }

            var catalogProjects = projects
                .Select((p, i) => new CatalogProject($"P{i + 1}", p.Company, p.Project))
                .ToList();

            var catalogActivities = activityPaths
                .Select((a, i) => new CatalogActivity($"A{i + 1}", a))
                .ToList();

            return new DayBuildRequest(day, windows, catalogProjects, catalogActivities);
        }
    }
}
//------------------------------EOF-----------------------------\\
