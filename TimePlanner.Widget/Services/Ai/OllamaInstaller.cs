using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using TimePlanner.Core.Ai;

namespace TimePlanner.Widget.Services.Ai
{
    //-----------------------------
    //manages automatic detection download and installation of ollama
    public sealed class OllamaInstaller
    {
        private readonly HttpClient _http;
        private readonly OllamaSetup _setup;

        private static readonly Guid WinTrustActionGenericVerifyV2 = new("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

        //-----------------------------
        //initializes installer with http client and setup probe
        public OllamaInstaller(HttpClient http, OllamaSetup setup)
        {
            _http = http;
            _setup = setup;
        }

        //-----------------------------
        //ensures ollama is running or installs and starts it
        public async Task EnsureOllamaAsync(IProgress<double>? progress, CancellationToken ct)
        {
            //check if ollama is already running
            var status = await _setup.CheckAsync(ct);
            //already running so skip installation
            if (status.State != OllamaState.NotRunning)
            {
                return;
            }

            //check if installed but stopped
            var stoppedApp = FindInstalledOllama();
            //start stopped app when found
            if (stoppedApp != null)
            {
                //start installed ollama app
                try
                {
                    Process.Start(new ProcessStartInfo(stoppedApp) { UseShellExecute = true });
                    //poll version endpoint for thirty seconds
                    if (await PollVersionAsync(30, ct))
                    {
                        return;
                    }
                }
                //continue to full installer when starting failed
                catch
                {
                }
            }

            //verify available drive space
            CheckDiskSpace();

            //download installer executable
            var setupExe = await DownloadInstallerAsync(progress, ct);

            //verify authenticode signature and signer subject
            VerifySignature(setupExe);

            //run quiet install
            var quietSuccess = await RunQuietInstallAsync(setupExe, ct);

            //poll for server startup after installation
            var running = await PollVersionAsync(120, ct);

            //if not running try launching installed app
            if (!running)
            {
                var installed = FindInstalledOllama();
                //start installed app when found
                if (installed != null)
                {
                    //start ollama app after setup
                    try
                    {
                        Process.Start(new ProcessStartInfo(installed) { UseShellExecute = true });
                        running = await PollVersionAsync(30, ct);
                    }
                    //ignore launch failure to proceed to fallback
                    catch
                    {
                    }
                }
            }

            //fallback to visible installer when quiet install failed
            if (!quietSuccess || !running)
            {
                //run visible installer once
                try
                {
                    Process.Start(new ProcessStartInfo(setupExe) { UseShellExecute = true });
                }
                //ignore start failure
                catch
                {
                }

                throw new InvalidOperationException("Finish the Ollama installer, then click Check again.");
            }

            //delete installer on success
            try
            {
                //remove installer executable
                if (File.Exists(setupExe))
                {
                    File.Delete(setupExe);
                }
            }
            //ignore deletion failure
            catch
            {
            }
        }

        //-----------------------------
        //searches common locations and path for installed ollama
        public static string? FindInstalledOllama()
        {
            var localApp = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Ollama", "ollama app.exe");

            //check standard local app data folder
            if (File.Exists(localApp))
            {
                return localApp;
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            //search each path directory for ollama app
            foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                //probe directory for ollama app executable
                try
                {
                    var candidate = Path.Combine(dir.Trim(), "ollama app.exe");
                    //return path when executable exists
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                //skip unreadable path entries
                catch
                {
                }
            }

            return null;
        }

        //-----------------------------
        //verifies at least 8 gb free on local app data drive
        private static void CheckDiskSpace()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = Path.GetPathRoot(localAppData) ?? "C:\\";
            var drive = new DriveInfo(root);

            //require at least eight gigabytes free space
            if (drive.AvailableFreeSpace < 8L * 1024 * 1024 * 1024)
            {
                var driveLetter = root.TrimEnd('\\');
                throw new InvalidOperationException($"You need about 8 GB free on drive {driveLetter} to set this up.");
            }
        }

        //-----------------------------
        //downloads ollama setup with progress tracking
        private async Task<string> DownloadInstallerAsync(IProgress<double>? progress, CancellationToken ct)
        {
            var downloadsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TimePlanner", "downloads");
            Directory.CreateDirectory(downloadsDir);

            var finalPath = Path.Combine(downloadsDir, "OllamaSetup.exe");
            var partPath = Path.Combine(downloadsDir, "OllamaSetup.exe.part");

            //remove partial file from earlier attempts
            if (File.Exists(partPath))
            {
                //delete stale partial download
                try
                {
                    File.Delete(partPath);
                }
                //ignore deletion failure
                catch
                {
                }
            }

            //download executable from ollama website
            try
            {
                using var response = await _http.GetAsync("https://ollama.com/download/OllamaSetup.exe", HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0L;
                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using (var fs = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int read;

                    //write stream chunks and report progress
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, read, ct);
                        totalRead += read;

                        //calculate and report progress fraction
                        if (totalBytes > 0 && progress != null)
                        {
                            progress.Report(Math.Min(1.0, (double)totalRead / totalBytes));
                        }
                    }
                }

                File.Move(partPath, finalPath, overwrite: true);
                return finalPath;
            }
            //clean up part file on failure or cancel
            catch
            {
                //delete partial file
                if (File.Exists(partPath))
                {
                    //remove incomplete file
                    try
                    {
                        File.Delete(partPath);
                    }
                    //ignore deletion failure
                    catch
                    {
                    }
                }

                throw;
            }
        }

        //-----------------------------
        //checks authenticode signature and signer identity
        private static void VerifySignature(string filePath)
        {
            var verified = false;

            //verify authenticode signature with wintrust
            try
            {
                var fileInfo = new WINTRUST_FILE_INFO(filePath);
                using var trustData = new WINTRUST_DATA(fileInfo);
                var result = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, trustData);
                //zero indicates trusted signature
                if (result == 0)
                {
                    verified = true;
                }
            }
            //treat winverifytrust errors as unverified
            catch
            {
                verified = false;
            }

            //verify certificate subject matches ollama
            if (verified)
            {
                //check subject name on certificate
                try
                {
#pragma warning disable SYSLIB0057
                    using var cert = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
                    //subject must contain ollama
                    if (!cert.Subject.Contains("Ollama", StringComparison.OrdinalIgnoreCase))
                    {
                        verified = false;
                    }
                }
                //treat certificate inspection failure as unverified
                catch
                {
                    verified = false;
                }
            }

            //delete unverified file and abort
            if (!verified)
            {
                //delete suspect download
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
                //ignore deletion failure
                catch
                {
                }

                throw new InvalidOperationException("The download couldn't be verified, so it wasn't run.");
            }
        }

        //-----------------------------
        //executes quiet installer process
        private static async Task<bool> RunQuietInstallAsync(string installerPath, CancellationToken ct)
        {
            //run installer silently without reboot
            try
            {
                var psi = new ProcessStartInfo(installerPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                //fail if process did not launch
                if (process == null)
                {
                    return false;
                }

                await process.WaitForExitAsync(ct);
                return process.ExitCode == 0;
            }
            //fail on process error
            catch
            {
                return false;
            }
        }

        //-----------------------------
        //polls ollama version endpoint until ready or timeout
        private async Task<bool> PollVersionAsync(int maxSeconds, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            //poll once per second
            while (sw.Elapsed.TotalSeconds < maxSeconds && !ct.IsCancellationRequested)
            {
                var status = await _setup.CheckAsync(ct);
                //return true when server responds
                if (status.State != OllamaState.NotRunning)
                {
                    return true;
                }

                //wait one second before next poll
                try
                {
                    await Task.Delay(1000, ct);
                }
                //exit loop when cancelled
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            return false;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class WINTRUST_FILE_INFO
        {
            public uint cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            public IntPtr pcwszFilePath;
            public IntPtr hFile = IntPtr.Zero;
            public IntPtr pgKnownSubject = IntPtr.Zero;

            public WINTRUST_FILE_INFO(string filePath)
            {
                pcwszFilePath = Marshal.StringToCoTaskMemUni(filePath);
            }

            ~WINTRUST_FILE_INFO()
            {
                //free native memory string
                if (pcwszFilePath != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pcwszFilePath);
                    pcwszFilePath = IntPtr.Zero;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private class WINTRUST_DATA : IDisposable
        {
            public uint cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
            public IntPtr pPolicyCallbackData = IntPtr.Zero;
            public IntPtr pSIPClientData = IntPtr.Zero;
            public uint dwUIChoice = 2; // WTD_UI_NONE
            public uint fdwRevocationChecks = 0; // WTD_REVOKE_NONE
            public uint dwUnionChoice = 1; // WTD_CHOICE_FILE
            public IntPtr pFile;
            public uint dwStateAction = 0;
            public IntPtr hWVTStateData = IntPtr.Zero;
            public IntPtr pwszURLReference = IntPtr.Zero;
            public uint dwProvFlags = 0x00000040; // WTD_SAFER_FLAG
            public uint dwUIContext = 0;
            public IntPtr pSignatureSettings = IntPtr.Zero;

            public WINTRUST_DATA(WINTRUST_FILE_INFO fileInfo)
            {
                pFile = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                Marshal.StructureToPtr(fileInfo, pFile, false);
            }

            public void Dispose()
            {
                //free allocated struct memory
                if (pFile != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pFile);
                    pFile = IntPtr.Zero;
                }
            }
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, WINTRUST_DATA pWVTData);
    }
}
//------------------------------EOF-----------------------------\\
