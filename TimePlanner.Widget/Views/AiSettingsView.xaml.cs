using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TimePlanner.Core.Ai;
using TimePlanner.Widget.Services.Ai;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //configuration view for voice notes and day builder models
    public partial class AiSettingsView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly Action _back;
        private readonly bool _ready;

        private CancellationTokenSource? _downloadCts;
        private bool _isDownloading;

        private CancellationTokenSource? _dayModelCts;
        private bool _isDownloadingDayModel;

        public AiSettingsView(WidgetFlow flow, Action back)
        {
            InitializeComponent();
            _flow = flow;
            _back = back;

            var settings = Settings;

            VoiceEnabledSwitch.IsChecked = settings.VoiceEnabled;

            TranscriberSelect.ItemsSource = new[] { "This PC (Whisper)", "Other service" };
            TranscriberSelect.SelectedIndex = settings.Transcriber == TranscriberKind.LocalWhisper ? 0 : 1;

            var models = WhisperModelCatalog.Models;
            VoiceModelSelect.ItemsSource = models.Select(m => $"{m.Label} ({m.About})").ToList();
            var modelIdx = models.ToList().FindIndex(m => m.File == settings.WhisperModel);
            VoiceModelSelect.SelectedIndex = modelIdx >= 0 ? modelIdx : 1;

            TranscriberAddressBox.Text = settings.TranscriberUrl;
            TranscriberModelBox.Text = settings.TranscriberModel;

            var savedTranscriberKey = _flow.Ai.KeyStore.Get("transcriber");
            TranscriberKeySavedHint.Visibility = string.IsNullOrEmpty(savedTranscriberKey) ? Visibility.Collapsed : Visibility.Visible;

            CompilerEngineSelect.ItemsSource = new[] { "Ollama on this PC", "Other service (OpenAI-compatible)" };
            CompilerEngineSelect.SelectedIndex = settings.Compiler == CompilerKind.Ollama ? 0 : 1;

            CompilerAddressBox.Text = settings.CompilerUrl;
            OtherCompilerModelBox.Text = settings.CompilerModel;

            var savedCompilerKey = _flow.Ai.KeyStore.Get("compiler");
            CompilerKeySavedHint.Visibility = string.IsNullOrEmpty(savedCompilerKey) ? Visibility.Collapsed : Visibility.Visible;

            UpdateVisibility();
            UpdateDownloadButton();

            _ready = true;

            Loaded += async (s, e) =>
            {
                await RefreshStatusLineAsync();
                await LoadOllamaModelsAsync();
            };
        }

        private AiSettings Settings => _flow.Session.Preferences.Ai;

        //-----------------------------
        //saves current preferences to disk
        private void Save()
        {
            //guard against changes during setup
            if (!_ready)
            {
                return;
            }

            _flow.SavePreferences();
        }

        //-----------------------------
        //refreshes top status banner text from current state
        private async Task RefreshStatusLineAsync()
        {
            string voiceStatus;
            if (!Settings.VoiceEnabled)
            {
                voiceStatus = "off";
            }
            else if (Settings.Transcriber == TranscriberKind.LocalWhisper &&
                     !_flow.Ai.ModelManager.IsDownloaded(Settings.WhisperModel))
            {
                voiceStatus = "needs a voice model";
            }
            else
            {
                voiceStatus = "ready";
            }

            string compilerStatus = "ready";
            if (Settings.Compiler == CompilerKind.Ollama)
            {
                try
                {
                    var status = await _flow.Ai.CheckOllamaAsync(CancellationToken.None);
                    compilerStatus = status.State switch
                    {
                        OllamaState.NotRunning => "needs Ollama",
                        OllamaState.ModelMissing => "needs the model",
                        _ => "ready"
                    };
                    UpdateOllamaSetupPanel(status.State);
                }
                catch
                {
                    compilerStatus = "needs Ollama";
                    UpdateOllamaSetupPanel(OllamaState.NotRunning);
                }
            }
            else
            {
                OllamaSetupPanel.Visibility = Visibility.Collapsed;
            }

            StatusBannerText.Text = $"Voice notes: {voiceStatus} · Build my day: {compilerStatus}";
        }

        //-----------------------------
        //updates ollama helper panel according to server and model readiness
        private void UpdateOllamaSetupPanel(OllamaState state)
        {
            if (Settings.Compiler != CompilerKind.Ollama)
            {
                OllamaSetupPanel.Visibility = Visibility.Collapsed;
                return;
            }

            switch (state)
            {
                case OllamaState.NotRunning:
                    OllamaSetupPanel.Visibility = Visibility.Visible;
                    OllamaNotRunningPanel.Visibility = Visibility.Visible;
                    OllamaModelMissingPanel.Visibility = Visibility.Collapsed;
                    break;
                case OllamaState.ModelMissing:
                    OllamaSetupPanel.Visibility = Visibility.Visible;
                    OllamaNotRunningPanel.Visibility = Visibility.Collapsed;
                    OllamaModelMissingPanel.Visibility = Visibility.Visible;
                    break;
                case OllamaState.Ready:
                    OllamaSetupPanel.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        //-----------------------------
        //updates visibility of conditional provider sections
        private void UpdateVisibility()
        {
            var isLocalWhisper = Settings.Transcriber == TranscriberKind.LocalWhisper;
            VoiceModelPanel.Visibility = isLocalWhisper ? Visibility.Visible : Visibility.Collapsed;
            OtherTranscriberPanel.Visibility = isLocalWhisper ? Visibility.Collapsed : Visibility.Visible;

            var isOllama = Settings.Compiler == CompilerKind.Ollama;
            OllamaModelPanel.Visibility = isOllama ? Visibility.Visible : Visibility.Collapsed;
            OtherCompilerPanel.Visibility = isOllama ? Visibility.Collapsed : Visibility.Visible;

            if (!isOllama)
            {
                OllamaSetupPanel.Visibility = Visibility.Collapsed;
            }
        }

        //-----------------------------
        //updates download button text and availability
        private void UpdateDownloadButton()
        {
            //keep button in cancel mode during active download
            if (_isDownloading)
            {
                DownloadModelButton.Content = "Cancel";
                DownloadModelButton.IsEnabled = true;
                return;
            }

            var idx = VoiceModelSelect.SelectedIndex;
            //guard against invalid model index
            if (idx < 0 || idx >= WhisperModelCatalog.Models.Count)
            {
                return;
            }

            var option = WhisperModelCatalog.Models[idx];
            var downloaded = _flow.Ai.ModelManager.IsDownloaded(option.File);

            DownloadModelButton.Content = downloaded ? "Downloaded ✓" : "Download";
            DownloadModelButton.IsEnabled = !downloaded;
        }

        //-----------------------------
        //handles voice enabled toggle state
        private void VoiceEnabled_Changed(object sender, RoutedEventArgs e)
        {
            Settings.VoiceEnabled = VoiceEnabledSwitch.IsChecked == true;
            Save();
            _ = RefreshStatusLineAsync();
        }

        //-----------------------------
        //handles whisper model selection change
        private void VoiceModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var idx = VoiceModelSelect.SelectedIndex;
            //apply selected model file name
            if (idx >= 0 && idx < WhisperModelCatalog.Models.Count)
            {
                Settings.WhisperModel = WhisperModelCatalog.Models[idx].File;
                Save();
                UpdateDownloadButton();
                _ = RefreshStatusLineAsync();
            }
        }

        //-----------------------------
        //downloads selected whisper model or cancels active download
        private async void DownloadModel_Click(object sender, RoutedEventArgs e)
        {
            //cancel download when running
            if (_isDownloading)
            {
                _downloadCts?.Cancel();
                return;
            }

            var idx = VoiceModelSelect.SelectedIndex;
            //validate model index
            if (idx < 0 || idx >= WhisperModelCatalog.Models.Count)
            {
                return;
            }

            var option = WhisperModelCatalog.Models[idx];
            _isDownloading = true;
            _downloadCts?.Dispose();
            _downloadCts = new CancellationTokenSource();

            DownloadProgressText.Visibility = Visibility.Visible;
            DownloadProgressText.Text = "Downloading 0%";
            UpdateDownloadButton();

            var progress = new Progress<double>(p =>
            {
                DownloadProgressText.Text = $"Downloading {(int)(p * 100)}%";
            });

            try
            {
                await _flow.Ai.ModelManager.DownloadAsync(option.File, progress, _downloadCts.Token);
                DownloadProgressText.Visibility = Visibility.Collapsed;
                await RefreshStatusLineAsync();
            }
            catch (OperationCanceledException)
            {
                DownloadProgressText.Text = "Cancelled";
            }
            catch (Exception ex)
            {
                DownloadProgressText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Download failed." : ex.Message;
            }
            finally
            {
                _isDownloading = false;
                UpdateDownloadButton();
            }
        }

        //-----------------------------
        //imports model binary from user selected file path
        private async void ImportModel_Click(object sender, RoutedEventArgs e)
        {
            var idx = VoiceModelSelect.SelectedIndex;
            if (idx < 0 || idx >= WhisperModelCatalog.Models.Count)
            {
                return;
            }

            var option = WhisperModelCatalog.Models[idx];
            var dialog = new OpenFileDialog
            {
                Title = "Choose voice model file",
                Filter = "Whisper Model (*.bin)|*.bin|All files (*.*)|*.*",
                FileName = option.File
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            ImportModelButton.IsEnabled = false;
            DownloadProgressText.Visibility = Visibility.Visible;
            DownloadProgressText.Text = "Importing…";

            try
            {
                await _flow.Ai.ModelManager.ImportAsync(dialog.FileName, option.File, CancellationToken.None);
                DownloadProgressText.Text = "Imported ✓";
                UpdateDownloadButton();
                await RefreshStatusLineAsync();
            }
            catch (Exception ex)
            {
                DownloadProgressText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Couldn't import model." : ex.Message;
            }
            finally
            {
                ImportModelButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //handles transcriber backend change
        private void Transcriber_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Settings.Transcriber = TranscriberSelect.SelectedIndex == 0
                ? TranscriberKind.LocalWhisper
                : TranscriberKind.OpenAiCompatible;

            Save();
            UpdateVisibility();
            _ = RefreshStatusLineAsync();
        }

        //-----------------------------
        //saves remote transcriber address
        private void TranscriberAddress_TextChanged(object sender, TextChangedEventArgs e)
        {
            Settings.TranscriberUrl = TranscriberAddressBox.Text.Trim();
            Save();
        }

        //-----------------------------
        //saves remote transcriber model
        private void TranscriberModel_TextChanged(object sender, TextChangedEventArgs e)
        {
            Settings.TranscriberModel = TranscriberModelBox.Text.Trim();
            Save();
        }

        //-----------------------------
        //encrypts and saves transcriber api key
        private void TranscriberApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            //avoid saving before initialization complete
            if (!_ready)
            {
                return;
            }

            var pwd = TranscriberApiKeyBox.Password;
            if (!string.IsNullOrEmpty(pwd))
            {
                _flow.Ai.KeyStore.Set("transcriber", pwd);
                TranscriberKeySavedHint.Visibility = Visibility.Visible;
            }
        }

        //-----------------------------
        //tests configured transcriber connection
        private async void TestTranscriber_Click(object sender, RoutedEventArgs e)
        {
            TestTranscriberButton.IsEnabled = false;
            TestTranscriberButton.Content = "Testing…";
            TranscriberTestResult.Text = "Testing…";

            try
            {
                var problem = await _flow.Ai.TestTranscriberAsync(CancellationToken.None);
                TranscriberTestResult.Text = problem ?? "Working";
            }
            catch (Exception ex)
            {
                TranscriberTestResult.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Connection test failed." : ex.Message;
            }
            finally
            {
                TestTranscriberButton.IsEnabled = true;
                TestTranscriberButton.Content = "Test";
            }
        }

        //-----------------------------
        //handles day compiler backend change
        private void CompilerEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Settings.Compiler = CompilerEngineSelect.SelectedIndex == 0
                ? CompilerKind.Ollama
                : CompilerKind.OpenAiCompatible;

            Save();
            UpdateVisibility();

            //populate models when switching to ollama
            if (Settings.Compiler == CompilerKind.Ollama)
            {
                _ = RefreshStatusLineAsync();
                _ = LoadOllamaModelsAsync();
            }
            else
            {
                _ = RefreshStatusLineAsync();
            }
        }

        //-----------------------------
        //saves compiler server address
        private void CompilerAddress_TextChanged(object sender, TextChangedEventArgs e)
        {
            Settings.CompilerUrl = CompilerAddressBox.Text.Trim();
            Save();
        }

        //-----------------------------
        //opens external browser to download ollama
        private void GetOllama_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://ollama.com/download",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                CompilerTestResult.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Couldn't open browser." : ex.Message;
            }
        }

        //-----------------------------
        //rechecks ollama server and missing model presence
        private async void CheckAgain_Click(object sender, RoutedEventArgs e)
        {
            CheckAgainButton.IsEnabled = false;

            try
            {
                await RefreshStatusLineAsync();
                await LoadOllamaModelsAsync();
            }
            catch (Exception ex)
            {
                CompilerTestResult.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Couldn't verify Ollama." : ex.Message;
            }
            finally
            {
                CheckAgainButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //pulls missing ollama model or cancels active pull
        private async void DownloadDayModel_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloadingDayModel)
            {
                _dayModelCts?.Cancel();
                return;
            }

            _isDownloadingDayModel = true;
            _dayModelCts?.Dispose();
            _dayModelCts = new CancellationTokenSource();

            DownloadDayModelButton.Content = "Cancel";
            DownloadDayModelButton.IsEnabled = true;
            DayModelDownloadText.Visibility = Visibility.Visible;
            DayModelDownloadText.Text = "Downloading 0%";

            var progress = new Progress<double>(p =>
            {
                DayModelDownloadText.Text = $"Downloading {(int)(p * 100)}%";
            });

            try
            {
                await _flow.Ai.PullOllamaAsync(progress, _dayModelCts.Token);
                DayModelDownloadText.Visibility = Visibility.Collapsed;
                await RefreshStatusLineAsync();
                await LoadOllamaModelsAsync();
            }
            catch (OperationCanceledException)
            {
                DayModelDownloadText.Text = "Cancelled";
            }
            catch (Exception ex)
            {
                DayModelDownloadText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Model download failed." : ex.Message;
            }
            finally
            {
                _isDownloadingDayModel = false;
                DownloadDayModelButton.Content = "Download model";
                DownloadDayModelButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //fetches models from local ollama instance
        private async Task LoadOllamaModelsAsync()
        {
            RefreshModelsButton.IsEnabled = false;

            try
            {
                var models = await _flow.Ai.ListOllamaModelsAsync(CancellationToken.None);

                //show help text if no models returned
                if (models.Count == 0)
                {
                    OllamaHelpText.Text = "No models found. Use Download model above.";
                    OllamaHelpText.Visibility = Visibility.Visible;
                    OllamaModelSelect.ItemsSource = new List<string>();
                }
                else
                {
                    OllamaHelpText.Visibility = Visibility.Collapsed;
                    OllamaModelSelect.ItemsSource = models;

                    //select configured model when found
                    if (models.Contains(Settings.CompilerModel))
                    {
                        OllamaModelSelect.SelectedItem = Settings.CompilerModel;
                    }
                    else if (models.Count > 0)
                    {
                        OllamaModelSelect.SelectedIndex = 0;
                    }
                }
            }
            catch (Exception)
            {
                OllamaHelpText.Text = "No models found. Use Download model above.";
                OllamaHelpText.Visibility = Visibility.Visible;
            }
            finally
            {
                RefreshModelsButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //refreshes installed ollama models list
        private async void RefreshModels_Click(object sender, RoutedEventArgs e)
        {
            RefreshModelsButton.IsEnabled = false;

            try
            {
                await LoadOllamaModelsAsync();
                await RefreshStatusLineAsync();
            }
            catch (Exception ex)
            {
                CompilerTestResult.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Refresh failed." : ex.Message;
            }
            finally
            {
                RefreshModelsButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //handles ollama model dropdown selection change
        private void OllamaModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //update settings if valid item selected
            if (OllamaModelSelect.SelectedItem is string model && !string.IsNullOrWhiteSpace(model))
            {
                Settings.CompilerModel = model;
                Save();
                _ = RefreshStatusLineAsync();
            }
        }

        //-----------------------------
        //saves remote compiler model
        private void OtherCompilerModel_TextChanged(object sender, TextChangedEventArgs e)
        {
            Settings.CompilerModel = OtherCompilerModelBox.Text.Trim();
            Save();
        }

        //-----------------------------
        //encrypts and saves compiler api key
        private void CompilerApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            //avoid saving before initialization complete
            if (!_ready)
            {
                return;
            }

            var pwd = CompilerApiKeyBox.Password;
            if (!string.IsNullOrEmpty(pwd))
            {
                _flow.Ai.KeyStore.Set("compiler", pwd);
                CompilerKeySavedHint.Visibility = Visibility.Visible;
            }
        }

        //-----------------------------
        //tests configured compiler connection
        private async void TestCompiler_Click(object sender, RoutedEventArgs e)
        {
            TestCompilerButton.IsEnabled = false;
            TestCompilerButton.Content = "Testing…";
            CompilerTestResult.Text = "Testing…";

            try
            {
                var problem = await _flow.Ai.TestCompilerAsync(CancellationToken.None);
                CompilerTestResult.Text = problem ?? "Working";
            }
            catch (Exception ex)
            {
                CompilerTestResult.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Connection test failed." : ex.Message;
            }
            finally
            {
                TestCompilerButton.IsEnabled = true;
                TestCompilerButton.Content = "Test";
            }
        }

        //-----------------------------
        //navigates back to previous screen
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            _back();
        }

        //-----------------------------
        //finishes editing settings
        private void Done_Click(object sender, RoutedEventArgs e)
        {
            _back();
        }
    }
}
//------------------------------EOF-----------------------------\\
