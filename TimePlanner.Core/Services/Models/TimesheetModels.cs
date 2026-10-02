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
    //result of an edit with what undo needs
    public sealed record EditResult(bool Ok, string? Error, int? EntryId, EntryEdit? Previous)
    {
        //-----------------------------
        //a refused edit with its reason
        public static EditResult Fail(string error) => new(false, error, null, null);

        //-----------------------------
        //a saved edit with the old values
        public static EditResult Done(int? entryId, EntryEdit? previous) => new(true, null, entryId, previous);
    }
}
//------------------------------EOF-----------------------------\\
