using System.IO;
using System.Net.Http;
using System.Windows;
using TimePlanner.Core.Ai;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Services.Ai
{
    //-----------------------------
    //background queue processing speech recognition for recorded voice notes
    public sealed class VoiceTranscriptionQueue : IDisposable
    {
        private readonly IVoiceNoteStore _store;
        private readonly LocalWhisperTranscriber _localTranscriber;
        private readonly TimeLogService _timeLogs;
        private readonly WidgetSession _session;
        private readonly AiKeyStore _keyStore;
        private readonly SemaphoreSlim _signal = new(0);
        private readonly CancellationTokenSource _cts = new();

        private bool _started;

        public VoiceTranscriptionQueue(
            IVoiceNoteStore store,
            LocalWhisperTranscriber localTranscriber,
            TimeLogService timeLogs,
            WidgetSession session,
            AiKeyStore keyStore)
        {
            _store = store;
            _localTranscriber = localTranscriber;
            _timeLogs = timeLogs;
            _session = session;
            _keyStore = keyStore;
        }

        //-----------------------------
        //raised on ui thread when voice note transcript or status changes
        public event Action<Guid>? NoteChanged;

        //-----------------------------
        //starts background processing loop
        public void Start()
        {
            //ensure queue loop starts only once
            if (_started)
            {
                return;
            }

            _started = true;
            Task.Run(ProcessQueueAsync);
        }

        //-----------------------------
        //signals queue that a new note is ready for transcription
        public void Enqueue(Guid id)
        {
            _signal.Release();
        }

        //-----------------------------
        //resets note attempt count and re-enqueues for processing
        public async Task RetryAsync(Guid id)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            //search recent days within retention boundary
            for (var i = 0; i < AiLimits.KeepDays; i++)
            {
                var dayNotes = await _store.GetDayAsync(today.AddDays(-i));
                var match = dayNotes.FirstOrDefault(n => n.Id == id);

                //reset attempts and error on match
                if (match != null)
                {
                    match.Attempts = 0;
                    match.Status = VoiceNoteStatus.Recorded;
                    match.Error = null;
                    await _store.UpdateAsync(match);
                    Enqueue(id);
                    RaiseNoteChanged(id);
                    break;
                }
            }
        }

        //-----------------------------
        //background loop processing pending notes one by one
        private async Task ProcessQueueAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var pending = await _store.GetToTranscribeAsync();

                    //wait for work when queue is empty
                    if (pending.Count == 0)
                    {
                        await _signal.WaitAsync(_cts.Token);
                        continue;
                    }

                    var note = pending[0];
                    note.Attempts++;

                    var vocabulary = await GatherVocabularyAsync();
                    var transcriber = ResolveTranscriber();

                    var fileName = note.AudioFile ?? $"{note.Id}.wav";
                    var audioPath = Path.Combine(_store.AudioFolder, fileName);

                    //fail note if audio file was deleted prematurely
                    if (!File.Exists(audioPath))
                    {
                        note.Status = VoiceNoteStatus.Failed;
                        note.Error = "Audio recording file missing.";
                        await _store.UpdateAsync(note);
                        RaiseNoteChanged(note.Id);
                        continue;
                    }

                    try
                    {
                        var hints = new TranscriptionHints(vocabulary, "en");
                        var text = await transcriber.TranscribeAsync(audioPath, hints, _cts.Token);

                        //empty transcription marks note as failed with no speech explanation
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            note.Status = VoiceNoteStatus.Failed;
                            note.Error = "No speech found in this note.";
                            note.Transcript = null;
                            await _store.UpdateAsync(note);
                            RaiseNoteChanged(note.Id);
                            continue;
                        }

                        note.Transcript = text;
                        note.Status = VoiceNoteStatus.Transcribed;
                        note.Error = null;

                        //delete wav on successful transcription
                        if (File.Exists(audioPath))
                        {
                            try
                            {
                                File.Delete(audioPath);
                            }
                            catch (Exception)
                            {
                            }
                        }

                        note.AudioFile = null;
                        await _store.UpdateAsync(note);
                        RaiseNoteChanged(note.Id);
                    }
                    //record transcription failure keeping audio file
                    catch (Exception ex)
                    {
                        note.Status = VoiceNoteStatus.Failed;
                        note.Error = ex is AiUnavailableException aue ? aue.Message : "Transcription failed.";
                        await _store.UpdateAsync(note);
                        RaiseNoteChanged(note.Id);
                    }
                }
                //handle loop level errors
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    await Task.Delay(1000, _cts.Token);
                }
            }
        }

        //-----------------------------
        //resolves active transcription provider based on settings
        private ITranscriber ResolveTranscriber()
        {
            var settings = _session.Preferences.Ai;
            //use local whisper when configured
            if (settings.Transcriber == TranscriberKind.LocalWhisper)
            {
                return _localTranscriber;
            }

            var apiKey = _keyStore.Get("transcriber");
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds) };
            var remote = AiProviders.CreateRemoteTranscriber(settings, http, apiKey);

            return remote ?? _localTranscriber;
        }

        //-----------------------------
        //collects project names and activity labels for vocabulary hints
        private async Task<IReadOnlyList<string>> GatherVocabularyAsync()
        {
            var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var projects = await _timeLogs.GetProjectsAsync();
                //extract company and project titles
                foreach (var p in projects)
                {
                    //include company name
                    if (!string.IsNullOrWhiteSpace(p.Company?.Name))
                    {
                        words.Add(p.Company.Name);
                    }

                    //include project title
                    if (!string.IsNullOrWhiteSpace(p.Name))
                    {
                        words.Add(p.Name);
                    }
                }

                var activities = await _timeLogs.GetActivityTreeAsync();
                CollectLabels(activities.Tree, words);
            }
            //suppress database retrieval errors
            catch (Exception)
            {
            }

            return words.ToList();
        }

        //-----------------------------
        //recursively collects activity node names
        private static void CollectLabels(IEnumerable<ActivityNode> nodes, HashSet<string> words)
        {
            foreach (var node in nodes)
            {
                words.Add(node.Label);
                CollectLabels(node.Children, words);
            }
        }

        //-----------------------------
        //dispatches change notification to ui thread
        private void RaiseNoteChanged(Guid id)
        {
            var app = Application.Current;
            //dispatch to wpf ui thread
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.InvokeAsync(() => NoteChanged?.Invoke(id));
            }
            else
            {
                NoteChanged?.Invoke(id);
            }
        }

        //-----------------------------
        //cancels background queue processing
        public void Dispose()
        {
            _cts.Cancel();
            _signal.Dispose();
            _cts.Dispose();
        }
    }
}
//------------------------------EOF-----------------------------\\
