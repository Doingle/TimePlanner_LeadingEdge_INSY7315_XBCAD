namespace TimePlanner.Core.Services.Models
{
    //-----------------------------
    //latest entry details used to prefill the log form
    public sealed record LastEntryInfo(int ProjectId, int CategoryId, IReadOnlyList<string> ActivityPath, DateTime EndedAt);
}
//------------------------------EOF-----------------------------\\
