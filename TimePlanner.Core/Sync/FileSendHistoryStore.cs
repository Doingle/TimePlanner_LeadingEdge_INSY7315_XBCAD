using System.Text.Json;

namespace TimePlanner.Core.Sync
{
    //-----------------------------
    //send history kept as a json file
    public sealed class FileSendHistoryStore : ISendHistoryStore
    {
        private readonly string _path;

        //one read or write of the file at a time
        private readonly SemaphoreSlim _gate = new(1, 1);

        public FileSendHistoryStore(string path) => _path = path;

        //-----------------------------
        //reads sent days from disk newest first
        private async Task<List<SentDay>> ReadAsync()
        {
            //missing history means no days sent yet
            if (!File.Exists(_path))
            {
                return new List<SentDay>();
            }

            try
            {
                var json = await File.ReadAllBytesAsync(_path);
                var days = JsonSerializer.Deserialize<List<SentDay>>(json);

                //null json body returns empty history
                if (days == null)
                {
                    return new List<SentDay>();
                }

                return days.OrderByDescending(d => d.Day).ToList();
            }
            //corrupted history file returns empty history
            catch (JsonException)
            {
                return new List<SentDay>();
            }
        }

        //-----------------------------
        //reads sent days newest first
        public async Task<IReadOnlyList<SentDay>> GetAsync()
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
        //adds a day or replaces an earlier entry for that day
        public async Task AddAsync(SentDay sent)
        {
            await _gate.WaitAsync();
            try
            {
                var existing = await ReadAsync();

                //re-sent days update the existing record
                existing.RemoveAll(d => d.Day == sent.Day);
                existing.Add(sent);

                var ordered = existing.OrderByDescending(d => d.Day).Take(366).ToList();
                var json = JsonSerializer.SerializeToUtf8Bytes(ordered);

                var dir = Path.GetDirectoryName(_path);

                //creates the local storage directory when missing
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await File.WriteAllBytesAsync(_path, json);
            }
            //the gate opens even when saving fails
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        //notes that a sent day was edited afterwards
        public async Task MarkChangedAsync(DateOnly day, DateTime at)
        {
            await _gate.WaitAsync();
            try
            {
                var existing = await ReadAsync();
                var index = existing.FindIndex(d => d.Day == day);

                //if the day is in history it gets marked changed
                if (index >= 0)
                {
                    existing[index] = existing[index] with { ChangedAt = at };
                    var json = JsonSerializer.SerializeToUtf8Bytes(existing);
                    var dir = Path.GetDirectoryName(_path);

                    //creates the local storage directory when missing
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    await File.WriteAllBytesAsync(_path, json);
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
