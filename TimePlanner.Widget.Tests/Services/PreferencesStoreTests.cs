using System.IO;
using TimePlanner.Core.Ai;
using TimePlanner.Widget.Models;
using TimePlanner.Widget.Services;
using Xunit;

namespace TimePlanner.Widget.Tests.Services
{
    //-----------------------------
    //unit tests for resilient preferences store loading and backup
    public class PreferencesStoreTests : IDisposable
    {
        private readonly string _dir;

        //-----------------------------
        //initializes isolated test directory
        public PreferencesStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "TimePlannerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        //-----------------------------
        //cleans up test directory after run
        public void Dispose()
        {
            //clean up temporary folder
            if (Directory.Exists(_dir))
            {
                try
                {
                    Directory.Delete(_dir, recursive: true);
                }
                catch (Exception)
                {
                    //ignore cleanup deletion error
                }
            }
        }

        //-----------------------------
        //unknown compiler value in ai settings keeps positions and startup preferences
        [Fact]
        public void UnknownCompiler_KeepsPositionsAndStartup()
        {
            var path = Path.Combine(_dir, "widget.json");
            var json = """
            {
              "StartWithWindows": false,
              "MainPosition": { "X": 100, "Y": 200, "Right": false, "Bottom": false },
              "IdlePosition": { "X": 300, "Y": 400, "Right": true, "Bottom": false },
              "Ai": {
                "Compiler": "Banana",
                "VoiceEnabled": true
              }
            }
            """;
            File.WriteAllText(path, json);

            var store = new PreferencesStore(path);
            var loaded = store.Load();

            Assert.False(loaded.StartWithWindows);
            Assert.NotNull(loaded.MainPosition);
            Assert.Equal(100, loaded.MainPosition.X);
            Assert.Equal(200, loaded.MainPosition.Y);
            Assert.NotNull(loaded.IdlePosition);
            Assert.Equal(300, loaded.IdlePosition.X);
            Assert.Equal(400, loaded.IdlePosition.Y);
            Assert.True(loaded.Ai.VoiceEnabled);
            Assert.Equal(CompilerKind.Ollama, loaded.Ai.Compiler);
        }

        //-----------------------------
        //unknown idle shape value keeps ai settings
        [Fact]
        public void UnknownIdleShape_KeepsAiSettings()
        {
            var path = Path.Combine(_dir, "widget.json");
            var json = """
            {
              "IdleShape": "Octagon",
              "Ai": {
                "VoiceEnabled": true,
                "CompilerModel": "custom:model",
                "ContextTokens": 4096
              }
            }
            """;
            File.WriteAllText(path, json);

            var store = new PreferencesStore(path);
            var loaded = store.Load();

            Assert.Equal(IdleShape.Pill, loaded.IdleShape);
            Assert.True(loaded.Ai.VoiceEnabled);
            Assert.Equal("custom:model", loaded.Ai.CompilerModel);
            Assert.Equal(4096, loaded.Ai.ContextTokens);
        }

        //-----------------------------
        //damaged json file writes backup copy and returns defaults
        [Fact]
        public void DamagedFile_WritesBackup_AndReturnsDefaults()
        {
            var path = Path.Combine(_dir, "widget.json");
            File.WriteAllText(path, "{ damaged json content without closure");

            var store = new PreferencesStore(path);
            var loaded = store.Load();

            var bakPath = Path.Combine(_dir, "widget.json.bak");
            Assert.True(File.Exists(bakPath));
            Assert.Equal("{ damaged json content without closure", File.ReadAllText(bakPath));
            Assert.Equal(IdleShape.Pill, loaded.IdleShape);
            Assert.True(loaded.StartWithWindows);
        }

        //-----------------------------
        //missing file returns fresh defaults
        [Fact]
        public void MissingFile_ReturnsDefaults()
        {
            var path = Path.Combine(_dir, "nonexistent.json");
            var store = new PreferencesStore(path);
            var loaded = store.Load();

            Assert.NotNull(loaded);
            Assert.Equal(IdleShape.Pill, loaded.IdleShape);
            Assert.True(loaded.StartWithWindows);
            Assert.False(loaded.Ai.VoiceEnabled);
        }

        //-----------------------------
        //the timesheet opens the way it was last left: wide, at its window's saved place; a place that couldn't be a window is dropped
        //without losing the rest, and a file from before the setting opens the small page
        [Fact]
        public void TimesheetWindow_RoundTrips_AndAnImpossiblePlaceIsDropped()
        {
            var path = Path.Combine(_dir, "widget.json");
            var store = new PreferencesStore(path);
            store.Save(new WidgetPreferences { TimesheetWide = true, TimesheetWindow = new WindowPlace(10, 20, 1200, 700, true), LargerText = true });

            var loaded = store.Load();
            Assert.True(loaded.TimesheetWide);
            Assert.Equal(new WindowPlace(10, 20, 1200, 700, true), loaded.TimesheetWindow);
            Assert.True(loaded.LargerText);

            File.WriteAllText(path, """{ "TimesheetWide": true, "TimesheetWindow": { "Left": 0, "Top": 0, "Width": -5, "Height": 300, "Maximised": false }, "SoundOnCheckIn": false }""");
            loaded = store.Load();
            Assert.True(loaded.TimesheetWide);
            Assert.Null(loaded.TimesheetWindow);
            Assert.False(loaded.SoundOnCheckIn);

            File.WriteAllText(path, """{ "SoundOnCheckIn": false }""");
            loaded = store.Load();
            Assert.False(loaded.TimesheetWide);
            Assert.Null(loaded.TimesheetWindow);
        }

        //-----------------------------
        //round trip preserves every setting
        [Fact]
        public void RoundTrip_KeepsEverySetting()
        {
            var path = Path.Combine(_dir, "widget.json");
            var original = new WidgetPreferences
            {
                IdleVisibility = IdleVisibility.Hidden,
                IdleShape = IdleShape.Ring,
                SoundOnCheckIn = false,
                StartWithWindows = false,
                VoiceTipDismissed = true,
                IdlePosition = new WidgetPosition(150, 250, true, false),
                MainPosition = new WidgetPosition(500, 600, false, true),
                Ai = new AiSettings
                {
                    VoiceEnabled = true,
                    SetupCompleted = true,
                    Compiler = CompilerKind.OpenAiCompatible,
                    CompilerUrl = "http://custom:11434",
                    CompilerModel = "qwen2.5:1.5b",
                    ContextTokens = 4096,
                    TimeoutSeconds = 120,
                    GpuLayers = 24,
                    DayHardware = HardwareChoice.Graphics,
                    VoiceHardware = VoiceHardwareChoice.Processor,
                    Transcriber = TranscriberKind.OpenAiCompatible,
                    WhisperModel = "ggml-base.en.bin",
                    WhisperModelPath = "C:\\path\\model.bin",
                    TranscriberUrl = "http://transcribe:8000",
                    TranscriberModel = "custom-whisper",
                    MicrophoneDeviceId = "mic-123",
                    OllamaManagedByWidget = true,
                    SlowPcHintDismissed = true
                }
            };

            var store = new PreferencesStore(path);
            store.Save(original);

            var loaded = store.Load();

            Assert.Equal(original.IdleVisibility, loaded.IdleVisibility);
            Assert.Equal(original.IdleShape, loaded.IdleShape);
            Assert.Equal(original.SoundOnCheckIn, loaded.SoundOnCheckIn);
            Assert.Equal(original.StartWithWindows, loaded.StartWithWindows);
            Assert.Equal(original.VoiceTipDismissed, loaded.VoiceTipDismissed);
            Assert.Equal(original.IdlePosition, loaded.IdlePosition);
            Assert.Equal(original.MainPosition, loaded.MainPosition);
            Assert.Equal(original.Ai.VoiceEnabled, loaded.Ai.VoiceEnabled);
            Assert.Equal(original.Ai.SetupCompleted, loaded.Ai.SetupCompleted);
            Assert.Equal(original.Ai.Compiler, loaded.Ai.Compiler);
            Assert.Equal(original.Ai.CompilerUrl, loaded.Ai.CompilerUrl);
            Assert.Equal(original.Ai.CompilerModel, loaded.Ai.CompilerModel);
            Assert.Equal(original.Ai.ContextTokens, loaded.Ai.ContextTokens);
            Assert.Equal(original.Ai.TimeoutSeconds, loaded.Ai.TimeoutSeconds);
            Assert.Equal(original.Ai.GpuLayers, loaded.Ai.GpuLayers);
            Assert.Equal(original.Ai.DayHardware, loaded.Ai.DayHardware);
            Assert.Equal(original.Ai.VoiceHardware, loaded.Ai.VoiceHardware);
            Assert.Equal(original.Ai.Transcriber, loaded.Ai.Transcriber);
            Assert.Equal(original.Ai.WhisperModel, loaded.Ai.WhisperModel);
            Assert.Equal(original.Ai.WhisperModelPath, loaded.Ai.WhisperModelPath);
            Assert.Equal(original.Ai.TranscriberUrl, loaded.Ai.TranscriberUrl);
            Assert.Equal(original.Ai.TranscriberModel, loaded.Ai.TranscriberModel);
            Assert.Equal(original.Ai.MicrophoneDeviceId, loaded.Ai.MicrophoneDeviceId);
            Assert.Equal(original.Ai.OllamaManagedByWidget, loaded.Ai.OllamaManagedByWidget);
            Assert.Equal(original.Ai.SlowPcHintDismissed, loaded.Ai.SlowPcHintDismissed);
        }
    }
}
//------------------------------EOF-----------------------------\\
