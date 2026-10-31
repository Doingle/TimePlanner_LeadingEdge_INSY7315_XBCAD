using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TimePlanner.Widget.Services;
using TimePlanner.Widget.Services.Ai;
using Whisper.net;

namespace TimePlanner.Widget
{
    /// <summary>
    /// Starts the widget: opens (or creates) this Windows user's database, signs them in and shows
    /// Setup. Debug builds also have the screen previews: <c>--screen &lt;id&gt;</c> opens a single
    /// figure from the spec on sample data (ids are in <see cref="ScreenPreviews"/>), without touching
    /// the real database. None of that is compiled into a release build.
    /// </summary>
    public partial class App : Application
    {
        // One widget per Windows user: a second copy would track the same day twice
        private const string InstanceName = @"Local\TimePlanner.Widget";
        public const string QuitSignalName = @"Local\TimePlanner.Widget.Quit";

        // Opening the widget again while it runs (perhaps waiting in the tray on a day off) tells it to show itself
        private const string ShowSignalName = @"Local\TimePlanner.Widget.Show";

        // Lets the running widget come to the front when a second copy asks it to show
        private const int AnyProcess = -1;

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int processId);

        private Mutex? _instance;
        private EventWaitHandle? _quitSignal;
        private RegisteredWaitHandle? _quitWait;
        private EventWaitHandle? _showSignal;
        private RegisteredWaitHandle? _showWait;
        private MainWindow _window = null!;
        private IHost? _app;
        private IWidgetHost? _appSlot;
        private WidgetFlow? _flow;
        private TrayIconManager? _tray;
#if DEBUG
        private PreviewSession? _preview;
#endif

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandledException;

            //handle voice benchmark process execution before mutex or ui initialization
            var benchIdx = Array.FindIndex(e.Args, a => string.Equals(a, "--voice-bench", StringComparison.OrdinalIgnoreCase));
            if (benchIdx >= 0 && benchIdx + 3 < e.Args.Length)
            {
                var mode = e.Args[benchIdx + 1];
                var modelPath = e.Args[benchIdx + 2];
                var resultFile = e.Args[benchIdx + 3];
                RunVoiceBenchChildProcess(mode, modelPath, resultFile);
                Environment.Exit(0);
                return;
            }

            //handle uninstall ai flag before mutex or ui initialization
            var uninstallAiIdx = Array.FindIndex(e.Args, a => string.Equals(a, "--uninstall-ai", StringComparison.OrdinalIgnoreCase));
            if (uninstallAiIdx >= 0)
            {
                try
                {
                    var whisperFolder = new WhisperModelManager().ModelsFolder;
                    //check if preferences file exists
                    if (!File.Exists(WidgetServices.PreferencesPath))
                    {
                        //delete whisper folder files if present when preferences never existed
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
                        Environment.Exit(0);
                        return;
                    }

                    var store = new PreferencesStore(WidgetServices.PreferencesPath);
                    var prefs = store.Load();
                    var removalService = new AiRemovalService();
                    removalService.RemoveAsync(prefs.Ai, whisperFolder, host: null, CancellationToken.None).GetAwaiter().GetResult();
                    store.Save(prefs);
                    Environment.Exit(0);
                    return;
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Failed to uninstall AI: {0}", ex);
                    Environment.Exit(1);
                    return;
                }
            }

            //scan developer flags
            var devIndex = Array.FindIndex(e.Args, a => string.Equals(a, "--dev", StringComparison.OrdinalIgnoreCase));
#if DEBUG
            var screenIndex = Array.FindIndex(e.Args, a => string.Equals(a, "--screen", StringComparison.OrdinalIgnoreCase));
            //enable dev mode if dev flag or screen preview is requested
            if (devIndex >= 0 || screenIndex >= 0)
            {
                //dev feature not available to users
                DevMode.Enabled = true;
            }
#else
            //enable dev mode if dev flag is requested
            if (devIndex >= 0)
            {
                //dev feature not available to users
                DevMode.Enabled = true;
            }
#endif

            _window = new MainWindow();
            MainWindow = _window;

#if DEBUG
            //show requested preview screen
            if (screenIndex >= 0 && screenIndex + 1 < e.Args.Length && ScreenPreviews.Find(e.Args[screenIndex + 1]) is { } preview)
            {
                _window.ShowInTaskbar = true;
                _tray = CreateTray();
                await ShowPreviewAsync(preview);
                return;
            }
#endif

            _instance = new Mutex(initiallyOwned: true, InstanceName, out var first);
            if (!first)
            {
                // The widget already runs, maybe out of sight in the tray: it shows itself instead
                if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var show))
                {
                    using (show)
                    {
                        AllowSetForegroundWindow(AnyProcess);
                        show.Set();
                    }
                }
                _instance.Dispose();
                _instance = null;
                Shutdown();
                return;
            }

            //register quit signal listener so installer can close widget cleanly
            _quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitSignalName);
            _quitWait = ThreadPool.RegisterWaitForSingleObject(
                _quitSignal,
                (state, timedOut) =>
                {
                    //trigger clean shutdown when signalled
                    if (!timedOut)
                    {
                        Dispatcher.BeginInvoke(() => Shutdown());
                    }
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: true);
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal,
                (_, _) => Dispatcher.BeginInvoke(ShowWidget), null, Timeout.Infinite, executeOnlyOnce: false);

            try
            {
                _appSlot = _window.OpenSlot();
                _app = WidgetServices.CreateForApp(_appSlot);
                await WidgetServices.MigrateAsync(_app.Services);
                _flow = _app.Services.GetRequiredService<WidgetFlow>();
                // Core adds the user, their settings and an internal project to log against
                await _flow.LoadAsync();

                //testers load a finished example day with the sample day switch
                if (e.Args.Any(a => string.Equals(a, "--sample-day", StringComparison.OrdinalIgnoreCase)))
                {
                    //dev feature not available to users
                    if (DevMode.Enabled)
                    {
                        var sample = await _flow.LoadSampleDayAsync();
                        MessageBox.Show(sample.Message, "Sample day");
                    }
                    //warn when developer flag is used in user mode
                    else
                    {
                        var logger = _app.Services.GetService<ILogger<App>>();
                        logger?.LogWarning("Developer flag ignored in user mode: --sample-day");
                    }
                }
            }
            catch (Exception ex)
            {
                Report(ex, "The widget could not open its database, so it will close.");
                Shutdown(1);
                return;
            }

            _tray = CreateTray();
            // Started by Windows at sign-in, a day off stays in the tray; opened by hand, the widget always shows
            if (e.Args.Any(a => string.Equals(a, Services.StartupService.SignInArgument, StringComparison.OrdinalIgnoreCase)))
                _flow.ShowStartAtSignIn();
            else
                _flow.ShowStart();
            var updates = _app.Services.GetRequiredService<Services.WidgetUpdateChecker>();
            _window.AttachUpdates(updates);
            _flow.DashboardSignedIn += (_, _) => _ = updates.CheckAsync();
            _ = updates.CheckAsync();
        }

        private TrayIconManager CreateTray() => new(ShowWidget,
            () => { if (ActiveFlow is { } flow) _ = flow.LogFromTrayAsync(); },
            () => { if (ActiveFlow is { } flow) _ = flow.ShowTimesheetAsync(DateOnly.FromDateTime(flow.Scheduler.Now), flow.ShowFromTray); },
            () => ActiveFlow?.ShowSettings(),
            () => ActiveFlow?.ShowHelp(),
            () => { if (ActiveFlow is { } flow) flow.AskExit(); else Shutdown(); },
            () => ActiveFlow?.Scheduler.IsRunning == true,
            CheckInNow
#if DEBUG
            , ShowPreviewAsync
#endif
        );

        private WidgetFlow? ActiveFlow
        {
            get
            {
#if DEBUG
                if (_preview != null) return _preview.Flow;
#endif
                return _flow;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _showWait?.Unregister(null);
            _showSignal?.Dispose();
            try
            {
                //stop managed ollama server process
                var host = _app?.Services.GetService<OllamaHost>();
                host?.Stop();
            }
            catch (Exception ex)
            {
                //ignore cleanup failure on exit
                Trace.TraceWarning($"Failed to stop OllamaHost on exit: {ex.Message}");
            }

            _tray?.Dispose();
#if DEBUG
            ClosePreview();
#endif
            _app?.Dispose();
            //clean up quit signal wait handle and registration
            _quitWait?.Unregister(null);
            _quitWait = null;
            _quitSignal?.Dispose();
            _quitSignal = null;
            if (_instance != null)
            {
                _instance.ReleaseMutex();
                _instance.Dispose();
            }
            base.OnExit(e);
        }

        /// <summary>The tray icon's Show: back to the day, leaving any preview.</summary>
        private void ShowWidget()
        {
#if DEBUG
            if (_flow == null && _preview != null)
            {
                // Started with --screen: the preview is all there is
                _preview.Flow.ShowFromTray();
                return;
            }
            ClosePreview();
#endif
            if (_flow == null || _appSlot == null)
                return;

            _window.UseSlot(_appSlot);
            _flow.ShowFromTray();
        }

#if DEBUG
        private async Task ShowPreviewAsync(ScreenPreview preview)
        {
            ClosePreview();
            _preview = await PreviewSession.StartAsync(_window.OpenSlot(), preview.WithEntries);
            await preview.Show(_preview);
            if (preview.Id == "tray-menu")
                await _window.Dispatcher.InvokeAsync(() => _tray?.ShowPreviewMenu(_window), DispatcherPriority.ContextIdle);
        }

        private void ClosePreview()
        {
            _preview?.Dispose();
            _preview = null;
        }
#endif

        //-----------------------------
        //triggers immediate check in for testing
        //dev feature not available to users
        private void CheckInNow()
        {
            //guard when flow is not initialized
            if (_flow == null)
            {
                return;
            }

            //prompt check in alert when scheduler is active
            if (_flow.Scheduler.IsRunning)
            {
                _flow.CheckIn(announce: true);
            }
            //otherwise navigate from tray
            else
            {
                _flow.ShowFromTray();
            }
        }

        /// <summary>A failed save or load says so, rather than closing the widget and losing the day.</summary>
        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Report(e.Exception, "Something went wrong, and your last change may not have been saved. Please try again.");
            e.Handled = true;
        }

        /// <summary>
        /// The details of a problem go to the log (the Windows event log and the debugger); the message
        /// on screen stays general, since an exception can carry file paths and data.
        /// </summary>
        private void Report(Exception exception, string message)
        {
            var logger = _app?.Services.GetService<ILogger<App>>();
            if (logger != null)
                logger.LogError(exception, "{Message}", message);
            else
                Trace.TraceError($"{message} {exception}");

#if DEBUG
            message += $"\n\n{exception.Message}";
#endif
            MessageBox.Show(message, "INSY", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        //-----------------------------
        //runs voice benchmark in isolated process and writes result json
        private static void RunVoiceBenchChildProcess(string mode, string modelPath, string resultFile)
        {
            var result = new VoiceBenchResult { Mode = mode };

            //attempt voice benchmark transcription with specified runtime mode
            try
            {
                //configure runtime library order based on mode
                if (string.Equals(mode, "cpu", StringComparison.OrdinalIgnoreCase))
                {
                    Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder = [Whisper.net.LibraryLoader.RuntimeLibrary.Cpu];
                }
                //otherwise allow vulkan with cpu fallback
                else
                {
                    Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder = [Whisper.net.LibraryLoader.RuntimeLibrary.Vulkan, Whisper.net.LibraryLoader.RuntimeLibrary.Cpu];
                }

                //generate 1 second 16khz 16bit mono silent wav buffer
                var wavBytes = CreateSilenceWav(16000, 1);

                using var factory = WhisperFactory.FromPath(modelPath);
                var times = new List<long>();

                //run 3 transcription benchmarks
                for (var i = 0; i < 3; i++)
                {
                    using var processor = factory.CreateBuilder()
                        .WithLanguage("en")
                        .WithThreads(1)
                        .Build();

                    using var ms = new MemoryStream(wavBytes);
                    var sw = Stopwatch.StartNew();
                    Task.Run(async () =>
                    {
                        await foreach (var _ in processor.ProcessAsync(ms))
                        {
                        }
                    }).GetAwaiter().GetResult();
                    sw.Stop();
                    times.Add(sw.ElapsedMilliseconds);
                }

                times.Sort();
                result.Success = true;
                result.MedianMs = times[1];
                result.MedianSeconds = times[1] / 1000.0;
            }
            //record failure details
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }

            //write benchmark outcome json
            try
            {
                var json = JsonSerializer.Serialize(result);
                File.WriteAllText(resultFile, json);
            }
            //ignore file write failures
            catch
            {
            }
        }

        //-----------------------------
        //creates silent mono 16 bit pcm wav buffer
        private static byte[] CreateSilenceWav(int sampleRate, int seconds)
        {
            var channels = 1;
            var bitsPerSample = 16;
            var byteRate = sampleRate * channels * (bitsPerSample / 8);
            var blockAlign = (short)(channels * (bitsPerSample / 8));
            var dataSize = byteRate * seconds;

            using var ms = new MemoryStream(44 + dataSize);
            using var w = new BinaryWriter(ms);
            w.Write("RIFF"u8.ToArray());
            w.Write(36 + dataSize);
            w.Write("WAVE"u8.ToArray());
            w.Write("fmt "u8.ToArray());
            w.Write(16);
            w.Write((short)1);
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(byteRate);
            w.Write(blockAlign);
            w.Write((short)bitsPerSample);
            w.Write("data"u8.ToArray());
            w.Write(dataSize);
            w.Write(new byte[dataSize]);
            return ms.ToArray();
        }
    }
}
//------------------------------EOF-----------------------------\\
