namespace TimePlanner.Widget.Models
{
    /// <summary>Which list an item in Settings → Manage lists belongs to.</summary>
    public enum ListKind
    {
        Client,
        Project,
        Activity,
    }

    public sealed record ManagedProject(int Id, string Name, bool Hidden, bool Locked);

    /// <summary>A client is hidden when every one of its projects is; internal work is locked.</summary>
    public sealed record ManagedClient(int Id, string Name, bool Locked, IReadOnlyList<ManagedProject> Projects)
    {
        public bool Hidden => Projects.Count > 0 && Projects.All(p => p.Hidden);
    }

    /// <summary>One node of the activity tree, hidden ones included. Depth 1 is a main activity.</summary>
    public sealed record ManagedActivity(int Id, string Name, int Depth, bool Hidden, IReadOnlyList<ManagedActivity> Children)
    {
        public bool Locked => Depth == 1;
    }

    /// <summary>Everything the pickers can show, and what is hidden from them.</summary>
    public sealed record ManagedLists(IReadOnlyList<ManagedClient> Clients, IReadOnlyList<ManagedActivity> Activities);
}
