using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TimePlanner.Core.Ai;
using TimePlanner.Widget.Services;
using TimePlanner.Widget.Services.Ai;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //main voice notes and day compilation settings view hosting advanced panel
    public partial class AiSettingsView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly Action _back;
        private readonly bool _ready;
        private readonly VoiceHardwareChoice _initialVoiceHardware;

        private CancellationTokenSource? _setupCts;
        private CancellationTokenSource? _dayModelDownloadCts;
        private CancellationTokenSource? _voiceModelDownloadCts;

        private AiSettings Settings => _flow.Session.Preferences.Ai;

        //-----------------------------
        //initializes settings view and populates controls from preferences
        public AiSettingsView(WidgetFlow flow, Action back, bool openAdvanced = false)
        {
            InitializeComponent();
            _flow = flow;
            _back = back;
            _initialVoiceHardware = Settings.VoiceHardware;

            //wire setup card actions
            SetupCard.InstallRequested += async () => await RunSetupAsync(1);
            SetupCard.CancelRequested += () => _setupCts?.Cancel();
            SetupCard.RetryRequested += async () => await RunSetupAsync(1);
            SetupCard.UseFasterRequested += async () => await UseFasterModelAsync();
            SetupCard.DismissSlowPcRequested += () =>
            {
                Settings.SlowPcHintDismissed = true;
                Save();
                _ = RefreshCardAndOrgStatusAsync();
            };

            //configure dev mode badge
            DevBadge.Visibility = DevMode.Enabled ? Visibility.Visible : Visibility.Collapsed;

            //populate build my day models
            DayModelSelect.ItemsSource = AiModelCatalog.DayModels.Select(m => $"{m.Label} ({m.SizeEstimate})").ToList();
            var dayModelIdx = AiModelCatalog.DayModels.ToList().FindIndex(m => m.Id == Settings.CompilerModel);
            DayModelSelect.SelectedIndex = dayModelIdx >= 0 ? dayModelIdx : 0;

            //populate voice accuracy models
            VoiceModelSelect.ItemsSource = AiModelCatalog.VoiceModels.Select(m => $"{m.Label} ({m.SizeEstimate})").ToList();
            var voiceModelIdx = AiModelCatalog.VoiceModels.ToList().FindIndex(m => m.File == Settings.WhisperModel);
            VoiceModelSelect.SelectedIndex = voiceModelIdx >= 0 ? voiceModelIdx : 0;

            //initialize advanced panel
            AdvancedPanel.Initialize(_flow, _initialVoiceHardware);
            AdvancedPanel.CloseRequested += () =>
            {
                AdvancedPanel.Visibility = Visibility.Collapsed;
                MainView.Visibility = Visibility.Visible;
                _ = RefreshCardAndOrgStatusAsync();
                UpdateAdvancedSummary();
            };
            AdvancedPanel.ResetRequested += () =>
            {
                AdvancedPanel.Visibility = Visibility.Collapsed;
                MainView.Visibility = Visibility.Visible;
                _ = RefreshCardAndOrgStatusAsync();
                UpdateAdvancedSummary();
            };
            AdvancedPanel.HardwareChanged += () =>
            {
                UpdateAdvancedSummary();
            };

            UpdateVoiceSwitch();
            UpdateDownloadButtons();
            UpdateAdvancedSummary();

            _ = RefreshCardAndOrgStatusAsync();
            _ready = true;

            //open advanced view directly when requested
            if (openAdvanced)
            {
                MainView.Visibility = Visibility.Collapsed;
                AdvancedPanel.Visibility = Visibility.Visible;
                AdvancedPanel.Refresh();
            }
        }

        //-----------------------------
        //saves preferences to persistent store
        private void Save()
        {
            _flow.SavePreferences();
        }

        //-----------------------------
        //updates voice notes switch state and tooltip based on readiness
        private void UpdateVoiceSwitch(AiReadiness? readiness = null)
        {
            var voiceReady = readiness?.VoiceReady ?? _flow.AiSetup.IsVoiceReady;
            VoiceEnabledSwitch.IsEnabled = voiceReady;
            VoiceEnabledSwitch.IsChecked = voiceReady && Settings.VoiceEnabled;
            VoiceEnabledSwitch.ToolTip = voiceReady ? "Enable or disable voice notes" : (readiness?.VoiceProblem ?? "Voice model not ready");
        }

        //-----------------------------
        //updates inline download button visibilities and labels based on readiness
        private void UpdateDownloadButtons(AiReadiness? readiness = null)
        {
            var dayModel = AiModelCatalog.DayModels.ElementAtOrDefault(DayModelSelect.SelectedIndex) ?? AiModelCatalog.DayModels[0];
            var dayPresent = dayModel.Id == Settings.CompilerModel && (readiness?.DayReady ?? false);
            DayModelDownloadButton.Visibility = dayPresent ? Visibility.Collapsed : Visibility.Visible;
            DayModelDownloadButton.Content = $"Download ({dayModel.SizeEstimate})";

            var voiceModel = AiModelCatalog.VoiceModels.ElementAtOrDefault(VoiceModelSelect.SelectedIndex) ?? AiModelCatalog.VoiceModels[0];
            var voicePresent = _flow.ModelManager.IsDownloaded(voiceModel.File);
            VoiceModelDownloadButton.Visibility = voicePresent ? Visibility.Collapsed : Visibility.Visible;
            VoiceModelDownloadButton.Content = $"Download ({voiceModel.SizeEstimate})";
        }

        //-----------------------------
        //updates summary text displayed on advanced nav row
        private void UpdateAdvancedSummary()
        {
            var hw = Settings.DayHardware switch
            {
                HardwareChoice.Graphics => "Graphics",
                HardwareChoice.Processor => "Processor",
                _ => "Automatic"
            };

            var engine = Settings.Compiler == CompilerKind.Dashboard ? "Organisation AI" : "This PC";
            AdvancedSummary.Text = $"{hw} · {engine}";
        }

        //-----------------------------
        //queries readiness and updates setup card state and slow pc hint
        private async Task RefreshCardAndOrgStatusAsync()
        {
            AiReadiness readiness;
            try
            {
                readiness = await _flow.AiSetup.GetReadinessAsync();
            }
            catch
            {
                readiness = new AiReadiness(false, false, "The voice model isn't downloaded.", "The AI engine isn't running.");
            }

            UpdateVoiceSwitch(readiness);
            UpdateDownloadButtons(readiness);

            if (Settings.Compiler == CompilerKind.Dashboard)
            {
                SetupCard.SetOrganisationAi();
            }
            else if (!Settings.SetupCompleted)
            {
                SetupCard.SetNotSetUp();
            }
            else if (!readiness.VoiceReady || !readiness.DayReady)
            {
                var problem = readiness.DayProblem ?? readiness.VoiceProblem ?? "AI is not ready.";
                SetupCard.SetNeedsAttention(problem, "Check again");
            }
            else
            {
                var hwName = Settings.DayHardware switch
                {
                    HardwareChoice.Graphics => "the graphics card",
                    HardwareChoice.Processor => "the processor",
                    _ => (Settings.LastSpeedCheck?.GpuLayers != 0 ? "the graphics card" : "the processor")
                };

                //slow pc hint check per e1
                var speedCheck = Settings.LastSpeedCheck;
                var fasterTps = Math.Max(speedCheck?.GraphicsTokensPerSec ?? 0, speedCheck?.ProcessorTokensPerSec ?? 0);
                var isRecommended = Settings.CompilerModel == AiModelCatalog.DayModels[0].Id;
                var showSlowPc = fasterTps > 0 && fasterTps < 8 && isRecommended && !Settings.SlowPcHintDismissed;

                SetupCard.SetReady($"Build my day uses {hwName}", showSlowPc);
            }
        }

        //-----------------------------
        //selects faster model and triggers download per e1
        private async Task UseFasterModelAsync()
        {
            Settings.CompilerModel = "qwen2.5:1.5b";
            Settings.SlowPcHintDismissed = true;
            Save();

            var idx = AiModelCatalog.DayModels.ToList().FindIndex(m => m.Id == "qwen2.5:1.5b");
            if (idx >= 0)
            {
                DayModelSelect.SelectedIndex = idx;
            }

            DayModelDownload_Click(this, new RoutedEventArgs());
            await RefreshCardAndOrgStatusAsync();
        }

        //-----------------------------
        //executes 4 step setup pipeline with cancellation support
        private async Task RunSetupAsync(int startStep)
        {
            _setupCts = new CancellationTokenSource();
            var progress = new Progress<AiSetupProgress>(p =>
            {
                SetupCard.SetInstalling(p.Step, p.TotalSteps, p.Description, p.Fraction, p.Step <= 3);
            });

            //execute setup steps
            try
            {
                await _flow.AiSetup.RunSetupAsync(startStep, progress, _setupCts.Token);
                Settings.SetupCompleted = true;
                Settings.VoiceEnabled = true;
                Save();

                UpdateVoiceSwitch();
                UpdateDownloadButtons();
                UpdateAdvancedSummary();
                await RefreshCardAndOrgStatusAsync();
            }
            //handle cancellation
            catch (OperationCanceledException)
            {
                SetupCard.SetNotSetUp();
            }
            //handle setup failure
            catch (Exception ex)
            {
                SetupCard.SetNeedsAttention(ex.Message, "Try again");
            }
            finally
            {
                _setupCts.Dispose();
                _setupCts = null;
            }
        }

        //-----------------------------
        //saves preferences and navigates back to caller
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            Save();
            _back();
        }

        //-----------------------------
        //switches view from main page to advanced page
        private void OpenAdvanced_Click(object sender, RoutedEventArgs e)
        {
            MainView.Visibility = Visibility.Collapsed;
            AdvancedPanel.Visibility = Visibility.Visible;
            AdvancedPanel.Refresh();
        }

        //-----------------------------
        //handles voice enabled toggle state change
        private void VoiceEnabled_Changed(object sender, RoutedEventArgs e)
        {
            //guard during initialization
            if (!_ready)
            {
                return;
            }

            Settings.VoiceEnabled = VoiceEnabledSwitch.IsChecked == true;
            Save();
        }

        //-----------------------------
        //handles day model selection change
        private void DayModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //guard during initialization
            if (!_ready || DayModelSelect.SelectedIndex < 0)
            {
                return;
            }

            var choice = AiModelCatalog.DayModels[DayModelSelect.SelectedIndex];
            Settings.CompilerModel = choice.Id;
            Save();
            UpdateDownloadButtons();
        }

        //-----------------------------
        //downloads selected day model with progress reporting
        private async void DayModelDownload_Click(object sender, RoutedEventArgs e)
        {
            DayModelDownloadButton.IsEnabled = false;
            DayModelProgressBar.Visibility = Visibility.Visible;
            _dayModelDownloadCts = new CancellationTokenSource();

            var progress = new Progress<double>(f => DayModelProgressBar.Value = Math.Clamp(f, 0.0, 1.0));
            //pull model from ollama
            try
            {
                await _flow.Ai.PullOllamaAsync(progress, _dayModelDownloadCts.Token);
                DayModelProgressBar.Visibility = Visibility.Collapsed;
                DayModelDownloadButton.Visibility = Visibility.Collapsed;
                Save();
                await RefreshCardAndOrgStatusAsync();
            }
            //handle cancellation or failure
            catch
            {
                DayModelProgressBar.Visibility = Visibility.Collapsed;
                DayModelDownloadButton.IsEnabled = true;
            }
            finally
            {
                _dayModelDownloadCts.Dispose();
                _dayModelDownloadCts = null;
            }
        }

        //-----------------------------
        //handles voice model selection change
        private void VoiceModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //guard during initialization
            if (!_ready || VoiceModelSelect.SelectedIndex < 0)
            {
                return;
            }

            var choice = AiModelCatalog.VoiceModels[VoiceModelSelect.SelectedIndex];
            Settings.WhisperModel = choice.File;
            Save();
            UpdateDownloadButtons();
        }

        //-----------------------------
        //downloads selected voice model with progress reporting
        private async void VoiceModelDownload_Click(object sender, RoutedEventArgs e)
        {
            var choice = AiModelCatalog.VoiceModels.ElementAtOrDefault(VoiceModelSelect.SelectedIndex) ?? AiModelCatalog.VoiceModels[0];
            VoiceModelDownloadButton.IsEnabled = false;
            VoiceModelProgressBar.Visibility = Visibility.Visible;
            _voiceModelDownloadCts = new CancellationTokenSource();

            var progress = new Progress<double>(f => VoiceModelProgressBar.Value = Math.Clamp(f, 0.0, 1.0));
            //download model to models folder
            try
            {
                await _flow.ModelManager.DownloadAsync(choice.File, progress, _voiceModelDownloadCts.Token);
                VoiceModelProgressBar.Visibility = Visibility.Collapsed;
                VoiceModelDownloadButton.Visibility = Visibility.Collapsed;
                Save();
                await RefreshCardAndOrgStatusAsync();
            }
            //handle cancellation or failure
            catch
            {
                VoiceModelProgressBar.Visibility = Visibility.Collapsed;
                VoiceModelDownloadButton.IsEnabled = true;
            }
            finally
            {
                _voiceModelDownloadCts.Dispose();
                _voiceModelDownloadCts = null;
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
