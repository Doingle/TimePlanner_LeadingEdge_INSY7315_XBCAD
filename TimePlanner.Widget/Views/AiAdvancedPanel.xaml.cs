using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using TimePlanner.Core.Ai;
using TimePlanner.Widget.Services;
using TimePlanner.Widget.Services.Ai;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //advanced configuration panel for ai hardware, microphone, engine, and models
    public partial class AiAdvancedPanel : UserControl
    {
        public event Action? CloseRequested;
        public event Action? ResetRequested;
        public event Action? HardwareChanged;

        private WidgetFlow? _flow;
        private VoiceHardwareChoice _initialVoiceHardware;
        private AudioRecorder? _testRecorder;
        private DispatcherTimer? _testTimer;
        private CancellationTokenSource? _speedCheckCts;
        private readonly MicrophoneCatalog _micCatalog = new();
        private AiSettings? Settings => _flow?.Session.Preferences.Ai;

        public AiAdvancedPanel()
        {
            InitializeComponent();
        }

        //-----------------------------
        //initializes panel with flow and initial settings
        public void Initialize(WidgetFlow flow, VoiceHardwareChoice initialVoiceHardware)
        {
            _flow = flow;
            _initialVoiceHardware = initialVoiceHardware;

            DayHardwareSelect.ItemsSource = new[] { "Automatic (recommended)", "Graphics card", "Processor" };
            VoiceHardwareSelect.ItemsSource = new[] { "Automatic (recommended)", "Processor only" };
            DevTranscriberSelect.ItemsSource = new[] { "Local Whisper (default)", "Remote OpenAI-compatible API" };
            DevCompilerSelect.ItemsSource = new[] { "Local Ollama (default)", "Remote OpenAI-compatible API" };
            Refresh();
        }

        //-----------------------------
        //refreshes panel controls and state from settings
        public void Refresh()
        {
            if (Settings == null || _flow == null) return;

            DayHardwareSelect.SelectedIndex = Math.Clamp((int)Settings.DayHardware, 0, 2);
            VoiceHardwareSelect.SelectedIndex = Settings.VoiceHardware == VoiceHardwareChoice.Processor ? 1 : 0;
            VoiceRestartCaption.Visibility = Settings.VoiceHardware != _initialVoiceHardware ? Visibility.Visible : Visibility.Collapsed;

            UpdateHardwareCaption();
            RefreshMicrophones();
            RefreshAiEngine();

            OllamaAddressBox.Text = Settings.CompilerUrl;
            VoiceModelPathBox.Text = Settings.WhisperModelPath ?? string.Empty;
            EngineThisPcRadio.IsChecked = Settings.Compiler != CompilerKind.Dashboard;
            EngineOrgAiRadio.IsChecked = Settings.Compiler == CompilerKind.Dashboard;

            DevDirectApiPanel.Visibility = DevMode.Enabled ? Visibility.Visible : Visibility.Collapsed;
            if (DevMode.Enabled)
            {
                DevTranscriberSelect.SelectedIndex = Settings.Transcriber == TranscriberKind.OpenAiCompatible ? 1 : 0;
                DevCompilerSelect.SelectedIndex = Settings.Compiler == CompilerKind.OpenAiCompatible ? 1 : 0;
                DevTranscriberAddressBox.Text = Settings.TranscriberUrl;
                DevTranscriberModelBox.Text = Settings.TranscriberModel;
                DevCompilerAddressBox.Text = Settings.CompilerUrl;
                DevCompilerModelBox.Text = Settings.CompilerModel;
                DevTranscriberApiKeyBox.Password = _flow.Ai.KeyStore.Get("transcriber") ?? string.Empty;
                DevCompilerApiKeyBox.Password = _flow.Ai.KeyStore.Get("compiler") ?? string.Empty;
            }

            DevSettingInactiveCaption.Visibility = !DevMode.Enabled
                && (Settings.Transcriber != TranscriberKind.LocalWhisper || Settings.Compiler == CompilerKind.OpenAiCompatible)
                ? Visibility.Visible : Visibility.Collapsed;

            _ = CheckOrgAiAvailabilityAsync();
            _ = RefreshOllamaModelListAsync();
        }

        //-----------------------------
        //refreshes microphone dropdown options and selection
        private void RefreshMicrophones()
        {
            if (Settings == null) return;
            var options = _micCatalog.List();
            MicrophoneSelect.ItemsSource = options.Select(o => o.Name).ToList();

            var currentId = Settings.MicrophoneDeviceId;
            if (string.IsNullOrWhiteSpace(currentId))
            {
                MicrophoneSelect.SelectedIndex = 0;
                MicDisconnectedCaption.Visibility = Visibility.Collapsed;
            }
            else
            {
                var idx = options.ToList().FindIndex(o => o.Id == currentId);
                MicrophoneSelect.SelectedIndex = idx >= 0 ? idx : 0;
                MicDisconnectedCaption.Text = "Your chosen microphone isn't connected. Using Windows default.";
                MicDisconnectedCaption.Visibility = idx >= 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        //-----------------------------
        //refreshes ai engine details and model directory locations
        private void RefreshAiEngine()
        {
            if (Settings == null) return;
            EngineManagementText.Text = Settings.OllamaManagedByWidget ? "Managed by Time Planner" : "Your own Ollama";
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var ollamaModelsPath = Path.Combine(userProfile, ".ollama", "models");
            var voiceModelsPath = Path.Combine(localAppData, "TimePlanner", "models");
            EngineLocationsText.Text = $"Models stored in:\n• Ollama: {ollamaModelsPath}\n• Voice: {voiceModelsPath}";
        }

        //-----------------------------
        //updates speed check caption under day hardware select
        private void UpdateHardwareCaption()
        {
            var check = Settings?.LastSpeedCheck;
            DayHardwareCaption.Text = (check?.GraphicsTokensPerSec != null && check.ProcessorTokensPerSec != null)
                ? $"Last check: graphics {check.GraphicsTokensPerSec:F0} tokens/s · processor {check.ProcessorTokensPerSec:F0} tokens/s"
                : "Run a check to see how fast each runs on this PC";
        }

        //-----------------------------
        //handles closing advanced panel
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            StopMicrophoneTest();
            CloseRequested?.Invoke();
        }

        private void DayHardware_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Settings == null) return;
            Settings.DayHardware = (HardwareChoice)Math.Clamp(DayHardwareSelect.SelectedIndex, 0, 2);
            _flow?.SavePreferences();
            HardwareChanged?.Invoke();
        }

        private void VoiceHardware_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Settings == null) return;
            Settings.VoiceHardware = VoiceHardwareSelect.SelectedIndex == 1 ? VoiceHardwareChoice.Processor : VoiceHardwareChoice.Automatic;
            VoiceRestartCaption.Visibility = Settings.VoiceHardware != _initialVoiceHardware ? Visibility.Visible : Visibility.Collapsed;
            _flow?.SavePreferences();
            HardwareChanged?.Invoke();
        }

        //-----------------------------
        //handles speed check button click
        private async void SpeedCheck_Click(object sender, RoutedEventArgs e)
        {
            if (Settings == null || _flow == null) return;
            SpeedCheckButton.IsEnabled = false;
            DayHardwareCaption.Text = "Testing graphics card and processor…";
            _speedCheckCts?.Cancel();
            _speedCheckCts = new CancellationTokenSource();
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(Settings.CompilerUrl) };
                var model = string.IsNullOrWhiteSpace(Settings.CompilerModel) ? "qwen2.5:3b" : Settings.CompilerModel;
                var res = await SpeedCheck.RunAsync(http, model, _speedCheckCts.Token);
                Settings.LastSpeedCheck = new SpeedResult(res.GraphicsTokensPerSec, res.ProcessorTokensPerSec, Settings.LastSpeedCheck?.VoiceSecondsGraphics, Settings.LastSpeedCheck?.VoiceSecondsProcessor, DateTime.UtcNow);
                _flow.SavePreferences();
                UpdateHardwareCaption();
                HardwareChanged?.Invoke();
            }
            catch { DayHardwareCaption.Text = "Speed check couldn't finish. Is the AI engine running?"; }
            finally { SpeedCheckButton.IsEnabled = true; }
        }

        private void Microphone_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Settings == null) return;
            var options = _micCatalog.List();
            var selectedIdx = MicrophoneSelect.SelectedIndex;
            if (selectedIdx >= 0 && selectedIdx < options.Count)
            {
                Settings.MicrophoneDeviceId = options[selectedIdx].Id;
                _flow?.SavePreferences();
            }
        }

        //-----------------------------
        //handles test microphone button click
        private void TestMicrophone_Click(object sender, RoutedEventArgs e)
        {
            TestMicButton.IsEnabled = false;
            MicTestProgress.Value = 0;
            MicTestProgress.Visibility = Visibility.Visible;
            MicTestResult.Text = "Listening for 3 seconds…";
            OpenSoundSettingsButton.Visibility = Visibility.Collapsed;

            _testRecorder?.Dispose();
            _testRecorder = new AudioRecorder(Settings, _micCatalog);
            _testRecorder.LevelChanged += level => Dispatcher.Invoke(() => MicTestProgress.Value = Math.Clamp(level * 2, 0.0, 1.0));
            _testRecorder.Stopped += (path, length, error) => Dispatcher.Invoke(() =>
            {
                MicTestProgress.Visibility = Visibility.Collapsed;
                TestMicButton.IsEnabled = true;
                if (_testRecorder.HeardSpeech)
                {
                    MicTestResult.Text = "Working: we can hear you.";
                }
                else if (_testRecorder.IsDigitalSilence)
                {
                    MicTestResult.Text = "Windows gave us silence. This can happen when the microphone is blocked, or with some recording formats.";
                    OpenSoundSettingsButton.Visibility = Visibility.Visible;
                }
                else
                {
                    MicTestResult.Text = "We couldn't hear anything. Check the microphone isn't muted, or choose another one.";
                }
            });

            _testRecorder.Start(saveToFile: false);
            _testTimer?.Stop();
            _testTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _testTimer.Tick += (s, ev) => { _testTimer.Stop(); _testRecorder.Stop(); };
            _testTimer.Start();
        }

        private void StopMicrophoneTest()
        {
            _testTimer?.Stop();
            if (_testRecorder?.IsRecording == true) _testRecorder.Stop();
            _testRecorder?.Dispose();
            _testRecorder = null;
        }

        private void OpenSoundSettings_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); } catch { }
        }

        private void RemoveEngine_Click(object sender, RoutedEventArgs e)
        {
            RemoveEngineButton.Visibility = Visibility.Collapsed;
            RemoveConfirmPanel.Visibility = Visibility.Visible;
        }

        private void RemoveConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            RemoveConfirmPanel.Visibility = Visibility.Collapsed;
            RemoveEngineButton.Visibility = Visibility.Visible;
        }

        //-----------------------------
        //executes removal of engine and models per d6
        private async void RemoveConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            if (Settings == null || _flow == null) return;
            StopMicrophoneTest();
            RemoveConfirmPanel.Visibility = Visibility.Collapsed;

            //1. stop ollama host
            _flow.AiHost?.Stop();

            //2. if managed by widget, run uninstaller and purge ollama models
            if (Settings.OllamaManagedByWidget)
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var uninstaller = Path.Combine(localAppData, "Programs", "Ollama", "unins000.exe");
                if (File.Exists(uninstaller))
                {
                    try
                    {
                        var psi = new ProcessStartInfo(uninstaller, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = false, CreateNoWindow = true };
                        using var proc = Process.Start(psi);
                        if (proc != null) await proc.WaitForExitAsync();
                    }
                    catch { }
                }

                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var ollamaModels = Path.Combine(userProfile, ".ollama", "models");
                if (Directory.Exists(ollamaModels)) { try { Directory.Delete(ollamaModels, recursive: true); } catch { } }
            }
            else
            {
                OwnOllamaNotice.Text = "Your own Ollama was left installed.";
                OwnOllamaNotice.Visibility = Visibility.Visible;
            }

            //3. delete whisper files in %localappdata%\timeplanner\models
            try
            {
                var whisperFolder = _flow.ModelManager.ModelsFolder;
                if (Directory.Exists(whisperFolder))
                {
                    foreach (var file in Directory.GetFiles(whisperFolder)) { try { File.Delete(file); } catch { } }
                }
            }
            catch { }

            //4. set setup flags and save
            Settings.SetupCompleted = false;
            Settings.VoiceEnabled = false;
            Settings.OllamaManagedByWidget = false;
            _flow.SavePreferences();

            //5. notify parent to reset card
            ResetRequested?.Invoke();
        }

        private async Task RefreshOllamaModelListAsync()
        {
            if (Settings == null || _flow == null) return;
            try
            {
                var models = await _flow.Ai.ListOllamaModelsAsync(CancellationToken.None);
                if (models.Count > 0)
                {
                    OllamaModelSelect.ItemsSource = models;
                    var curIdx = models.ToList().IndexOf(Settings.CompilerModel);
                    OllamaModelSelect.SelectedIndex = curIdx >= 0 ? curIdx : 0;
                }
            }
            catch { }
        }

        private void OllamaAddress_TextChanged(object sender, TextChangedEventArgs e) { if (Settings != null) { Settings.CompilerUrl = OllamaAddressBox.Text; _flow?.SavePreferences(); } }
        private void OllamaModel_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (Settings != null && OllamaModelSelect.SelectedItem is string m && !string.IsNullOrWhiteSpace(m)) { Settings.CompilerModel = m; _flow?.SavePreferences(); } }
        private async void RefreshOllamaModels_Click(object sender, RoutedEventArgs e) { RefreshOllamaModelsButton.IsEnabled = false; await RefreshOllamaModelListAsync(); RefreshOllamaModelsButton.IsEnabled = true; }

        private async void TestOllama_Click(object sender, RoutedEventArgs e)
        {
            if (_flow == null) return;
            TestOllamaButton.IsEnabled = false;
            OllamaTestResult.Text = "Connecting…";
            try
            {
                var status = await _flow.Ai.CheckOllamaAsync(CancellationToken.None);
                OllamaTestResult.Text = status.State == OllamaState.Ready ? "Working: Ollama is ready." : (status.State == OllamaState.ModelMissing ? "The day model isn't downloaded yet." : "The AI engine isn't running.");
            }
            catch (Exception ex) { OllamaTestResult.Text = ex.Message; }
            finally { TestOllamaButton.IsEnabled = true; }
        }

        private void ChooseVoiceFile_Click(object sender, RoutedEventArgs e)
        {
            if (Settings == null) return;
            var dlg = new OpenFileDialog { Filter = "ggml models (*.bin)|*.bin|All files (*.*)|*.*", Title = "Choose a voice model" };
            if (dlg.ShowDialog() == true) { Settings.WhisperModelPath = dlg.FileName; VoiceModelPathBox.Text = dlg.FileName; _flow?.SavePreferences(); }
        }

        private void ClearVoiceFile_Click(object sender, RoutedEventArgs e)
        {
            if (Settings == null) return;
            Settings.WhisperModelPath = null;
            VoiceModelPathBox.Text = string.Empty;
            _flow?.SavePreferences();
        }

        private void TestVoice_Click(object sender, RoutedEventArgs e)
        {
            if (_flow == null) return;
            var voiceModel = string.IsNullOrWhiteSpace(Settings?.WhisperModel) ? "ggml-small.en-q5_1.bin" : Settings.WhisperModel;
            var file = Settings?.WhisperModelPath ?? Path.Combine(_flow.ModelManager.ModelsFolder, voiceModel);
            VoiceTestResult.Text = File.Exists(file) ? "Voice model is downloaded and ready." : "Voice model file not found on disk.";
        }

        private async Task CheckOrgAiAvailabilityAsync()
        {
            if (_flow == null) return;
            try
            {
                var email = await _flow.Logs.GetDashboardEmailAsync();
                var signedIn = !string.IsNullOrWhiteSpace(email);
                EngineOrgAiRadio.IsEnabled = signedIn;
                OrgAiDisabledReason.Text = signedIn ? string.Empty : "Sign in to the dashboard first";
                OrgAiDisabledReason.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
            }
            catch { }
        }

        private void Engine_Checked(object sender, RoutedEventArgs e)
        {
            if (Settings == null) return;
            Settings.Compiler = EngineOrgAiRadio.IsChecked == true ? CompilerKind.Dashboard : CompilerKind.Ollama;
            OrgAiNotice.Visibility = Settings.Compiler == CompilerKind.Dashboard ? Visibility.Visible : Visibility.Collapsed;
            _flow?.SavePreferences();
            HardwareChanged?.Invoke();
        }

        //dev direct api handlers
        private void DevTranscriber_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Settings == null) return;
            Settings.Transcriber = DevTranscriberSelect.SelectedIndex == 1 ? TranscriberKind.OpenAiCompatible : TranscriberKind.LocalWhisper;
            DevTranscriberDetailsPanel.Visibility = Settings.Transcriber == TranscriberKind.OpenAiCompatible ? Visibility.Visible : Visibility.Collapsed;
            _flow?.SavePreferences();
        }

        private void DevCompiler_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Settings == null) return;
            Settings.Compiler = DevCompilerSelect.SelectedIndex == 1 ? CompilerKind.OpenAiCompatible : CompilerKind.Ollama;
            DevCompilerDetailsPanel.Visibility = Settings.Compiler == CompilerKind.OpenAiCompatible ? Visibility.Visible : Visibility.Collapsed;
            _flow?.SavePreferences();
        }

        private void DevTranscriberAddress_TextChanged(object sender, TextChangedEventArgs e) { if (Settings != null) { Settings.TranscriberUrl = DevTranscriberAddressBox.Text; _flow?.SavePreferences(); } }
        private void DevTranscriberModel_TextChanged(object sender, TextChangedEventArgs e) { if (Settings != null) { Settings.TranscriberModel = DevTranscriberModelBox.Text; _flow?.SavePreferences(); } }
        private void DevTranscriberApiKey_PasswordChanged(object sender, RoutedEventArgs e) { _flow?.Ai.KeyStore.Set("transcriber", DevTranscriberApiKeyBox.Password); }
        private void DevCompilerAddress_TextChanged(object sender, TextChangedEventArgs e) { if (Settings != null) { Settings.CompilerUrl = DevCompilerAddressBox.Text; _flow?.SavePreferences(); } }
        private void DevCompilerModel_TextChanged(object sender, TextChangedEventArgs e) { if (Settings != null) { Settings.CompilerModel = DevCompilerModelBox.Text; _flow?.SavePreferences(); } }
        private void DevCompilerApiKey_PasswordChanged(object sender, RoutedEventArgs e) { _flow?.Ai.KeyStore.Set("compiler", DevCompilerApiKeyBox.Password); }

        private async void DevTestTranscriber_Click(object sender, RoutedEventArgs e)
        {
            if (_flow == null) return;
            DevTestTranscriberButton.IsEnabled = false;
            DevTranscriberTestResult.Text = "Testing…";
            try { var err = await _flow.Ai.TestTranscriberAsync(CancellationToken.None); DevTranscriberTestResult.Text = err ?? "Working"; }
            catch (Exception ex) { DevTranscriberTestResult.Text = ex.Message; }
            finally { DevTestTranscriberButton.IsEnabled = true; }
        }

        private async void DevTestCompiler_Click(object sender, RoutedEventArgs e)
        {
            if (_flow == null) return;
            DevTestCompilerButton.IsEnabled = false;
            DevCompilerTestResult.Text = "Testing…";
            try { var err = await _flow.Ai.TestCompilerAsync(CancellationToken.None); DevCompilerTestResult.Text = err ?? "Working"; }
            catch (Exception ex) { DevCompilerTestResult.Text = ex.Message; }
            finally { DevTestCompilerButton.IsEnabled = true; }
        }
    }
}
//------------------------------EOF-----------------------------\\
