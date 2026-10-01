namespace TimePlanner.Dashboard.Services.TimesheetImport
{
    //-----------------------------
    //one worked period as a client sends it. Everything is nullable so a missing field becomes a clear row error instead of a generic 400.
    //there is no user field on purpose: the owner of every entry is the logged in caller
    public record ImportEntry(string? Company, string? Project, string? Activity, DateTime? Start, DateTime? End, string? Note, string? Method);

    //-----------------------------
    //Row is the spreadsheet row number for a csv (the header is row 1) or the position in the list for json, 0 means the whole file
    public record ImportRowError(int Row, string Message);

    //-----------------------------
    //the outcome of an import. Nothing is stored unless Errors is empty
    public record ImportResult(int Created, int Skipped, IReadOnlyList<ImportRowError> Errors)
    {
        public bool Success => Errors.Count == 0;

        public static ImportResult Failed(int row, string message) => new(0, 0, new[] { new ImportRowError(row, message) });
    }
}
//------------------------------EOF-----------------------------\\
