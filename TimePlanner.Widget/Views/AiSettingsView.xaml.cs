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

            //populate build my day models: each shows its name, and its size at the right
            DayModelSelect.ItemsSource = AiModelCatalog.DayModels;
            var dayModelIdx = AiModelCatalog.DayModels.ToList().FindIndex(m => m.Id == Settings.CompilerModel);
            DayModelSelect.SelectedIndex = dayModelIdx >= 0 ? dayModelIdx : 0;

            //populate voice models, without the "(recommended)" note so the name and size fit beside each other
            VoiceModelSelect.ItemsSource = AiModelCatalog.VoiceModels
                .Select(m => new { Label = m.Label.Replace(" (recommended)", string.Empty), m.SizeEstimate })
                .ToList();
            var voiceModelIdx = AiModelCatalog.VoiceModels.ToList().FindIndex(m => m.File == Settings.WhisperModel);
            VoiceModelSelect.SelectedIndex = voiceModelIdx >= 0 ? voiceModelIdx : 0;

            //initialize advanced panel
            AdvancedPanel.Initialize(_flow, _initialVoiceHardware);
            AdvancedPanel.CloseRequested += () =>
            {
                AdvancedPanel.Visibility = Visibility.Collapsed;
                MainView.Visibility = Visibility.Visible;
                _ = RefreshCardAndOrgStatusAsync();
            };
            AdvancedPanel.ResetRequested += () =>
            {
                AdvancedPanel.Visibility = Visibility.Collapsed;
                MainView.Visibility = Visibility.Visible;
                _ = RefreshCardAndOrgStatusAsync();
            };

            UpdateVoiceSwitch();
            UpdateDownloadButtons();
            ShowSetUpState();

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
            DayModelDownloadButton.ToolTip = $"Download {dayModel.Label} ({dayModel.SizeEstimate})";

            var voiceModel = AiModelCatalog.VoiceModels.ElementAtOrDefault(VoiceModelSelect.SelectedIndex) ?? AiModelCatalog.VoiceModels[0];
            var voicePresent = _flow.ModelManager.IsDownloaded(voiceModel.File);
            VoiceModelDownloadButton.Visibility = voicePresent ? Visibility.Collapsed : Visibility.Visible;
            VoiceModelDownloadButton.ToolTip = $"Download {voiceModel.Label} ({voiceModel.SizeEstimate})";

            _dayPresent = dayPresent;
            _voicePresent = voicePresent;
            UpdateSteps();
        }

        private enum StepState { Done, Current, Pending }

        private bool _engineInstalled;
        private bool _dayPresent;
        private bool _voicePresent;

        //-----------------------------
        //before setup: the two ways to install and Close; after it: the voice switch, what's installed and Done
        private void ShowSetUpState()
        {
            var notSetUp = !Settings.SetupCompleted && Settings.Compiler != CompilerKind.Dashboard;
            NotSetUpPanel.Visibility = notSetUp ? Visibility.Visible : Visibility.Collapsed;
            InstalledPanel.Visibility = notSetUp ? Visibility.Collapsed : Visibility.Visible;
            CloseButton.Visibility = notSetUp ? Visibility.Visible : Visibility.Collapsed;
            DoneButton.Visibility = notSetUp ? Visibility.Collapsed : Visibility.Visible;
        }

        //-----------------------------
        //the installed banner and the custom install steps. One step at a time: the engine first,
        //then the Build my day model, then the voice model; a step whose turn hasn't come is greyed with its picker hidden
        private void UpdateSteps()
        {
            //the banner shows while everything is ready; otherwise the setup card says what's wrong
            var dayReady = _flow.AiSetup.LastReadiness?.DayReady ?? false;
            var voiceReady = _flow.AiSetup.LastReadiness?.VoiceReady ?? _flow.AiSetup.IsVoiceReady;
            InstalledStatus.Visibility = dayReady && voiceReady ? Visibility.Visible : Visibility.Collapsed;

            var engine = _engineInstalled ? StepState.Done : StepState.Current;
            var dayStep = _dayPresent ? StepState.Done : _engineInstalled ? StepState.Current : StepState.Pending;
            var voiceStep = _voicePresent ? StepState.Done : _engineInstalled && _dayPresent ? StepState.Current : StepState.Pending;

            SetStep(EngineCircle, EngineTick, EngineNumber, EngineTitle, engine);
            EngineInstallButton.Visibility = engine == StepState.Done ? Visibility.Collapsed : Visibility.Visible;
            //once the engine is in, the whole install can still run again
            ReinstallButton.Visibility = engine == StepState.Done ? Visibility.Visible : Visibility.Collapsed;

            SetStep(DayCircle, DayTick, DayNumber, DayTitle, dayStep);
            DayControls.Visibility = dayStep == StepState.Pending ? Visibility.Collapsed : Visibility.Visible;
            DayPendingText.Visibility = dayStep == StepState.Pending ? Visibility.Visible : Visibility.Collapsed;

            SetStep(VoiceCircle, VoiceTick, VoiceNumber, VoiceTitle, voiceStep);
            VoiceControls.Visibility = voiceStep == StepState.Pending ? Visibility.Collapsed : Visibility.Visible;
            VoicePendingText.Visibility = voiceStep == StepState.Pending ? Visibility.Visible : Visibility.Collapsed;
        }

        //-----------------------------
        //one step's circle and title: a green tick on pale green, a white number on purple, or a grey number in an outline
        private void SetStep(Border circle, Controls.Icon tick, TextBlock? number, TextBlock? title, StepState state)
        {
            circle.Background = state switch
            {
                StepState.Done => Res("SuccessFillBrush"),
                StepState.Current => Res("ActionBrush"),
                _ => Res("SurfaceBrush"),
            };
            circle.BorderBrush = Res("BorderControlBrush");
            circle.BorderThickness = state == StepState.Pending ? new Thickness(1) : new Thickness(0);
            tick.Foreground = Res("LiveBrush");
            tick.Visibility = state == StepState.Done ? Visibility.Visible : Visibility.Collapsed;

            if (number != null)
            {
                number.Visibility = state == StepState.Done ? Visibility.Collapsed : Visibility.Visible;
                number.Foreground = state == StepState.Current ? Res("TextOnActionBrush") : Res("TextTertiaryBrush");
            }

            if (title != null)
                title.Foreground = state == StepState.Pending ? Res("TextTertiaryBrush") : Res("TextPrimaryBrush");
        }

        private System.Windows.Media.Brush Res(string key) => (System.Windows.Media.Brush)FindResource(key);

        //-----------------------------
        //whether the AI engine answers; asked only when Build my day runs on this PC
        private async Task CheckEngineAsync()
        {
            if (Settings.Compiler != CompilerKind.Ollama)
            {
                _engineInstalled = false;
                return;
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                _engineInstalled = (await _flow.Ai.CheckOllamaAsync(cts.Token)).State != OllamaState.NotRunning;
            }
            catch
            {
                _engineInstalled = false;
            }
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

            await CheckEngineAsync();
            UpdateVoiceSwitch(readiness);
            UpdateDownloadButtons(readiness);
            ShowSetUpState();

            //the card shows only when it has something to say; before setup the two install choices stand in for it
            SetupCard.Visibility = Visibility.Visible;
            if (Settings.Compiler == CompilerKind.Dashboard)
            {
                SetupCard.SetOrganisationAi();
            }
            else if (!Settings.SetupCompleted)
            {
                SetupCard.SetNotSetUp();
                SetupCard.Visibility = Visibility.Collapsed;
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
                SetupCard.Visibility = showSlowPc ? Visibility.Visible : Visibility.Collapsed;
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

            //while it installs, the card's progress takes the place of the two install choices
            NotSetUpPanel.Visibility = Visibility.Collapsed;
            SetupCard.Visibility = Visibility.Visible;
            SetupCard.SetInstalling(1, 4, "Starting", null, true);
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
                await RefreshCardAndOrgStatusAsync();
            }
            //handle cancellation: back to the two install choices
            catch (OperationCanceledException)
            {
                SetupCard.SetNotSetUp();
                SetupCard.Visibility = Visibility.Collapsed;
                ShowSetUpState();
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
        //recommended install: the whole setup, in order
        private async void Recommended_Click(object sender, RoutedEventArgs e) => await RunSetupAsync(1);

        //-----------------------------
        //opens custom install, from the install choices or from Change what's installed
        private void OpenCustom_Click(object sender, RoutedEventArgs e)
        {
            MainView.Visibility = Visibility.Collapsed;
            CustomView.Visibility = Visibility.Visible;
            _ = RefreshCardAndOrgStatusAsync();
        }

        //-----------------------------
        //back from custom install to the main page
        private void CloseCustom_Click(object sender, RoutedEventArgs e)
        {
            CustomView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
            _ = RefreshCardAndOrgStatusAsync();
        }

        //-----------------------------
        //step 1 of custom install: the engine comes with the full setup, so this runs it and shows its progress on the main page
        private async void InstallEngine_Click(object sender, RoutedEventArgs e)
        {
            CustomView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
            await RunSetupAsync(1);
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
