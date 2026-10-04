namespace TimePlanner.Core.Services
{
    //-----------------------------
    //name rules shared with the dashboard import
    public static class NameRules
    {
        //longest company or project name the import accepts
        public const int MaxCompanyOrProjectLength = 100;

        //characters that start a spreadsheet formula
        private const string FormulaStarts = "=+-@";

        //-----------------------------
        //valid company or project name
        public static bool IsValidCompanyOrProjectName(string? name) => IsSafe(name, MaxCompanyOrProjectLength);

        //-----------------------------
        //valid activity name without path separators
        public static bool IsValidActivityName(string? name) =>
            IsSafe(name, ActivityService.MaxNameLength) && !name!.Contains('>') && !name.Contains('›');

        //-----------------------------
        //present and short with no control characters or formula start
        private static bool IsSafe(string? name, int max)
        {
            var clean = name?.Trim() ?? string.Empty;
            //checks length and characters
            return clean.Length > 0 && clean.Length <= max && !clean.Any(char.IsControl) && !FormulaStarts.Contains(clean[0]);
        }
    }
}
//------------------------------EOF-----------------------------\\
