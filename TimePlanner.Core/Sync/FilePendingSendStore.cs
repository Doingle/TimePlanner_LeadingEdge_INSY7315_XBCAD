using System.Text.Json;

namespace TimePlanner.Core.Sync
{
    //-----------------------------
    //pending sends kept as a json file, so a day still waits after the widget restarts
    public sealed class FilePendingSendStore : IPendingSendStore
    {
        private readonly string _path;

        //one read or write of the file at a time
        private readonly SemaphoreSlim _gate = new(1, 1);

        public FilePendingSendStore(string path) => _path = path;

        //-----------------------------
        //reads the waiting days, oldest first
        private async Task<List<PendingDay>> ReadAsync()
        {
            //no file means nothing is waiting
            if (!File.Exists(_path))
            {
                return new List<PendingDay>();
            }

            try
            {
                var json = await File.ReadAllBytesAsync(_path);
                return (JsonSerializer.Deserialize<List<PendingDay>>(json) ?? new List<PendingDay>()).OrderBy(d => d.Day).ToList();
            }
            //an unreadable file means nothing is waiting, the days themselves are still saved locally and can be sent again
            catch (JsonException)
            {
                return new List<PendingDay>();
            }
        }

        //-----------------------------
        //writes the list under a temporary name first, so a crash half way never leaves a broken file
        private async Task WriteAsync(List<PendingDay> days)
        {
            var dir = Path.GetDirectoryName(_path);

            //creates the local storage directory when missing
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var temporary = _path + ".tmp";
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(days));
            File.Move(temporary, _path, overwrite: true);
        }

        //-----------------------------
        public async Task<IReadOnlyList<PendingDay>> GetAsync()
        {
            await _gate.WaitAsync();
            try
            {
                return await ReadAsync();
            }
            //the gate opens even when reading fails
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        public async Task SaveAsync(PendingDay pending)
        {
            await _gate.WaitAsync();
            try
            {
                var days = await ReadAsync();
                days.RemoveAll(d => d.Day == pending.Day);
                days.Add(pending);
                await WriteAsync(days);
            }
            //the gate opens even when saving fails
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        public async Task RemoveAsync(DateOnly day)
        {
            await _gate.WaitAsync();
            try
            {
                var days = await ReadAsync();

                //only rewrites the file when the day was waiting
                if (days.RemoveAll(d => d.Day == day) > 0)
                {
                    await WriteAsync(days);
                }
            }
            //the gate opens even when saving fails
            finally
            {
                _gate.Release();
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
