namespace TimePlanner.Widget.Models
{
    /// <summary>
    /// "Still on the same task" at a check-in: the client › project and activity of the last entry, while both
    /// are still offered in the pickers.
    /// </summary>
    public sealed record SameTask(IReadOnlyList<string> ProjectPath, IReadOnlyList<string> Activity)
    {
        /// <summary>"Project X · Bug fix": short enough for the button.</summary>
        public string Short => $"{ProjectPath[^1]} · {Activity[^1]}";

        /// <summary>"Contoso › Project X · Coding › Bug fix": the whole of both paths.</summary>
        public string Full => $"{string.Join(" › ", ProjectPath)} · {string.Join(" › ", Activity)}";
    }
}
