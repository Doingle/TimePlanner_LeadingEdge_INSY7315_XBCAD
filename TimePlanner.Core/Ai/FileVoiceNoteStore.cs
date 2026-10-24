using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //file based voice note and metadata storage
    public sealed class FileVoiceNoteStore : IVoiceNoteStore
    {
        private readonly string _folder;
        private readonly string _path;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public FileVoiceNoteStore(string folder)
        {
            _folder = folder;
            _path = Path.Combine(folder, "voice-notes.json");
            AudioFolder = Path.Combine(folder, "audio");
        }

        public string AudioFolder { get; }

        //-----------------------------
        //appends a new voice note
        public async Task AddAsync(VoiceNote note)
        {
            await _gate.WaitAsync();
            try
            {
                var data = await ReadDataAsync();
                data.Notes.Add(note);
                await SaveDataAsync(data);
            }
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        //updates an existing voice note
        public async Task UpdateAsync(VoiceNote note)
        {
            await _gate.WaitAsync();
            try
            {
                var data = await ReadDataAsync();
                var index = data.Notes.FindIndex(n => n.Id == note.Id);

                //replace existing entry or append
                if (index >= 0)
                {
                    data.Notes[index] = note;
                }
                else
                {
                    data.Notes.Add(note);
                }

                await SaveDataAsync(data);
            }
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        //deletes a note and its audio file
        public async Task DeleteAsync(Guid id)
        {
            await _gate.WaitAsync();
            try
            {
                var data = await ReadDataAsync();
                var index = data.Notes.FindIndex(n => n.Id == id);

                //delete record and associated audio
                if (index >= 0)
                {
                    var note = data.Notes[index];
                    data.Notes.RemoveAt(index);

                    //clean up audio file on disk
                    if (!string.IsNullOrWhiteSpace(note.AudioFile))
                    {
                        var audioPath = Path.Combine(AudioFolder, note.AudioFile);
                        //ignore missing file
                        if (File.Exists(audioPath))
                        {
                            try
                            {
                                File.Delete(audioPath);
                            }
                            //suppress file deletion errors
                            catch (Exception)
                            {
                            }
                        }
                    }

                    await SaveDataAsync(data);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        //retrieves notes for a specific day
        public async Task<IReadOnlyList<VoiceNote>> GetDayAsync(DateOnly day)
        {
            await _gate.WaitAsync();
            try
            {
                var data = await ReadDataAsync();
                return data.Notes
                    .Where(n => n.Day == day)
                    .OrderBy(n => n.WindowStart)
                    .ToList();
            }
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        //finds notes waiting for transcription
        public async Task<IReadOnlyList<VoiceNote>> GetToTranscribeAsync()
        {
            await _gate.WaitAsync();
            try
            {
                var data = await ReadDataAsync();
                return data.Notes
                    .Where(n => n.Status == VoiceNoteStatus.Recorded ||
                                (n.Status == VoiceNoteStatus.Failed && n.Attempts < AiLimits.MaxTranscribeAttempts))
                    .OrderBy(n => n.WindowStart)
                    .ToList();
            }
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        //finds the latest window end time on a given day
        public async Task<DateTime?> GetLatestWindowEndAsync(DateOnly day)
        {
            await _gate.WaitAsync();
            try
            {
                var data = await ReadDataAsync();
                var notes = data.Notes.Where(n => n.Day == day).ToList();

                //return null when day has no notes
                if (notes.Count == 0)
                {
                    return null;
                }

                return notes.Max(n => n.WindowEnd);
            }
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        //reads entry ids marked as ai built
        public async Task<IReadOnlyList<int>> GetAiEntryIdsAsync(DateOnly day)
        {
            await _gate.WaitAsync();
            try
            {
                var data = await ReadDataAsync();
                var match = data.Days.FirstOrDefault(d => d.Day == day);
                return match?.AiEntryIds.ToList() ?? [];
            }
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        //records entry ids as ai built
        public async Task AddAiEntryIdsAsync(DateOnly day, IEnumerable<int> ids)
        {
            await _gate.WaitAsync();
            try
            {
                var data = await ReadDataAsync();
                var match = data.Days.FirstOrDefault(d => d.Day == day);

                //create day bucket when absent
                if (match == null)
                {
                    match = new DayAiEntries { Day = day };
                    data.Days.Add(match);
                }

                //add each unique entry id
                foreach (var id in ids)
                {
                    //avoid duplicate ids
                    if (!match.AiEntryIds.Contains(id))
                    {
                        match.AiEntryIds.Add(id);
                    }
                }

                await SaveDataAsync(data);
            }
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        //unmarks an entry id as ai built
        public async Task RemoveAiEntryIdAsync(DateOnly day, int id)
        {
            await _gate.WaitAsync();
            try
            {
                var data = await ReadDataAsync();
                var match = data.Days.FirstOrDefault(d => d.Day == day);

                //remove id from day list
                if (match != null && match.AiEntryIds.Remove(id))
                {
                    await SaveDataAsync(data);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        //clears all ai markers for a day
        public async Task ClearAiEntryIdsAsync(DateOnly day)
        {
            await _gate.WaitAsync();
            try
            {
                var data = await ReadDataAsync();
                var count = data.Days.RemoveAll(d => d.Day == day);

                //save when day was present
                if (count > 0)
                {
                    await SaveDataAsync(data);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        //purges records and audio older than retention limit
        public async Task PurgeAsync(DateTime now)
        {
            await _gate.WaitAsync();
            try
            {
                var data = await ReadDataAsync();
                var cutoffDay = DateOnly.FromDateTime(now.AddDays(-AiLimits.KeepDays));

                var expiredNotes = data.Notes.Where(n => n.Day < cutoffDay).ToList();

                //delete audio files for expired notes
                foreach (var note in expiredNotes)
                {
                    //clean audio file from disk
                    if (!string.IsNullOrWhiteSpace(note.AudioFile))
                    {
                        var audioPath = Path.Combine(AudioFolder, note.AudioFile);
                        //ignore missing file
                        if (File.Exists(audioPath))
                        {
                            try
                            {
                                File.Delete(audioPath);
                            }
                            //suppress file deletion errors
                            catch (Exception)
                            {
                            }
                        }
                    }
                }

                data.Notes.RemoveAll(n => n.Day < cutoffDay);
                data.Days.RemoveAll(d => d.Day < cutoffDay);

                await SaveDataAsync(data);
            }
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        //reads json store handling corruption
        private async Task<StoreData> ReadDataAsync()
        {
            //empty store when file absent
            if (!File.Exists(_path))
            {
                return new StoreData();
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(_path);
                var loaded = JsonSerializer.Deserialize<StoreData>(bytes);
                return loaded ?? new StoreData();
            }
            //preserve damaged files for diagnosis
            catch (JsonException)
            {
                var damagedName = $"voice-notes.damaged-{DateTime.Now:yyyyMMddHHmmss}.json";
                var damagedPath = Path.Combine(_folder, damagedName);

                try
                {
                    File.Move(_path, damagedPath, overwrite: true);
                }
                //suppress move failures
                catch (Exception)
                {
                }

                return new StoreData();
            }
        }

        //-----------------------------
        //atomically saves store data to disk
        private async Task SaveDataAsync(StoreData data)
        {
            var dir = Path.GetDirectoryName(_path);
            //ensure container folder exists
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tmpPath = _path + ".tmp";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(data, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllBytesAsync(tmpPath, bytes);
            File.Move(tmpPath, _path, overwrite: true);
        }

        //-----------------------------
        //root store serialization schema
        private sealed class StoreData
        {
            [JsonPropertyName("notes")]
            public List<VoiceNote> Notes { get; set; } = [];

            [JsonPropertyName("days")]
            public List<DayAiEntries> Days { get; set; } = [];
        }

        //-----------------------------
        //ai entry id markers per day
        private sealed class DayAiEntries
        {
            [JsonPropertyName("day")]
            public DateOnly Day { get; set; }

            [JsonPropertyName("aiEntryIds")]
            public List<int> AiEntryIds { get; set; } = [];
        }
    }
}
//------------------------------EOF-----------------------------\\
