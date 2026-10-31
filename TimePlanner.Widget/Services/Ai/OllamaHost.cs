using System.Diagnostics;
using System.IO;
using System.Net.Http;
using TimePlanner.Core.Ai;

namespace TimePlanner.Widget.Services.Ai
{
    //-----------------------------
    //manages lifetime of widget started silent ollama server process
    public sealed class OllamaHost : IDisposable
    {
        private readonly HttpClient _http;
        private readonly AiSettings _settings;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private Process? _startedProcess;
        private bool _isDisposed;

        //-----------------------------
        //initializes host with http client and settings
        public OllamaHost(HttpClient http, AiSettings settings)
        {
            _http = http;
            _settings = settings;
        }

        //-----------------------------
        //ensures ollama server is reachable starting managed or unmanaged instance if needed
        public async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
        {
            //check if server already responds on configured address
            if (await IsRespondingAsync(ct))
            {
                return true;
            }

            await _gate.WaitAsync(ct);
            try
            {
                //recheck under lock
                if (await IsRespondingAsync(ct))
                {
                    return true;
                }

                //if managed by widget start silent ollama.exe serve
                if (_settings.OllamaManagedByWidget)
                {
                    var ollamaExe = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Programs", "Ollama", "ollama.exe");

                    //start silent managed ollama server
                    if (await StartHiddenServerAsync(ollamaExe, ct))
                    {
                        return true;
                    }
                }
                //otherwise start user installed ollama app if present
                else
                {
                    var installed = OllamaInstaller.FindInstalledOllama();
                    //start unmanaged ollama instance
                    if (installed != null)
                    {
                        var dir = Path.GetDirectoryName(installed);
                        var ollamaExe = dir != null ? Path.Combine(dir, "ollama.exe") : null;

                        //start silent server when ollama.exe present
                        if (ollamaExe != null && File.Exists(ollamaExe))
                        {
                            if (await StartHiddenServerAsync(ollamaExe, ct))
                            {
                                return true;
                            }
                        }
                        //otherwise fall back to launching desktop app
                        else
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(installed) { UseShellExecute = true });
                                //poll for unmanaged server response up to twenty seconds
                                if (await PollVersionAsync(20, ct))
                                {
                                    return true;
                                }
                            }
                            catch (Exception ex)
                            {
                                //ignore start error
                                Trace.TraceWarning($"Failed to start unmanaged Ollama app: {ex.Message}");
                            }
                        }
                    }
                }

                return false;
            }
            finally
            {
                _gate.Release();
            }
        }

        //-----------------------------
        //starts silent ollama server process and polls until responding
        private async Task<bool> StartHiddenServerAsync(string ollamaExe, CancellationToken ct)
        {
            //verify ollama executable existence
            if (!File.Exists(ollamaExe))
            {
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = ollamaExe,
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.Environment["OLLAMA_HOST"] = "127.0.0.1:11434";

            var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (_, _) => { };
            proc.ErrorDataReceived += (_, _) => { };

            //start process and discard streams
            try
            {
                if (proc.Start())
                {
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    _startedProcess = proc;

                    //poll for server response up to twenty seconds
                    if (await PollVersionAsync(20, ct))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                //ignore start error
                Trace.TraceWarning($"Failed to start silent Ollama server: {ex.Message}");
            }

            return false;
        }

        //-----------------------------
        //polls api version endpoint until responding or timeout
        private async Task<bool> PollVersionAsync(int maxSeconds, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddSeconds(maxSeconds);
            //loop until timeout or cancellation
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                //return true once responding
                if (await IsRespondingAsync(ct))
                {
                    return true;
                }

                try
                {
                    await Task.Delay(500, ct);
                }
                catch (OperationCanceledException)
                {
                    //exit polling loop on cancellation
                    return false;
                }
            }

            return false;
        }

        //-----------------------------
        //checks whether api version responds on configured url
        public async Task<bool> IsRespondingAsync(CancellationToken ct = default)
        {
            var url = (_settings.CompilerUrl ?? "http://localhost:11434").TrimEnd('/') + "/api/version";
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var res = await _http.SendAsync(req, cts.Token);
                return res.IsSuccessStatusCode;
            }
            catch
            {
                //endpoint unresponsive
                return false;
            }
        }

        //-----------------------------
        //stops the managed ollama process and its process tree
        public void Stop()
        {
            //stop only process started by widget
            if (_startedProcess != null)
            {
                try
                {
                    //terminate process tree if not exited
                    if (!_startedProcess.HasExited)
                    {
                        _startedProcess.Kill(entireProcessTree: true);
                        _startedProcess.WaitForExit(3000);
                    }
                }
                catch (Exception ex)
                {
                    //ignore stop error
                    Trace.TraceWarning($"Failed to terminate managed Ollama process: {ex.Message}");
                }
                finally
                {
                    _startedProcess.Dispose();
                    _startedProcess = null;
                }
            }
        }

        //-----------------------------
        //disposes host and ensures started process is terminated
        public void Dispose()
        {
            //skip repeated disposes
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            Stop();
            _gate.Dispose();
        }
    }
}
//------------------------------EOF-----------------------------\\
