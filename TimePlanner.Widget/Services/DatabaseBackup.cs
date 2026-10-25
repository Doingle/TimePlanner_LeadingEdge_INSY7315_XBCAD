using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace TimePlanner.Widget.Services
{
    //-----------------------------
    //copies the local database to a dated file before a migration changes it, and keeps only the newest few copies
    public static class DatabaseBackup
    {
        public const int KeepCount = 5;
        public const string FolderName = "backups";

        //-----------------------------
        //backs up when the database already holds data from earlier migrations and more are waiting. Returns the backup file, or null when there was nothing to protect
        //(a new install, or a database that is already up to date). A failure throws, so the migration never runs without a copy of what it is about to change
        public static async Task<string?> BackUpBeforeMigratingAsync(DbContext db, DateTime now, int keep = KeepCount)
        {
            if (!(await db.Database.GetAppliedMigrationsAsync()).Any() || !(await db.Database.GetPendingMigrationsAsync()).Any())
                return null;

            await db.Database.OpenConnectionAsync();
            try
            {
                var source = (SqliteConnection)db.Database.GetDbConnection();
                var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source.DataSource))!, FolderName);
                Directory.CreateDirectory(folder);

                var name = Path.GetFileNameWithoutExtension(source.DataSource);
                var backup = Path.Combine(folder, $"{name}-{now:yyyyMMdd-HHmmss}.db");

                //written under a temporary name, so a copy that stops half way is never mistaken for a good backup
                var partial = backup + ".partial";
                try
                {
                    //no pooling, so the file is released the moment the copy is done and can be renamed
                    using (var destination = new SqliteConnection($"Data Source={partial};Pooling=False"))
                        source.BackupDatabase(destination);
                    File.Move(partial, backup, overwrite: true);
                }
                catch
                {
                    TryDelete(partial);
                    throw;
                }

                Prune(folder, name, keep);
                return backup;
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }

        //-----------------------------
        //deletes all but the newest backups of this database. The names start with the date, so the newest sort last. Other files in the folder are never touched
        private static void Prune(string folder, string name, int keep)
        {
            var old = Directory.GetFiles(folder, $"{name}-*.db")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Skip(keep);

            foreach (var file in old)
                TryDelete(file);
        }

        //an old backup that cannot be removed right now must not stop the widget from starting
        private static void TryDelete(string file)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
