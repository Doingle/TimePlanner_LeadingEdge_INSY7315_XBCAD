using System.Text.Json;

namespace TimePlanner.Core.Sync
{
    //-----------------------------
    //send history kept as a json file
    public sealed class FileSendHistoryStore : ISendHistoryStore
    {
        private readonly string _path;

        public FileSendHistoryStore(string path) => _path = path;

        //-----------------------------
        //reads sent days newest first
        public async Task<IReadOnlyList<SentDay>> GetAsync()
        {
            //missing history means no days sent yet
            if (!File.Exists(_path))
            {
                return Array.Empty<SentDay>();
            }

            try
            {
                var json = await File.ReadAllBytesAsync(_path);
                var days = JsonSerializer.Deserialize<List<SentDay>>(json);

                //null json body returns empty history
                if (days == null)
                {
                    return Array.Empty<SentDay>();
                }

                return days.OrderByDescending(d => d.Day).ToList();
            }
            //corrupted history file returns empty history
            catch (JsonException)
            {
                return Array.Empty<SentDay>();
            }
        }

        //-----------------------------
        //adds a day or replaces an earlier entry for that day
        public async Task AddAsync(SentDay sent)
        {
            var existing = (await GetAsync()).ToList();

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
    }
}
//------------------------------EOF-----------------------------\\
