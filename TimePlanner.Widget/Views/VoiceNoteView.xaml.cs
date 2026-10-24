using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TimePlanner.Core.Ai;
using TimePlanner.Widget.Controls;
using TimePlanner.Widget.Models;
using TimePlanner.Widget.Services.Ai;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //view handling voice note recording consent and download states
    public partial class VoiceNoteView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly DateTime _windowStart;
        private readonly DateTime _windowEnd;
        private readonly WhisperModelManager _modelManager;
        private readonly AiService _aiService;
        private readonly AudioRecorder _recorder;

        private CancellationTokenSource? _downloadCts;
        private string? _savedWavPath;
        private TimeSpan _recordedLength;

        public VoiceNoteView(
            WidgetFlow flow,
            DateTime windowStart,
            DateTime windowEnd,
            WhisperModelManager modelManager,
            AiService aiService)
        {
            InitializeComponent();
            _flow = flow;
            _windowStart = windowStart;
            _windowEnd = windowEnd;
            _modelManager = modelManager;
            _aiService = aiService;
            _recorder = new AudioRecorder();

            _recorder.LevelChanged += OnLevelChanged;
            _recorder.Elapsed += OnElapsed;
            _recorder.Stopped += OnRecorderStopped;

            UpdateState();
        }

        //-----------------------------
        //activates panel corresponding to current readiness
        private void UpdateState()
        {
            ConsentState.Visibility = Visibility.Collapsed;
            ModelNeededState.Visibility = Visibility.Collapsed;
            RecordingState.Visibility = Visibility.Collapsed;
            SavedState.Visibility = Visibility.Collapsed;

            //prompt for consent on first use
            if (!_aiService.Settings.VoiceEnabled)
            {
                ConsentState.Visibility = Visibility.Visible;
                return;
            }

            //prompt for model download when using local whisper
            if (_aiService.Settings.Transcriber == TranscriberKind.LocalWhisper &&
                !_modelManager.IsDownloaded(_aiService.Settings.WhisperModel))
            {
                ModelNeededState.Visibility = Visibility.Visible;
                return;
            }

            RecordingState.Visibility = Visibility.Visible;
            WindowHeaderText.Text = $"For {_windowStart:h:mm} – {_windowEnd:h:mm tt}".ToLower();
        }

        //-----------------------------
        //enables voice notes preference and advances state
        private void TurnOn_Click(object sender, RoutedEventArgs e)
        {
            _aiService.Settings.VoiceEnabled = true;
            _flow.SavePreferences();
            UpdateState();
        }

        //-----------------------------
        //downloads required local speech model with progress
        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            DownloadButton.IsEnabled = false;
            DownloadBar.Visibility = Visibility.Visible;
            DownloadPctText.Visibility = Visibility.Visible;

            _downloadCts = new CancellationTokenSource();
            var progress = new Progress<double>(p =>
            {
                DownloadBar.Value = p * 100;
                DownloadPctText.Text = $"{(int)(p * 100)}%";
            });

            try
            {
                ModelDownloadErrorText.Visibility = Visibility.Collapsed;
                await _modelManager.DownloadAsync(_aiService.Settings.WhisperModel, progress, _downloadCts.Token);
                UpdateState();
            }
            //handle cancellation
            catch (OperationCanceledException)
            {
            }
            //display download errors
            catch (Exception ex)
            {
                DownloadBar.Visibility = Visibility.Collapsed;
                DownloadPctText.Visibility = Visibility.Collapsed;
                ModelDownloadErrorText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Couldn't download voice model." : ex.Message;
                ModelDownloadErrorText.Visibility = Visibility.Visible;
            }
            finally
            {
                DownloadButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //cancels ongoing model download
        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            _downloadCts?.Cancel();
            _flow.ShowIdle();
        }

        //-----------------------------
        //toggles recording state between active and stopped
        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            //stop when currently recording
            if (_recorder.IsRecording)
            {
                _recorder.Stop();
                return;
            }

            ErrorSection.Visibility = Visibility.Collapsed;
            CaptionText.Text = "Speak naturally. Mention the client, what you did, any problem and how you fixed it.";
            CaptionText.ClearValue(TextBlock.ForegroundProperty);
            RecordIcon.Kind = IconKind.Stop;
            ActiveRecordingIndicator.Visibility = Visibility.Visible;

            //start pulsing dot animation if enabled in windows
            if (SystemParameters.ClientAreaAnimation)
            {
                var anim = new DoubleAnimation(1.0, 0.3, TimeSpan.FromSeconds(0.8))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                PulsingDot.BeginAnimation(UIElement.OpacityProperty, anim);
            }

            _recorder.Start();
        }

        //-----------------------------
        //updates visual level meter from incoming audio peak
        private void OnLevelChanged(double peak)
        {
            LevelBar.Width = Math.Max(0, peak * LevelTrack.ActualWidth);
        }

        //-----------------------------
        //updates duration timer and warns when near cap
        private void OnElapsed(TimeSpan elapsed)
        {
            TimerText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";

            //warn user when approaching recording limit
            if (elapsed.TotalSeconds >= AiLimits.RecordingWarnSeconds)
            {
                CaptionText.Text = "30 seconds left";
                CaptionText.Foreground = (Brush)FindResource("TextAccentBrush");
            }
        }

        //-----------------------------
        //processes recorded audio file and transitions to saved state
        private void OnRecorderStopped(string? path, TimeSpan length, string? error)
        {
            RecordIcon.Kind = IconKind.Mic;
            ActiveRecordingIndicator.Visibility = Visibility.Collapsed;
            PulsingDot.BeginAnimation(UIElement.OpacityProperty, null);

            //display error if recording stopped prematurely
            if (error != null)
            {
                ErrorSection.Visibility = Visibility.Visible;
                ErrorText.Text = error;
                return;
            }

            //transition to saved confirmation view
            if (path != null)
            {
                _savedWavPath = path;
                _recordedLength = length;

                RecordingState.Visibility = Visibility.Collapsed;
                SavedState.Visibility = Visibility.Visible;

                //warn user when audio buffer contained no speech
                if (!_recorder.HeardSpeech)
                {
                    SavedTitleText.Text = "Nothing heard";
                    SavedMessageText.Text = "We didn't hear anything. Try again?";
                    DoneButton.Visibility = Visibility.Collapsed;
                    NoSpeechButtons.Visibility = Visibility.Visible;
                }
                else
                {
                    SavedTitleText.Text = "Saved";
                    SavedMessageText.Text = "Saved. Transcribing in the background.";
                    DoneButton.Visibility = Visibility.Visible;
                    NoSpeechButtons.Visibility = Visibility.Collapsed;
                }
            }
        }

        //-----------------------------
        //saves recorded note and returns to idle screen
        private async void Done_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button ?? DoneButton;
            btn.IsEnabled = false;
            SavedErrorText.Text = string.Empty;
            SavedErrorText.Visibility = Visibility.Collapsed;

            try
            {
                //commit saved audio to store
                if (_savedWavPath != null)
                {
                    await _flow.SaveVoiceNoteAsync(_savedWavPath, _windowStart, _windowEnd, _recordedLength.TotalSeconds);
                }
            }
            catch (Exception)
            {
                //show error message and keep done button available
                SavedErrorText.Text = "Couldn't save the note. Try again.";
                SavedErrorText.Visibility = Visibility.Visible;
                DoneButton.Visibility = Visibility.Visible;
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        //-----------------------------
        //resets view to record audio again
        private void RecordAgain_Click(object sender, RoutedEventArgs e)
        {
            UpdateState();
        }

        //-----------------------------
        //aborts voice note and opens regular log entry form
        private async void TypeInstead_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                btn.IsEnabled = false;
            }

            try
            {
                _recorder.Cancel();
                await _flow.LogTimeAsync(LogEntryMode.CheckIn);
            }
            catch (Exception ex)
            {
                ErrorText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Couldn't open timesheet entry." : ex.Message;
                ErrorSection.Visibility = Visibility.Visible;
            }
            finally
            {
                if (btn != null)
                {
                    btn.IsEnabled = true;
                }
            }
        }

        //-----------------------------
        //cancels voice note and returns to idle
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _recorder.Cancel();
            _flow.ShowIdle();
        }
    }
}
//------------------------------EOF-----------------------------\\
