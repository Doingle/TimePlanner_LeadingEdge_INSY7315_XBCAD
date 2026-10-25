using System.IO;
using TimePlanner.Core.Ai;
using Whisper.net;

namespace TimePlanner.Widget.Services.Ai
{
    //-----------------------------
    //local whisper transcription caching model factory
    public sealed class LocalWhisperTranscriber : ITranscriber, IDisposable
    {
        private readonly WhisperModelManager _manager;
        private readonly AiSettings _settings;
        private readonly object _lock = new();

        private WhisperFactory? _factory;
        private string? _cachedModelPath;

        public LocalWhisperTranscriber(WhisperModelManager manager, AiSettings settings)
        {
            _manager = manager;
            _settings = settings;
        }

        //-----------------------------
        //transcribes wav file with local whisper on background thread
        public async Task<string> TranscribeAsync(string wavPath, TranscriptionHints hints, CancellationToken ct)
        {
            var modelPath = Path.Combine(_manager.ModelsFolder, _settings.WhisperModel);

            //throw plain language error if model missing
            if (!File.Exists(modelPath))
            {
                throw new AiUnavailableException("The voice model isn't downloaded yet. Open Settings › Voice and AI.");
            }

            EnsureRuntimeConfigured();

            WhisperFactory factory;
            lock (_lock)
            {
                //reload factory if model selection changed
                if (_factory == null || _cachedModelPath != modelPath)
                {
                    _factory?.Dispose();
                    _factory = WhisperFactory.FromPath(modelPath);
                    _cachedModelPath = modelPath;
                }

                factory = _factory;
            }

            var promptText = string.Join(", ", hints.Vocabulary);
            //clamp prompt length to limit
            if (promptText.Length > 400)
            {
                promptText = promptText[..400];
            }

            var threads = Math.Max(1, Environment.ProcessorCount / 2);

            return await Task.Run(async () =>
            {
                using var processor = factory.CreateBuilder()
                    .WithLanguage(hints.Language)
                    .WithPrompt(promptText)
                    .WithThreads(threads)
                    .Build();

                using var stream = File.OpenRead(wavPath);
                var segments = new List<string>();

                //gather all recognized segments
                await foreach (var segment in processor.ProcessAsync(stream, ct))
                {
                    segments.Add(segment.Text);
                }

                return string.Join(" ", segments).Trim();
            }, ct);
        }

        //-----------------------------
        //tests model file presence on disk
        public Task<string?> TestAsync(CancellationToken ct)
        {
            var modelPath = Path.Combine(_manager.ModelsFolder, _settings.WhisperModel);
            //return null when model exists
            if (File.Exists(modelPath))
            {
                return Task.FromResult<string?>(null);
            }

            return Task.FromResult<string?>("The voice model isn't downloaded yet. Open Settings › Voice and AI.");
        }

        private static bool _runtimeConfigured;
        private static readonly object _configLock = new();

        //-----------------------------
        //configures native whisper runtime library order once
        private void EnsureRuntimeConfigured()
        {
            lock (_configLock)
            {
                //configure runtime order once
                if (!_runtimeConfigured)
                {
                    _runtimeConfigured = true;
                    //force cpu when processor selected
                    if (_settings.VoiceHardware == VoiceHardwareChoice.Processor)
                    {
                        Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder = [Whisper.net.LibraryLoader.RuntimeLibrary.Cpu];
                    }
                    //otherwise allow vulkan with cpu fallback
                    else
                    {
                        Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder = [Whisper.net.LibraryLoader.RuntimeLibrary.Vulkan, Whisper.net.LibraryLoader.RuntimeLibrary.Cpu];
                    }
                }
            }
        }

        //-----------------------------
        //disposes native whisper factory
        public void Dispose()
        {
            lock (_lock)
            {
                _factory?.Dispose();
                _factory = null;
                _cachedModelPath = null;
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
