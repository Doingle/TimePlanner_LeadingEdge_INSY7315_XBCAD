namespace TimePlanner.Core.Services.Models
{
    //-----------------------------
    //flat lookup of one activity with its root details
    public sealed record ActivityInfo(
        int CategoryId,
        IReadOnlyList<string> Path,
        int RootCategoryId,
        string RootName,
        string Colour,
        bool IsBillable);
}
//------------------------------EOF-----------------------------\\
