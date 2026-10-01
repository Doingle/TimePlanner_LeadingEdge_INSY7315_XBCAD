using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

        private Mutex? _instance;
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

            _window = new MainWindow();
            MainWindow = _window;

#if DEBUG
            var index = Array.FindIndex(e.Args, a => string.Equals(a, "--screen", StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index + 1 < e.Args.Length && ScreenPreviews.Find(e.Args[index + 1]) is { } preview)
            {
                _tray = new TrayIconManager(ShowWidget);
                _tray.AddPreviews(ShowPreview);
                await ShowPreviewAsync(preview);
                return;
            }
#endif

            _instance = new Mutex(initiallyOwned: true, InstanceName, out var first);
            if (!first)
            {
                _instance.Dispose();
                _instance = null;
                Shutdown();
                return;
            }

            try
            {
                _appSlot = _window.OpenSlot();
                _app = WidgetServices.CreateForApp(_appSlot);
                await WidgetServices.MigrateAsync(_app.Services);
                _flow = _app.Services.GetRequiredService<WidgetFlow>();
                // Core adds the user, their settings and an internal project to log against
                await _flow.LoadAsync();
            }
            catch (Exception ex)
            {
                Report(ex, "The widget could not open its database, so it will close.");
                Shutdown(1);
                return;
            }

            _tray = new TrayIconManager(ShowWidget);
#if DEBUG
            _tray.AddPreviews(ShowPreview);
#endif
            _flow.ShowStart();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
#if DEBUG
            ClosePreview();
#endif
            _app?.Dispose();
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
        private async void ShowPreview(ScreenPreview preview) => await ShowPreviewAsync(preview);

        private async Task ShowPreviewAsync(ScreenPreview preview)
        {
            ClosePreview();
            _preview = await PreviewSession.StartAsync(_window.OpenSlot(), preview.WithEntries);
            await preview.Show(_preview);
        }

        private void ClosePreview()
        {
            _preview?.Dispose();
            _preview = null;
        }
#endif

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
    }
}
