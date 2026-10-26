using System.IO;
using TimePlanner.Core.Ai;
using TimePlanner.Widget.Models;
using TimePlanner.Widget.Services.Ai;
using Xunit;

namespace TimePlanner.Widget.Tests.Services
{
    //-----------------------------
    //unit tests for ai removal service
    public class AiRemovalServiceTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly string _localAppData;
        private readonly string _userProfile;
        private readonly string _whisperFolder;

        //-----------------------------
        //initializes isolated test environment
        public AiRemovalServiceTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "TimePlannerTests_AiRemoval", Guid.NewGuid().ToString("N"));
            _localAppData = Path.Combine(_testRoot, "LocalAppData");
            _userProfile = Path.Combine(_testRoot, "UserProfile");
            _whisperFolder = Path.Combine(_testRoot, "WhisperModels");

            Directory.CreateDirectory(_localAppData);
            Directory.CreateDirectory(_userProfile);
            Directory.CreateDirectory(_whisperFolder);
        }

        //-----------------------------
        //cleans up test environment
        public void Dispose()
        {
            //clean up temporary test files
            if (Directory.Exists(_testRoot))
            {
                try
                {
                    Directory.Delete(_testRoot, recursive: true);
                }
                catch
                {
                    //ignore cleanup failure
                }
            }
        }

        //-----------------------------
        //managed ollama invokes uninstaller purges models and resets flags
        [Fact]
        public async Task RemoveAsync_WidgetManagedOllama_InvokesUninstallerAndPurgesModels()
        {
            var uninstallerDir = Path.Combine(_localAppData, "Programs", "Ollama");
            Directory.CreateDirectory(uninstallerDir);
            var uninstallerExe = Path.Combine(uninstallerDir, "unins000.exe");
            await File.WriteAllTextAsync(uninstallerExe, "fake uninstaller");

            var ollamaModelsDir = Path.Combine(_userProfile, ".ollama", "models");
            Directory.CreateDirectory(ollamaModelsDir);
            await File.WriteAllTextAsync(Path.Combine(ollamaModelsDir, "manifest.json"), "{}");

            var dummyWhisperModel = Path.Combine(_whisperFolder, "ggml-small.bin");
            await File.WriteAllTextAsync(dummyWhisperModel, "model data");

            var invocations = new List<(string Exe, string Args)>();
            Task FakeUninstaller(string exe, string args)
            {
                invocations.Add((exe, args));
                return Task.CompletedTask;
            }

            var service = new AiRemovalService(FakeUninstaller, _localAppData, _userProfile);
            var settings = new AiSettings
            {
                OllamaManagedByWidget = true,
                SetupCompleted = true,
                VoiceEnabled = true
            };

            var result = await service.RemoveAsync(settings, _whisperFolder, host: null, CancellationToken.None);

            Assert.False(result.OwnOllamaKept);
            Assert.False(settings.SetupCompleted);
            Assert.False(settings.VoiceEnabled);
            Assert.False(settings.OllamaManagedByWidget);

            Assert.Single(invocations);
            Assert.Equal(uninstallerExe, invocations[0].Exe);
            Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", invocations[0].Args);

            Assert.False(Directory.Exists(ollamaModelsDir));
            Assert.Empty(Directory.GetFiles(_whisperFolder));
        }

        //-----------------------------
        //user own ollama skips uninstaller and reports own ollama kept
        [Fact]
        public async Task RemoveAsync_UserOwnOllama_NeverInvokesUninstaller()
        {
            var invocations = new List<(string Exe, string Args)>();
            Task FakeUninstaller(string exe, string args)
            {
                invocations.Add((exe, args));
                return Task.CompletedTask;
            }

            var dummyWhisperModel = Path.Combine(_whisperFolder, "ggml-small.bin");
            await File.WriteAllTextAsync(dummyWhisperModel, "model data");

            var service = new AiRemovalService(FakeUninstaller, _localAppData, _userProfile);
            var settings = new AiSettings
            {
                OllamaManagedByWidget = false,
                SetupCompleted = true,
                VoiceEnabled = true
            };

            var result = await service.RemoveAsync(settings, _whisperFolder, host: null, CancellationToken.None);

            Assert.True(result.OwnOllamaKept);
            Assert.False(settings.SetupCompleted);
            Assert.False(settings.VoiceEnabled);
            Assert.False(settings.OllamaManagedByWidget);

            Assert.Empty(invocations);
            Assert.Empty(Directory.GetFiles(_whisperFolder));
        }

        //-----------------------------
        //missing folders do not throw exception
        [Fact]
        public async Task RemoveAsync_MissingFolders_DoesNotThrow()
        {
            var nonExistentLocal = Path.Combine(_testRoot, "nonexistent_local");
            var nonExistentUser = Path.Combine(_testRoot, "nonexistent_user");
            var nonExistentWhisper = Path.Combine(_testRoot, "nonexistent_whisper");

            var service = new AiRemovalService(null, nonExistentLocal, nonExistentUser);
            var settings = new AiSettings
            {
                OllamaManagedByWidget = true,
                SetupCompleted = true,
                VoiceEnabled = true
            };

            var exception = await Record.ExceptionAsync(() => service.RemoveAsync(settings, nonExistentWhisper, host: null, CancellationToken.None));
            Assert.Null(exception);
            Assert.False(settings.SetupCompleted);
        }
    }
}
//------------------------------EOF-----------------------------\\
