using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using TimePlanner.Core.Ai;
using TimePlanner.Widget.Services;
using TimePlanner.Widget.Services.Ai;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //two page settings view for voice notes and day compilation
    public partial class AiSettingsView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly Action _back;
        private readonly bool _ready;
        private readonly VoiceHardwareChoice _initialVoiceHardware;

        private CancellationTokenSource? _setupCts;
        private CancellationTokenSource? _dayModelDownloadCts;
        private CancellationTokenSource? _voiceModelDownloadCts;
        private CancellationTokenSource? _speedCheckCts;

        private AiSettings Settings => _flow.Session.Preferences.Ai;

        //-----------------------------
        //initializes settings view and populates controls from preferences
        public AiSettingsView(WidgetFlow flow, Action back)
        {
            InitializeComponent();
            _flow = flow;
            _back = back;
            _initialVoiceHardware = Settings.VoiceHardware;

            //wire setup card actions
            SetupCard.InstallRequested += async () => await RunSetupAsync(1);
            SetupCard.CancelRequested += () => _setupCts?.Cancel();
            SetupCard.RetryRequested += async () => await RunSetupAsync(1);

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

            //populate hardware choices
            DayHardwareSelect.ItemsSource = new[] { "Automatic (recommended)", "Graphics card", "Processor" };
            DayHardwareSelect.SelectedIndex = Math.Clamp((int)Settings.DayHardware, 0, 2);

            VoiceHardwareSelect.ItemsSource = new[] { "Automatic (recommended)", "Processor only" };
            VoiceHardwareSelect.SelectedIndex = Settings.VoiceHardware == VoiceHardwareChoice.Processor ? 1 : 0;

            //populate your own models section
            OllamaAddressBox.Text = string.IsNullOrWhiteSpace(Settings.CompilerUrl) ? "http://localhost:11434" : Settings.CompilerUrl;
            VoiceModelPathBox.Text = Settings.WhisperModelPath ?? "(built-in model)";

            //populate engine radio buttons
            if (Settings.Compiler == CompilerKind.Dashboard)
            {
                EngineOrgAiRadio.IsChecked = true;
                OrgAiNotice.Visibility = Visibility.Visible;
            }
            else
            {
                EngineThisPcRadio.IsChecked = true;
                OrgAiNotice.Visibility = Visibility.Collapsed;
            }

            //configure developer mode visibility
            if (DevMode.Enabled)
            {
                //dev feature not available to users
                DevDirectApiPanel.Visibility = Visibility.Visible;
                DevTranscriberSelect.ItemsSource = new[] { "Local Whisper", "OpenAI-compatible" };
                DevTranscriberSelect.SelectedIndex = Settings.Transcriber == TranscriberKind.LocalWhisper ? 0 : 1;
                DevTranscriberAddressBox.Text = Settings.TranscriberUrl;
                DevTranscriberModelBox.Text = Settings.TranscriberModel;

                DevCompilerSelect.ItemsSource = new[] { "Ollama", "Organisation AI", "OpenAI-compatible" };
                DevCompilerSelect.SelectedIndex = Settings.Compiler switch
                {
                    CompilerKind.Ollama => 0,
                    CompilerKind.Dashboard => 1,
                    _ => 2
                };
                DevCompilerAddressBox.Text = Settings.CompilerUrl;
                DevCompilerModelBox.Text = Settings.CompilerModel;
            }
            else
            {
                DevDirectApiPanel.Visibility = Visibility.Collapsed;
                //check whether developer settings are inactive in user mode
                if (Settings.Compiler == CompilerKind.OpenAiCompatible || Settings.Transcriber == TranscriberKind.OpenAiCompatible)
                {
                    DevSettingInactiveCaption.Visibility = Visibility.Visible;
                }
            }

            UpdateVoiceSwitch();
            UpdateDownloadButtons();
            UpdateAdvancedSummary();
            UpdateHardwareCaption();

            _ = RefreshCardAndOrgStatusAsync();
            _ready = true;
        }

        //-----------------------------
        //saves preferences to persistent store
        private void Save()
        {
            _flow.SavePreferences();
        }

        //-----------------------------
        //updates voice notes switch state and tooltip
        private void UpdateVoiceSwitch()
        {
            var isSetUp = Settings.SetupCompleted;
            VoiceEnabledSwitch.IsEnabled = isSetUp;
            VoiceEnabledSwitch.IsChecked = isSetUp && Settings.VoiceEnabled;
            VoiceEnabledSwitch.ToolTip = isSetUp ? "Enable or disable voice notes" : "Install first";
        }

        //-----------------------------
        //updates inline download button visibilities and labels
        private void UpdateDownloadButtons()
        {
            var dayModel = AiModelCatalog.DayModels.ElementAtOrDefault(DayModelSelect.SelectedIndex) ?? AiModelCatalog.DayModels[0];
            var dayPresent = dayModel.Id == Settings.CompilerModel && Settings.SetupCompleted;
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
        //updates speed check caption under day hardware select
        private void UpdateHardwareCaption()
        {
            var check = Settings.LastSpeedCheck;
            //format measured tokens per second when available
            if (check?.GraphicsTokensPerSec != null && check.ProcessorTokensPerSec != null)
            {
                DayHardwareCaption.Text = $"Last check: graphics {check.GraphicsTokensPerSec:F0} tokens/s · processor {check.ProcessorTokensPerSec:F0} tokens/s";
            }
            //otherwise state no speed check run yet
            else
            {
                DayHardwareCaption.Text = "Not speed-checked yet";
            }
        }

        //-----------------------------
        //probes local ollama and dashboard status to update card state
        private async Task RefreshCardAndOrgStatusAsync()
        {
            //organisation ai mode displays dedicated summary
            if (Settings.Compiler == CompilerKind.Dashboard)
            {
                SetupCard.SetOrganisationAi();
            }
            //not setup displays initial install prompt
            else if (!Settings.SetupCompleted)
            {
                SetupCard.SetNotSetUp();
            }
            //otherwise verify local engine health
            else
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var status = await _flow.Ai.CheckOllamaAsync(cts.Token);
                    //ready when server running and model exists
                    if (status.State == OllamaState.Ready)
                    {
                        var hwName = Settings.DayHardware switch
                        {
                            HardwareChoice.Graphics => "the graphics card",
                            HardwareChoice.Processor => "the processor",
                            _ => (Settings.LastSpeedCheck?.GpuLayers != 0 ? "the graphics card" : "the processor")
                        };
                        SetupCard.SetReady($"Build my day uses {hwName}");
                    }
                    //prompt engine start when not running
                    else if (status.State == OllamaState.NotRunning)
                    {
                        SetupCard.SetNeedsAttention("The AI engine isn't running.", "Check again");
                    }
                    //prompt model download when model missing
                    else
                    {
                        SetupCard.SetNeedsAttention("The day model isn't downloaded yet.", "Download model");
                    }
                }
                //handle probe timeout or network failure
                catch
                {
                    SetupCard.SetNeedsAttention("Can't reach the AI engine.", "Check again");
                }
            }

            //check dashboard organisation ai availability
            try
            {
                var email = await _flow.Logs.GetDashboardEmailAsync();
                //require dashboard sign in
                if (string.IsNullOrWhiteSpace(email))
                {
                    EngineOrgAiRadio.IsEnabled = false;
                    OrgAiDisabledReason.Text = "Sign in to the dashboard first";
                    OrgAiDisabledReason.Visibility = Visibility.Visible;
                }
                //verify remote organisation ai status
                else
                {
                    var token = await _flow.Logs.GetDashboardAccessTokenAsync();
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    var compiler = new DashboardDayCompiler(http, token, Settings.CompilerUrl);
                    using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var test = await compiler.TestAsync(testCts.Token);

                    //enable option when organisation ai configured
                    if (test == null)
                    {
                        EngineOrgAiRadio.IsEnabled = true;
                        OrgAiDisabledReason.Visibility = Visibility.Collapsed;
                    }
                    //otherwise show disabled reason
                    else
                    {
                        EngineOrgAiRadio.IsEnabled = false;
                        OrgAiDisabledReason.Text = "Not set up by your organisation";
                        OrgAiDisabledReason.Visibility = Visibility.Visible;
                    }
                }
            }
            //treat dashboard status error as unavailable
            catch
            {
                EngineOrgAiRadio.IsEnabled = false;
                OrgAiDisabledReason.Text = "Not set up by your organisation";
                OrgAiDisabledReason.Visibility = Visibility.Visible;
            }
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
                UpdateHardwareCaption();
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
            AdvancedView.Visibility = Visibility.Visible;
        }

        //-----------------------------
        //switches view from advanced page back to main page
        private void CloseAdvanced_Click(object sender, RoutedEventArgs e)
        {
            AdvancedView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
            UpdateAdvancedSummary();
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
            var choice = AiModelCatalog.DayModels.ElementAtOrDefault(DayModelSelect.SelectedIndex) ?? AiModelCatalog.DayModels[0];
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

        //-----------------------------
        //handles day hardware selection change
        private void DayHardware_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //guard during initialization
            if (!_ready || DayHardwareSelect.SelectedIndex < 0)
            {
                return;
            }

            Settings.DayHardware = (HardwareChoice)DayHardwareSelect.SelectedIndex;
            Save();
            UpdateAdvancedSummary();
        }

        //-----------------------------
        //handles voice hardware selection change
        private void VoiceHardware_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //guard during initialization
            if (!_ready || VoiceHardwareSelect.SelectedIndex < 0)
            {
                return;
            }

            var choice = VoiceHardwareSelect.SelectedIndex == 1 ? VoiceHardwareChoice.Processor : VoiceHardwareChoice.Automatic;
            Settings.VoiceHardware = choice;
            VoiceRestartCaption.Visibility = choice != _initialVoiceHardware ? Visibility.Visible : Visibility.Collapsed;
            Save();
        }

        //-----------------------------
        //runs speed benchmark for day model
        private async void SpeedCheck_Click(object sender, RoutedEventArgs e)
        {
            SpeedCheckButton.IsEnabled = false;
            SpeedCheckButton.Content = "Checking speed…";
            _speedCheckCts = new CancellationTokenSource();

            //run speed check
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(Settings.CompilerUrl.TrimEnd('/') + "/") };
                var model = string.IsNullOrWhiteSpace(Settings.CompilerModel) ? "qwen2.5:3b" : Settings.CompilerModel;
                var res = await SpeedCheck.RunAsync(http, model, _speedCheckCts.Token);

                Settings.LastSpeedCheck = new SpeedResult(res.GraphicsTokensPerSec, res.ProcessorTokensPerSec, null, null, DateTime.UtcNow);
                Save();
                UpdateHardwareCaption();
            }
            //ignore speed check failure
            catch
            {
            }
            finally
            {
                SpeedCheckButton.IsEnabled = true;
                SpeedCheckButton.Content = "Run speed check";
                _speedCheckCts.Dispose();
                _speedCheckCts = null;
            }
        }

        //-----------------------------
        //handles ollama address text change
        private void OllamaAddress_TextChanged(object sender, TextChangedEventArgs e)
        {
            //guard during initialization
            if (!_ready)
            {
                return;
            }

            Settings.CompilerUrl = OllamaAddressBox.Text.Trim();
            Save();
        }

        //-----------------------------
        //refreshes installed models list from ollama
        private async void RefreshOllamaModels_Click(object sender, RoutedEventArgs e)
        {
            RefreshOllamaModelsButton.IsEnabled = false;
            //query installed models
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var models = await _flow.Ai.ListOllamaModelsAsync(cts.Token);
                OllamaModelSelect.ItemsSource = models;
                var idx = models.ToList().IndexOf(Settings.CompilerModel);
                //select current model when present
                if (idx >= 0)
                {
                    OllamaModelSelect.SelectedIndex = idx;
                }
            }
            //ignore communication failure
            catch
            {
            }
            finally
            {
                RefreshOllamaModelsButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //handles ollama model selection change
        private void OllamaModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //guard during initialization
            if (!_ready || OllamaModelSelect.SelectedItem is not string selected)
            {
                return;
            }

            Settings.CompilerModel = selected;
            Save();
        }

        //-----------------------------
        //tests ollama server connectivity and model readiness
        private async void TestOllama_Click(object sender, RoutedEventArgs e)
        {
            TestOllamaButton.IsEnabled = false;
            OllamaTestResult.Text = "Testing…";

            //execute test probe
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var err = await _flow.Ai.TestCompilerAsync(cts.Token);
                OllamaTestResult.Text = err ?? "Working";
            }
            //record test failure
            catch (Exception ex)
            {
                OllamaTestResult.Text = ex.Message;
            }
            finally
            {
                TestOllamaButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //prompts user for custom ggml voice model file
        private void ChooseVoiceFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose voice model file",
                Filter = "GGML model files (*.bin)|*.bin|All files (*.*)|*.*"
            };

            //process chosen file
            if (dialog.ShowDialog() == true)
            {
                //validate file size and magic
                if (IsValidGgmlFile(dialog.FileName))
                {
                    Settings.WhisperModelPath = dialog.FileName;
                    VoiceModelPathBox.Text = dialog.FileName;
                    VoiceTestResult.Text = string.Empty;
                    Save();
                }
                //warn on invalid file format
                else
                {
                    VoiceTestResult.Text = "Must be a valid ggml model file at least 10 MB";
                }
            }
        }

        //-----------------------------
        //clears custom voice model file reverting to built in model
        private void ClearVoiceFile_Click(object sender, RoutedEventArgs e)
        {
            Settings.WhisperModelPath = null;
            VoiceModelPathBox.Text = "(built-in model)";
            VoiceTestResult.Text = string.Empty;
            Save();
        }

        //-----------------------------
        //tests voice model transcription readiness
        private async void TestVoice_Click(object sender, RoutedEventArgs e)
        {
            TestVoiceButton.IsEnabled = false;
            VoiceTestResult.Text = "Testing…";

            //execute test probe
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var err = await _flow.Ai.TestTranscriberAsync(cts.Token);
                VoiceTestResult.Text = err ?? "Working";
            }
            //record test failure
            catch (Exception ex)
            {
                VoiceTestResult.Text = ex.Message;
            }
            finally
            {
                TestVoiceButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //handles engine radio button selection
        private void Engine_Checked(object sender, RoutedEventArgs e)
        {
            //guard during initialization
            if (!_ready)
            {
                return;
            }

            var isOrg = EngineOrgAiRadio.IsChecked == true;
            Settings.Compiler = isOrg ? CompilerKind.Dashboard : CompilerKind.Ollama;
            OrgAiNotice.Visibility = isOrg ? Visibility.Visible : Visibility.Collapsed;
            Save();
            UpdateAdvancedSummary();
            _ = RefreshCardAndOrgStatusAsync();
        }

        //-----------------------------
        //checks whether file exists exceeds 10mb and starts with ggml magic
        private static bool IsValidGgmlFile(string path)
        {
            //inspect file size and binary header
            try
            {
                var fi = new FileInfo(path);
                //file must exist and exceed minimum model threshold
                if (!fi.Exists || fi.Length < 10 * 1024 * 1024)
                {
                    return false;
                }

                using var stream = File.OpenRead(path);
                var header = new byte[4];
                var read = stream.Read(header, 0, 4);
                //require full 4 byte header read
                if (read < 4)
                {
                    return false;
                }

                var magic = System.Text.Encoding.ASCII.GetString(header);
                return magic == "ggml" || magic == "ggmf" || magic == "ggmv" || magic == "ggjt";
            }
            //treat any inspection error as invalid file
            catch
            {
                return false;
            }
        }

        //-----------------------------
        //handles dev transcriber service change
        //dev feature not available to users
        private void DevTranscriber_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || DevTranscriberSelect.SelectedIndex < 0) return;
            Settings.Transcriber = DevTranscriberSelect.SelectedIndex == 0 ? TranscriberKind.LocalWhisper : TranscriberKind.OpenAiCompatible;
            DevTranscriberDetailsPanel.Visibility = Settings.Transcriber == TranscriberKind.OpenAiCompatible ? Visibility.Visible : Visibility.Collapsed;
            Save();
        }

        //-----------------------------
        //handles dev transcriber address change
        //dev feature not available to users
        private void DevTranscriberAddress_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            Settings.TranscriberUrl = DevTranscriberAddressBox.Text.Trim();
            Save();
        }

        //-----------------------------
        //handles dev transcriber model change
        //dev feature not available to users
        private void DevTranscriberModel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            Settings.TranscriberModel = DevTranscriberModelBox.Text.Trim();
            Save();
        }

        //-----------------------------
        //handles dev transcriber api key change
        //dev feature not available to users
        private void DevTranscriberApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _flow.Ai.KeyStore.Set("transcriber", DevTranscriberApiKeyBox.Password);
        }

        //-----------------------------
        //tests dev transcriber connection
        //dev feature not available to users
        private async void DevTestTranscriber_Click(object sender, RoutedEventArgs e)
        {
            DevTestTranscriberButton.IsEnabled = false;
            DevTranscriberTestResult.Text = "Testing…";
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var err = await _flow.Ai.TestTranscriberAsync(cts.Token);
                DevTranscriberTestResult.Text = err ?? "Working";
            }
            catch (Exception ex)
            {
                DevTranscriberTestResult.Text = ex.Message;
            }
            finally
            {
                DevTestTranscriberButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //handles dev compiler service change
        //dev feature not available to users
        private void DevCompiler_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || DevCompilerSelect.SelectedIndex < 0) return;
            Settings.Compiler = DevCompilerSelect.SelectedIndex switch
            {
                0 => CompilerKind.Ollama,
                1 => CompilerKind.Dashboard,
                _ => CompilerKind.OpenAiCompatible
            };
            DevCompilerDetailsPanel.Visibility = Settings.Compiler == CompilerKind.OpenAiCompatible ? Visibility.Visible : Visibility.Collapsed;
            Save();
        }

        //-----------------------------
        //handles dev compiler address change
        //dev feature not available to users
        private void DevCompilerAddress_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            Settings.CompilerUrl = DevCompilerAddressBox.Text.Trim();
            Save();
        }

        //-----------------------------
        //handles dev compiler model change
        //dev feature not available to users
        private void DevCompilerModel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            Settings.CompilerModel = DevCompilerModelBox.Text.Trim();
            Save();
        }

        //-----------------------------
        //handles dev compiler api key change
        //dev feature not available to users
        private void DevCompilerApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _flow.Ai.KeyStore.Set("compiler", DevCompilerApiKeyBox.Password);
        }

        //-----------------------------
        //tests dev compiler connection
        //dev feature not available to users
        private async void DevTestCompiler_Click(object sender, RoutedEventArgs e)
        {
            DevTestCompilerButton.IsEnabled = false;
            DevCompilerTestResult.Text = "Testing…";
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var err = await _flow.Ai.TestCompilerAsync(cts.Token);
                DevCompilerTestResult.Text = err ?? "Working";
            }
            catch (Exception ex)
            {
                DevCompilerTestResult.Text = ex.Message;
            }
            finally
            {
                DevTestCompilerButton.IsEnabled = true;
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
