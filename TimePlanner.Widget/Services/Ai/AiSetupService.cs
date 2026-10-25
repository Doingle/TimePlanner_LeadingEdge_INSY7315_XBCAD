using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using TimePlanner.Core.Ai;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Services.Ai
{
    //-----------------------------
    //setup progress reporting record
    public sealed record AiSetupProgress(int Step, int Steps, string Text, double? Fraction)
    {
        public int TotalSteps => Steps;
        public string Description => Text;
    }

    //-----------------------------
    //result of separate process voice benchmark
    public sealed class VoiceBenchResult
    {
        public string? Mode { get; set; }
        public bool Success { get; set; }
        public long? MedianMs { get; set; }
        public double? MedianSeconds { get; set; }
        public string? Error { get; set; }
        public string? LoadedRuntime { get; set; }
    }

    //-----------------------------
    //coordinates full one click ai setup pipeline
    public sealed class AiSetupService
    {
        private readonly OllamaInstaller _installer;
        private readonly OllamaSetup _setup;
        private readonly WhisperModelManager _modelManager;
        private readonly WidgetSession _session;
        private readonly PreferencesStore _preferencesStore;

        //-----------------------------
        //initializes setup service with needed components
        public AiSetupService(
            OllamaInstaller installer,
            OllamaSetup setup,
            WhisperModelManager modelManager,
            WidgetSession session,
            PreferencesStore preferencesStore)
        {
            _installer = installer;
            _setup = setup;
            _modelManager = modelManager;
            _session = session;
            _preferencesStore = preferencesStore;
        }

        //-----------------------------
        //executes setup pipeline starting from specified step
        public Task RunSetupAsync(int startStep, IProgress<AiSetupProgress>? progress, CancellationToken ct) => RunAsync(startStep, progress, ct);

        //-----------------------------
        //executes setup pipeline starting from specified step
        public async Task RunAsync(int startStep, IProgress<AiSetupProgress>? progress, CancellationToken ct)
        {
            var settings = _session.Preferences.Ai;

            //step 1: install ai engine
            if (startStep <= 1)
            {
                progress?.Report(new AiSetupProgress(1, 4, "Installing the AI engine (Ollama)", null));
                var dlProgress = new Progress<double>(f => progress?.Report(new AiSetupProgress(1, 4, "Installing the AI engine (Ollama)", f)));
                await _installer.EnsureOllamaAsync(dlProgress, ct);
            }

            //step 2: download day model
            if (startStep <= 2)
            {
                progress?.Report(new AiSetupProgress(2, 4, "Downloading the day model", null));
                var pullProgress = new Progress<double>(f => progress?.Report(new AiSetupProgress(2, 4, "Downloading the day model", f)));
                var dayModel = string.IsNullOrWhiteSpace(settings.CompilerModel) ? "qwen2.5:3b" : settings.CompilerModel;
                settings.CompilerModel = dayModel;
                await _setup.PullAsync(pullProgress, ct);
            }

            //step 3: download voice model
            if (startStep <= 3)
            {
                var voiceModel = string.IsNullOrWhiteSpace(settings.WhisperModel) ? "ggml-small.en-q5_1.bin" : settings.WhisperModel;
                //download model when not already on disk
                if (!_modelManager.IsDownloaded(voiceModel))
                {
                    progress?.Report(new AiSetupProgress(3, 4, "Downloading the voice model", null));
                    var voiceProgress = new Progress<double>(f => progress?.Report(new AiSetupProgress(3, 4, "Downloading the voice model", f)));
                    await _modelManager.DownloadAsync(voiceModel, voiceProgress, ct);
                }
            }

            //step 4: check fastest hardware
            if (startStep <= 4)
            {
                progress?.Report(new AiSetupProgress(4, 4, "Checking which is faster on this PC", null));

                int? graphicsTps = null;
                int? processorTps = null;

                //run day model benchmark
                try
                {
                    using var http = new HttpClient { BaseAddress = new Uri(settings.CompilerUrl) };
                    var dayBench = await SpeedCheck.RunAsync(http, settings.CompilerModel, ct);
                    graphicsTps = dayBench.GraphicsTokensPerSec;
                    processorTps = dayBench.ProcessorTokensPerSec;
                }
                //ignore speed check failures
                catch
                {
                }

                double? voiceGraphics = null;
                double? voiceProcessor = null;

                var voiceModelFile = string.IsNullOrWhiteSpace(settings.WhisperModel) ? "ggml-small.en-q5_1.bin" : settings.WhisperModel;
                var modelPath = Path.Combine(_modelManager.ModelsFolder, voiceModelFile);

                //run voice benchmark when model file exists
                if (File.Exists(modelPath) && !string.IsNullOrWhiteSpace(Environment.ProcessPath))
                {
                    var autoResult = await RunVoiceBenchProcessAsync("auto", modelPath, ct);
                    var cpuResult = await RunVoiceBenchProcessAsync("cpu", modelPath, ct);

                    voiceGraphics = autoResult?.MedianSeconds;
                    voiceProcessor = cpuResult?.MedianSeconds;

                    //keep vulkan only when at least fifteen percent faster
                    if (voiceGraphics.HasValue && voiceProcessor.HasValue && voiceGraphics.Value <= voiceProcessor.Value * (1.0 / 1.15))
                    {
                        settings.VoiceHardware = VoiceHardwareChoice.Automatic;
                    }
                    else
                    {
                        settings.VoiceHardware = VoiceHardwareChoice.Processor;
                    }
                }

                settings.LastSpeedCheck = new SpeedResult(graphicsTps, processorTps, voiceGraphics, voiceProcessor, DateTime.UtcNow);
                settings.VoiceEnabled = true;
                settings.SetupCompleted = true;
                _preferencesStore.Save(_session.Preferences);
            }
        }

        //-----------------------------
        //runs voice benchmark in separate process to test native runtime
        public static async Task<VoiceBenchResult?> RunVoiceBenchProcessAsync(string mode, string modelPath, CancellationToken ct)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"voicebench-{mode}-{Guid.NewGuid():N}.json");

            //run separate process with voice bench switch
            try
            {
                var psi = new ProcessStartInfo(Environment.ProcessPath!, $"--voice-bench {mode} \"{modelPath}\" \"{tempFile}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                //fail if process failed to start
                if (process == null)
                {
                    return null;
                }

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                await process.WaitForExitAsync(linked.Token);

                //parse output when exit code was success
                if (process.ExitCode == 0 && File.Exists(tempFile))
                {
                    var json = await File.ReadAllTextAsync(tempFile, ct);
                    return JsonSerializer.Deserialize<VoiceBenchResult>(json);
                }
            }
            //treat any exception as benchmark failure
            catch
            {
                return null;
            }
            finally
            {
                //clean up temporary benchmark output file
                try
                {
                    //delete temporary file
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                //ignore cleanup error
                catch
                {
                }
            }

            return null;
        }
    }
}
//------------------------------EOF-----------------------------\\
