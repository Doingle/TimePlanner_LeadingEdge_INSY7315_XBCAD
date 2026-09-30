namespace TimePlanner.Core.Data
{
    //-----------------------------
    //finds the local database file
    public static class DatabasePaths
    {
        //-----------------------------
        //path under local app data with its folder created
        public static string GetDefaultDatabasePath()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimePlanner");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "timeplanner.db");
        }

        //-----------------------------
        public static string GetDefaultConnectionString() => $"Data Source={GetDefaultDatabasePath()}";
    }
}
//------------------------------EOF-----------------------------\\
