namespace TimePlanner.Core.Services.Models
{
    //-----------------------------
    //what one timesheet row shows
    public enum SlotKind
    {
        Entry,
        Gap,
        Break
    }

    //-----------------------------
    //one row of a day: an entry or a gap or a break
    public sealed record TimesheetSlot(
        SlotKind Kind,
        DateTime Start,
        DateTime End,
        int? EntryId,
        string Company,
        string Project,
        IReadOnlyList<string> ActivityPath,
        string? Note,
        bool Billable);

    //-----------------------------
    //the values a user edits on one entry
    public sealed record EntryEdit(
        DateTime Start,
        DateTime End,
        IReadOnlyList<string> ProjectPath,
        IReadOnlyList<string> ActivityPath,
        string? Note);

    //-----------------------------
    //the times of one entry, used to put a neighbour back where it was
    public sealed record EntrySpan(int EntryId, DateTime Start, DateTime End);

    //-----------------------------
    //result of an edit with what undo needs. When the edit moved or removed neighbouring entries, undo also needs where they were
    public sealed record EditResult(bool Ok, string? Error, int? EntryId, EntryEdit? Previous,
        IReadOnlyList<EntrySpan>? ChangedNeighbours = null, IReadOnlyList<EntryEdit>? RemovedNeighbours = null)
    {
        //-----------------------------
        //a refused edit with its reason
        public static EditResult Fail(string error) => new(false, error, null, null);

        //-----------------------------
        //a saved edit with the old values
        public static EditResult Done(int? entryId, EntryEdit? previous) => new(true, null, entryId, previous);
    }

    //-----------------------------
    //another entry an edit would change. New times are null when the edit would remove the entry
    public sealed record NeighbourChange(int EntryId, string Label, DateTime OldStart, DateTime OldEnd, DateTime? NewStart, DateTime? NewEnd)
    {
        public bool IsRemoval => NewStart == null;
    }

    //-----------------------------
    //what saving an edit would do to the entries around it, shown to the user before anything is stored
    public sealed record EditPreview(bool Ok, string? Error, IReadOnlyList<NeighbourChange> Changes)
    {
        //-----------------------------
        //true when the user has to agree to changes in other entries before the edit can be saved
        public bool NeedsConfirmation => Ok && Changes.Count > 0;

        public static EditPreview Fail(string error) => new(false, error, Array.Empty<NeighbourChange>());
    }
}
//------------------------------EOF-----------------------------\\
