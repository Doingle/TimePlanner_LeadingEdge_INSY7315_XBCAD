using System.IO;
using System.Net.Http;
using TimePlanner.Core.Ai;
using TimePlanner.Core.Services.Models;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Services.Ai
{
    //-----------------------------
    //facade unifying voice notes speech recognition and day compilation
    public sealed class AiService
    {
        private readonly IVoiceNoteStore _store;
        private readonly WhisperModelManager _modelManager;
        private readonly VoiceTranscriptionQueue _queue;
        private readonly TimeLogService _timeLogs;
        private readonly WidgetSession _session;
        private readonly AiKeyStore _keyStore;

        public AiService(
            IVoiceNoteStore store,
            WhisperModelManager modelManager,
            VoiceTranscriptionQueue queue,
            TimeLogService timeLogs,
            WidgetSession session,
            AiKeyStore keyStore)
        {
            _store = store;
            _modelManager = modelManager;
            _queue = queue;
            _timeLogs = timeLogs;
            _session = session;
            _keyStore = keyStore;
        }

        public AiSettings Settings => _session.Preferences.Ai;

        //-----------------------------
        //true when voice notes are enabled and models ready
        public bool VoiceReady
        {
            get
            {
                //disabled voice feature is not ready
                if (!Settings.VoiceEnabled)
                {
                    return false;
                }

                //verify local whisper model binary presence
                if (Settings.Transcriber == TranscriberKind.LocalWhisper)
                {
                    return _modelManager.IsDownloaded(Settings.WhisperModel);
                }

                return true;
            }
        }

        //-----------------------------
        //saves recorded audio into persistent storage and enqueues transcription
        public async Task<VoiceNote> SaveRecordingAsync(string wavPath, DateTime windowStart, DateTime windowEnd, double seconds)
        {
            var id = Guid.NewGuid();
            Directory.CreateDirectory(_store.AudioFolder);

            var fileName = $"{id}.wav";
            var targetPath = Path.Combine(_store.AudioFolder, fileName);
            File.Move(wavPath, targetPath, overwrite: true);

            var note = new VoiceNote
            {
                Id = id,
                Day = DateOnly.FromDateTime(windowStart),
                WindowStart = windowStart,
                WindowEnd = windowEnd,
                RecordedAt = DateTime.Now,
                Seconds = seconds,
                AudioFile = fileName,
                Status = VoiceNoteStatus.Recorded
            };

            await _store.AddAsync(note);
            _queue.Enqueue(note.Id);
            return note;
        }

        //-----------------------------
        //retrieves all notes recorded for a specific day
        public Task<IReadOnlyList<VoiceNote>> GetNotesAsync(DateOnly day)
        {
            return _store.GetDayAsync(day);
        }

        //-----------------------------
        //counts notes awaiting compilation for a specific day
        public async Task<int> CountUnbuiltAsync(DateOnly day)
        {
            var notes = await _store.GetDayAsync(day);
            return notes.Count(n => n.Status != VoiceNoteStatus.Built);
        }

        //-----------------------------
        //compiles day timesheet suggestions from transcribed voice notes
        public async Task<DayBuildResult> BuildDayAsync(DateOnly day, CancellationToken ct)
        {
            var slots = await _timeLogs.GetTimesheetAsync(_session.User.UserId, day);

            var activeProjects = await _timeLogs.GetProjectsAsync();
            var projectTuples = activeProjects
                .Where(p => !string.IsNullOrWhiteSpace(p.Company?.Name) && !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => (p.Company!.Name, p.Name))
                .Distinct()
                .ToList();

            var activityTree = await _timeLogs.GetActivityTreeAsync();
            var paths = new List<IReadOnlyList<string>>();

            //recursively flatten tree into activity path arrays
            void Flatten(IEnumerable<ActivityNode> nodes, List<string> parent)
            {
                foreach (var node in nodes)
                {
                    var current = new List<string>(parent) { node.Label };
                    paths.Add(current);
                    Flatten(node.Children, current);
                }
            }

            Flatten(activityTree.Tree, []);

            var notes = await _store.GetDayAsync(day);
            var request = DayBuildRequestFactory.Create(day, notes, slots, projectTuples, paths);

            var instructionsPath = Path.Combine(AppContext.BaseDirectory, "Ai", "instructions.md");
            var examplesPath = Path.Combine(AppContext.BaseDirectory, "Ai", "examples.md");

            DayPromptBuilder prompt;
            //load custom prompt files when available on disk
            if (File.Exists(instructionsPath) && File.Exists(examplesPath))
            {
                var inst = await File.ReadAllTextAsync(instructionsPath, ct);
                var exm = await File.ReadAllTextAsync(examplesPath, ct);
                prompt = new DayPromptBuilder(inst, exm);
            }
            else
            {
                prompt = DayPromptBuilder.Default;
            }

            var apiKey = _keyStore.Get("compiler");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(Settings.TimeoutSeconds) };
            var compiler = AiProviders.CreateCompiler(Settings, http, apiKey, prompt);

            return await compiler.BuildAsync(request, ct);
        }

        //-----------------------------
        //commits suggested entries to timesheet via timelog service
        public async Task<(int Added, IReadOnlyList<string> Problems)> AddSuggestionsAsync(DateOnly day, IReadOnlyList<SuggestedEntry> entries)
        {
            var addedIds = new List<int>();
            var addedEntries = new List<SuggestedEntry>();
            var problems = new List<string>();

            //attempt to add each accepted entry
            foreach (var entry in entries)
            {
                var edit = new EntryEdit(entry.Start, entry.End, entry.ProjectPath, entry.ActivityPath, entry.Note);
                var result = await _timeLogs.AddEntryAsync(_session.User.UserId, edit);

                //record success or note refusal reason
                if (result.Ok && result.EntryId.HasValue)
                {
                    addedIds.Add(result.EntryId.Value);
                    addedEntries.Add(entry);
                }
                else
                {
                    problems.Add($"{entry.Start:HH:mm}–{entry.End:HH:mm} wasn't added: {result.Error ?? "Conflict"}");
                }
            }

            //tag added entries with ai provenance
            if (addedIds.Count > 0)
            {
                await _store.AddAiEntryIdsAsync(day, addedIds);
            }

            var notes = await _store.GetDayAsync(day);
            var now = DateTime.Now;

            //mark referenced voice notes as built
            foreach (var note in notes)
            {
                var hasMatchingAddedEntry = addedEntries.Any(e =>
                    (e.Start >= note.WindowStart && e.Start < note.WindowEnd) ||
                    (e.End > note.WindowStart && e.End <= note.WindowEnd));

                //update note status on match
                if (hasMatchingAddedEntry)
                {
                    note.Status = VoiceNoteStatus.Built;
                    note.BuiltAt = now;
                    await _store.UpdateAsync(note);
                }
            }

            return (addedIds.Count, problems);
        }

        //-----------------------------
        //raised on ui thread when voice note transcript or status changes
        public event Action<Guid>? NoteChanged
        {
            add => _queue.NoteChanged += value;
            remove => _queue.NoteChanged -= value;
        }

        //-----------------------------
        //clears tracked ai entry ids for a given day
        public Task ClearAiEntryIdsAsync(DateOnly day)
        {
            return _store.ClearAiEntryIdsAsync(day);
        }

        //-----------------------------
        //finds a specific voice note by id across active days
        public async Task<VoiceNote?> GetNoteAsync(Guid id)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            //search recent days within retention boundary
            for (var i = 0; i < AiLimits.KeepDays; i++)
            {
                var dayNotes = await _store.GetDayAsync(today.AddDays(-i));
                var match = dayNotes.FirstOrDefault(n => n.Id == id);

                //return note on match
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        //-----------------------------
        //re-enqueues failed voice note for transcription
        public Task RetryNoteAsync(Guid id)
        {
            return _queue.RetryAsync(id);
        }

        //-----------------------------
        //reads entry ids created by ai suggestions
        public Task<IReadOnlyList<int>> GetAiEntryIdsAsync(DateOnly day)
        {
            return _store.GetAiEntryIdsAsync(day);
        }

        //-----------------------------
        //deletes voice note and associated recording
        public Task DeleteNoteAsync(Guid id)
        {
            return _store.DeleteAsync(id);
        }

        //-----------------------------
        //updates transcript text after manual user correction
        public async Task UpdateTranscriptAsync(Guid id, string text)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            //search recent days within retention boundary
            for (var i = 0; i < AiLimits.KeepDays; i++)
            {
                var dayNotes = await _store.GetDayAsync(today.AddDays(-i));
                var match = dayNotes.FirstOrDefault(n => n.Id == id);

                //update transcript on match
                if (match != null)
                {
                    match.Transcript = text;
                    await _store.UpdateAsync(match);
                    break;
                }
            }
        }

        public AiKeyStore KeyStore => _keyStore;

        public WhisperModelManager ModelManager => _modelManager;

        //-----------------------------
        //tests transcriber connectivity or local model presence
        public async Task<string?> TestTranscriberAsync(CancellationToken ct)
        {
            //test local whisper binary availability
            if (Settings.Transcriber == TranscriberKind.LocalWhisper)
            {
                var transcriber = new LocalWhisperTranscriber(_modelManager, Settings);
                return await transcriber.TestAsync(ct);
            }

            var apiKey = _keyStore.Get("transcriber");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(Settings.TimeoutSeconds) };
            var remote = AiProviders.CreateRemoteTranscriber(Settings, http, apiKey);

            //ensure remote transcriber provider exists
            if (remote == null)
            {
                return "Unknown transcriber";
            }

            return await remote.TestAsync(ct);
        }

        //-----------------------------
        //tests compiler connectivity and model readiness
        public async Task<string?> TestCompilerAsync(CancellationToken ct)
        {
            var apiKey = _keyStore.Get("compiler");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(Settings.TimeoutSeconds) };
            var compiler = AiProviders.CreateCompiler(Settings, http, apiKey, DayPromptBuilder.Default);
            return await compiler.TestAsync(ct);
        }

        //-----------------------------
        //lists models installed in local ollama
        public async Task<IReadOnlyList<string>> ListOllamaModelsAsync(CancellationToken ct)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(Settings.TimeoutSeconds) };
            var compiler = new OllamaDayCompiler(http, Settings, DayPromptBuilder.Default);
            return await compiler.ListModelsAsync(ct);
        }

        //-----------------------------
        //checks status of local ollama server and model
        public async Task<OllamaStatus> CheckOllamaAsync(CancellationToken ct)
        {
            using var http = new HttpClient();
            var setup = new OllamaSetup(http, Settings);
            return await setup.CheckAsync(ct);
        }

        //-----------------------------
        //pulls missing compiler model from ollama library
        public async Task PullOllamaAsync(IProgress<double>? progress, CancellationToken ct)
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var setup = new OllamaSetup(http, Settings);
            await setup.PullAsync(progress, ct);
        }
    }
}
//------------------------------EOF-----------------------------\\
