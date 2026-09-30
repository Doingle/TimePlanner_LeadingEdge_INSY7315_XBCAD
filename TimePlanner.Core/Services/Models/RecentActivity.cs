namespace TimePlanner.Core.Services.Models
{
    //-----------------------------
    //recently logged activity for the picker
    public sealed record RecentActivity(int CategoryId, IReadOnlyList<string> Path);
}
//------------------------------EOF-----------------------------\\
