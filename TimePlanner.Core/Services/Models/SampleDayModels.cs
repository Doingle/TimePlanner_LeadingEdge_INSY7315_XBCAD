namespace TimePlanner.Core.Services.Models
{
    //-----------------------------
    //one row read from a sample day csv file
    public sealed record SampleRow(int Line, string Task, string ClientProject, TimeOnly Start, TimeOnly End, double Hours, string ActivityPath, string Billable);

    //-----------------------------
    //result of loading a sample day
    public sealed record SampleDayResult(bool Loaded, DateOnly Day, int Entries, string Message);
}
//------------------------------EOF-----------------------------\\
