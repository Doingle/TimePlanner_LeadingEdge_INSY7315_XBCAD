using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TimePlanner.Widget.Services;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //displays update notice with inline confirmation download progress and execution
    public partial class UpdateAvailableView : UserControl
    {
        public const string ReleasesUrl = "https://github.com/Doingle/TimePlanner_LeadingEdge_INSY7315_XBCAD/releases/latest";

        private readonly Action _openRelease;
        private readonly WidgetUpdateDownloader _downloader;
        private readonly Action<string> _startInstaller;
        private Version? _version;
        private CancellationTokenSource? _downloadCts;

        public event EventHandler? Dismissed;

        public UpdateAvailableView() : this(
            () => Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true }),
            new WidgetUpdateDownloader(new HttpClient()),
            path => WidgetUpdateDownloader.StartInstaller(path))
        {
        }

        public UpdateAvailableView(
            Action openRelease,
            WidgetUpdateDownloader? downloader = null,
            Action<string>? startInstaller = null)
        {
            InitializeComponent();
            _openRelease = openRelease;
            _downloader = downloader ?? new WidgetUpdateDownloader(new HttpClient());
            _startInstaller = startInstaller ?? (path => WidgetUpdateDownloader.StartInstaller(path));
        }

        //-----------------------------
        //sets available version and resets view to initial prompt
        public void SetAvailableUpdate(Version v)
        {
            _version = v;
            var versionText = $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
            VersionNoticeText.Text = $"A newer Time Planner (version {versionText}) is ready.";

            CancelDownload();
            ShowPrompt();
        }

        //-----------------------------
        //displays initial prompt panel
        private void ShowPrompt()
        {
            PromptPanel.Visibility = Visibility.Visible;
            ConfirmPanel.Visibility = Visibility.Collapsed;
            DownloadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            ProblemText.Visibility = Visibility.Collapsed;
        }

        //-----------------------------
        //handles dismiss button and backdrop clicks
        private void Dismiss_Click(object sender, RoutedEventArgs e)
        {
            CancelDownload();
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        //-----------------------------
        //handles escape key to cancel and dismiss
        private void Notice_KeyDown(object sender, KeyEventArgs e)
        {
            //check if escape pressed
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelDownload();
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
        }

        //-----------------------------
        //handles update now click to reveal confirmation panel
        private void UpdateNow_Click(object sender, RoutedEventArgs e)
        {
            PromptPanel.Visibility = Visibility.Collapsed;
            ConfirmPanel.Visibility = Visibility.Visible;
            DownloadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
        }

        //-----------------------------
        //handles not now click to return to initial prompt
        private void NotNow_Click(object sender, RoutedEventArgs e)
        {
            ShowPrompt();
        }

        //-----------------------------
        //handles update and restart confirmation to begin download
        private async void UpdateAndRestart_Click(object sender, RoutedEventArgs e)
        {
            ConfirmPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            DownloadingPanel.Visibility = Visibility.Visible;

            DownloadProgressBar.Value = 0;
            ProgressPercentText.Text = "0%";

            _downloadCts?.Cancel();
            _downloadCts = new CancellationTokenSource();

            var progress = new Progress<double>(p =>
            {
                DownloadProgressBar.Value = p;
                ProgressPercentText.Text = $"{Math.Round(p * 100)}%";
            });

            var targetVersion = _version ?? new Version(0, 0, 0);

            try
            {
                var result = await _downloader.DownloadAsync(targetVersion, progress, _downloadCts.Token);
                //check if download succeeded
                if (result.Success && !string.IsNullOrWhiteSpace(result.InstallerPath))
                {
                    _startInstaller(result.InstallerPath);
                }
                else
                {
                    ShowError(result.ErrorMessage ?? "Couldn't download the update. Check your connection and try again.");
                }
            }
            catch (OperationCanceledException)
            {
                //download was canceled by user
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                ShowError("Couldn't download the update. Check your connection and try again.");
            }
        }

        //-----------------------------
        //handles cancel download button click
        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            CancelDownload();
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        //-----------------------------
        //handles try again click on error panel
        private void TryAgain_Click(object sender, RoutedEventArgs e)
        {
            UpdateAndRestart_Click(sender, e);
        }

        //-----------------------------
        //displays error message and actions
        private void ShowError(string message)
        {
            DownloadingPanel.Visibility = Visibility.Collapsed;
            ErrorMessageText.Text = message;
            ErrorPanel.Visibility = Visibility.Visible;
        }

        //-----------------------------
        //cancels active download operation
        private void CancelDownload()
        {
            //cancel download token if active
            if (_downloadCts != null)
            {
                try
                {
                    _downloadCts.Cancel();
                    _downloadCts.Dispose();
                }
                catch
                {
                    //ignore cancellation error
                }
                _downloadCts = null;
            }
        }

        //-----------------------------
        //handles what's new button click to open releases in browser
        private void WhatsNew_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _openRelease();
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                ProblemText.Visibility = Visibility.Visible;
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
