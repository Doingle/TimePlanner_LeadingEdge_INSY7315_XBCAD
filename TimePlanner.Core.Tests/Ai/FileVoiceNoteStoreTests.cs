using TimePlanner.Core.Ai;
using Xunit;

namespace TimePlanner.Core.Tests.Ai
{
    //-----------------------------
    //unit tests for file based voice note storage
    public class FileVoiceNoteStoreTests : IDisposable
    {
        private readonly string _testFolder;
        private readonly FileVoiceNoteStore _store;

        public FileVoiceNoteStoreTests()
        {
            _testFolder = Path.Combine(Path.GetTempPath(), "TPTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testFolder);
            _store = new FileVoiceNoteStore(_testFolder);
        }

        public void Dispose()
        {
            //clean up temporary test folder
            if (Directory.Exists(_testFolder))
            {
                try
                {
                    Directory.Delete(_testFolder, recursive: true);
                }
                //suppress cleanup errors
                catch (Exception)
                {
                }
            }
        }

        //-----------------------------
        //adds a note and retrieves it by day
        [Fact]
        public async Task AddAsync_ThenGetDayAsync_ReturnsNote()
        {
            var day = new DateOnly(2026, 10, 22);
            var note = new VoiceNote
            {
                Day = day,
                WindowStart = new DateTime(2026, 10, 22, 9, 0, 0),
                WindowEnd = new DateTime(2026, 10, 22, 10, 0, 0),
                Transcript = "Test transcript"
            };

            await _store.AddAsync(note);
            var retrieved = await _store.GetDayAsync(day);

            Assert.Single(retrieved);
            Assert.Equal(note.Id, retrieved[0].Id);
            Assert.Equal("Test transcript", retrieved[0].Transcript);
        }

        //-----------------------------
        //updates an existing voice note in storage
        [Fact]
        public async Task UpdateAsync_ModifiesExistingNote()
        {
            var day = new DateOnly(2026, 10, 22);
            var note = new VoiceNote
            {
                Day = day,
                WindowStart = new DateTime(2026, 10, 22, 9, 0, 0),
                WindowEnd = new DateTime(2026, 10, 22, 10, 0, 0),
                Status = VoiceNoteStatus.Recorded
            };

            await _store.AddAsync(note);

            note.Status = VoiceNoteStatus.Transcribed;
            note.Transcript = "Updated transcript";
            await _store.UpdateAsync(note);

            var retrieved = await _store.GetDayAsync(day);
            Assert.Single(retrieved);
            Assert.Equal(VoiceNoteStatus.Transcribed, retrieved[0].Status);
            Assert.Equal("Updated transcript", retrieved[0].Transcript);
        }

        //-----------------------------
        //deleting a note removes its audio file from disk
        [Fact]
        public async Task DeleteAsync_RemovesAudioFile()
        {
            Directory.CreateDirectory(_store.AudioFolder);
            var audioFile = "test.wav";
            var audioPath = Path.Combine(_store.AudioFolder, audioFile);
            await File.WriteAllBytesAsync(audioPath, new byte[] { 1, 2, 3 });

            var note = new VoiceNote
            {
                Day = new DateOnly(2026, 10, 22),
                AudioFile = audioFile
            };

            await _store.AddAsync(note);
            Assert.True(File.Exists(audioPath));

            await _store.DeleteAsync(note.Id);

            Assert.False(File.Exists(audioPath));
            var remaining = await _store.GetDayAsync(note.Day);
            Assert.Empty(remaining);
        }

        //-----------------------------
        //ai entry ids can be added removed and cleared
        [Fact]
        public async Task AiEntryIds_AddRemoveAndClear_WorkAsExpected()
        {
            var day = new DateOnly(2026, 10, 22);

            await _store.AddAiEntryIdsAsync(day, [10, 20, 30]);
            var ids = await _store.GetAiEntryIdsAsync(day);
            Assert.Equal(3, ids.Count);

            await _store.RemoveAiEntryIdAsync(day, 20);
            ids = await _store.GetAiEntryIdsAsync(day);
            Assert.Equal(2, ids.Count);
            Assert.DoesNotContain(20, ids);

            await _store.ClearAiEntryIdsAsync(day);
            ids = await _store.GetAiEntryIdsAsync(day);
            Assert.Empty(ids);
        }

        //-----------------------------
        //purge deletes items older than 30 days and keeps newer ones
        [Fact]
        public async Task PurgeAsync_RemovesOlderThan30Days()
        {
            Directory.CreateDirectory(_store.AudioFolder);
            var now = new DateTime(2026, 10, 22, 12, 0, 0);

            var oldAudio = "old.wav";
            var newAudio = "new.wav";
            var oldPath = Path.Combine(_store.AudioFolder, oldAudio);
            var newPath = Path.Combine(_store.AudioFolder, newAudio);
            await File.WriteAllBytesAsync(oldPath, new byte[] { 1 });
            await File.WriteAllBytesAsync(newPath, new byte[] { 2 });

            var oldDay = DateOnly.FromDateTime(now.AddDays(-35));
            var newDay = DateOnly.FromDateTime(now.AddDays(-5));

            var oldNote = new VoiceNote { Day = oldDay, AudioFile = oldAudio };
            var newNote = new VoiceNote { Day = newDay, AudioFile = newAudio };

            await _store.AddAsync(oldNote);
            await _store.AddAsync(newNote);
            await _store.AddAiEntryIdsAsync(oldDay, [101]);
            await _store.AddAiEntryIdsAsync(newDay, [202]);

            await _store.PurgeAsync(now);

            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(newPath));

            var oldNotes = await _store.GetDayAsync(oldDay);
            var newNotes = await _store.GetDayAsync(newDay);
            Assert.Empty(oldNotes);
            Assert.Single(newNotes);

            var oldIds = await _store.GetAiEntryIdsAsync(oldDay);
            var newIds = await _store.GetAiEntryIdsAsync(newDay);
            Assert.Empty(oldIds);
            Assert.Single(newIds);
        }

        //-----------------------------
        //damaged json file is renamed and treated as empty
        [Fact]
        public async Task Read_DamagedFile_RenamedAndReadsEmpty()
        {
            var jsonPath = Path.Combine(_testFolder, "voice-notes.json");
            await File.WriteAllTextAsync(jsonPath, "{ invalid json corrupt content");

            var day = new DateOnly(2026, 10, 22);
            var notes = await _store.GetDayAsync(day);

            Assert.Empty(notes);
            Assert.False(File.Exists(jsonPath));

            var damagedFiles = Directory.GetFiles(_testFolder, "voice-notes.damaged-*.json");
            Assert.Single(damagedFiles);
        }

        //-----------------------------
        //handles 20 parallel update calls without io exceptions
        [Fact]
        public async Task ParallelUpdates_20Calls_AllSavedWithoutError()
        {
            var day = new DateOnly(2026, 10, 22);
            var notes = Enumerable.Range(1, 20).Select(i => new VoiceNote
            {
                Id = Guid.NewGuid(),
                Day = day,
                Transcript = $"Note {i}"
            }).ToList();

            //add all notes first
            foreach (var note in notes)
            {
                await _store.AddAsync(note);
            }

            //update all 20 in parallel
            var tasks = notes.Select(n => Task.Run(async () =>
            {
                n.Transcript += " updated";
                await _store.UpdateAsync(n);
            })).ToArray();

            await Task.WhenAll(tasks);

            var all = await _store.GetDayAsync(day);
            Assert.Equal(20, all.Count);
            Assert.All(all, n => Assert.EndsWith("updated", n.Transcript));
        }
    }
}
//------------------------------EOF-----------------------------\\
