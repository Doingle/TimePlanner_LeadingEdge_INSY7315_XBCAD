namespace TimePlanner.Dashboard.Services.Reports
{
    //-----------------------------
    //one bar of a breakdown: a category, a project or a person with their share of the total. Percent is of the whole period and rows are largest first.
    //Colour is the category colour (null for projects and people) and Billable is set for projects, which are billable unless they belong to the internal company
    public record BreakdownRow(string Label, double Hours, int Minutes, double Percent, string? Colour, bool? Billable, int Entries);

    //-----------------------------
    //everything a "where did the time go" screen needs for a period, for one person or for the whole team
    public record ReportSummary(DateTime From, DateTime To, double TotalHours, int TotalMinutes, double BillableHours, double NonBillableHours, int Entries,
        IReadOnlyList<BreakdownRow> ByCategory, IReadOnlyList<BreakdownRow> ByProject, IReadOnlyList<BreakdownRow> ByPerson);
}
//------------------------------EOF-----------------------------\\
