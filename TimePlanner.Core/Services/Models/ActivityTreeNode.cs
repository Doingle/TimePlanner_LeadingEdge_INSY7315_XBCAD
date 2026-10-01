namespace TimePlanner.Core.Services.Models
{
    //-----------------------------
    //picker node with colour and billing from its root
    public sealed record ActivityTreeNode(
        int CategoryId,
        string Name,
        IReadOnlyList<string> Path,
        string Colour,
        bool IsBillable,
        IReadOnlyList<ActivityTreeNode> Children);
}
//------------------------------EOF-----------------------------\\
