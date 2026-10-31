using System.Diagnostics;
using System.IO;
using TimePlanner.Core.Ai;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Services.Ai
{
    //result of ai removal operation
    public sealed record AiRemovalResult(bool OwnOllamaKept);

    //-----------------------------
    //removes ai engine models and resets preferences for uninstaller and settings
    public sealed class AiRemovalService
    {
        private readonly Func<string, string, Task> _runUninstaller;
        private readonly string _localAppDataFolder;
        private readonly string _userProfileFolder;

        public AiRemovalService() : this(null, null, null) { }

        public AiRemovalService(
            Func<string, string, Task>? runUninstaller = null,
            string? localAppDataFolder = null,
            string? userProfileFolder = null)
        {
            _runUninstaller = runUninstaller ?? DefaultRunUninstallerAsync;
            _localAppDataFolder = localAppDataFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _userProfileFolder = userProfileFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        //-----------------------------
        //runs uninstaller process silently without a window
        private static async Task DefaultRunUninstallerAsync(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            //wait for process exit if started
            if (proc != null)
            {
                await proc.WaitForExitAsync();
            }
        }

        //-----------------------------
        //removes ai engine and models and resets configuration flags
        public async Task<AiRemovalResult> RemoveAsync(AiSettings settings, string whisperFolder, OllamaHost? host, CancellationToken ct)
        {
            //1. stop ollama host if provided
            host?.Stop();

            var ownOllamaKept = false;

            //2. if managed by widget run uninstaller and purge ollama models
            if (settings.OllamaManagedByWidget)
            {
                var uninstaller = Path.Combine(_localAppDataFolder, "Programs", "Ollama", "unins000.exe");
                //run uninstaller if file exists
                if (File.Exists(uninstaller))
                {
                    try
                    {
                        await _runUninstaller(uninstaller, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART");
                    }
                    catch
                    {
                        //ignore uninstaller failures
                    }
                }

                var ollamaModels = Path.Combine(_userProfileFolder, ".ollama", "models");
                //delete ollama models directory if present
                if (Directory.Exists(ollamaModels))
                {
                    try
                    {
                        Directory.Delete(ollamaModels, recursive: true);
                    }
                    catch
                    {
                        //ignore deletion failure
                    }
                }
            }
            else
            {
                ownOllamaKept = true;
            }

            //3. delete whisper model files in folder
            try
            {
                //check if whisper folder exists
                if (Directory.Exists(whisperFolder))
                {
                    foreach (var file in Directory.GetFiles(whisperFolder))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                            //ignore file deletion failure
                        }
                    }
                }
            }
            catch
            {
                //ignore directory access failure
            }

            //4. reset setup flags
            settings.SetupCompleted = false;
            settings.VoiceEnabled = false;
            settings.OllamaManagedByWidget = false;

            return new AiRemovalResult(ownOllamaKept);
        }
    }
}
//------------------------------EOF-----------------------------\\
