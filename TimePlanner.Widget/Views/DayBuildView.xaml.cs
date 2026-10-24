using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TimePlanner.Core.Ai;
using TimePlanner.Widget.Controls;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //compilation and review screen for automated timesheet day creation
    public partial class DayBuildView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly DateOnly _day;
        private readonly Action _back;
        private readonly DispatcherTimer _timer;

        private CancellationTokenSource? _cts;
        private DateTime _buildStartTime;
        private List<SuggestedEntry> _suggestions = new();
        private bool _hasRefusedSuggestions;

        public DayBuildView(WidgetFlow flow, DateOnly day, Action back)
        {
            InitializeComponent();
            _flow = flow;
            _day = day;
            _back = back;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;

            Loaded += async (s, e) => await StartBuildAsync();
        }

        //-----------------------------
        //updates elapsed build time display
        private void Timer_Tick(object? sender, EventArgs e)
        {
            var elapsed = DateTime.UtcNow - _buildStartTime;
            TimerText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
        }

        //-----------------------------
        //loads pending notes and kicks off ai compilation
        private async Task StartBuildAsync()
        {
            BuildingPanel.Visibility = Visibility.Visible;
            ResultsPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Collapsed;
            SetupModelButton.Visibility = Visibility.Collapsed;

            var notes = await _flow.Ai.GetNotesAsync(_day);
            var ready = notes.Where(n => n.Status == VoiceNoteStatus.Transcribed && !string.IsNullOrWhiteSpace(n.Transcript)).ToList();

            //show empty state when no notes ready
            if (ready.Count == 0)
            {
                BuildingPanel.Visibility = Visibility.Collapsed;
                EmptyPanel.Visibility = Visibility.Visible;
                return;
            }

            var noteWord = ready.Count == 1 ? "voice note" : "voice notes";
            BuildingCaption.Text = $"Reading {ready.Count} {noteWord}. On this PC that can take about a minute.";

            _buildStartTime = DateTime.UtcNow;
            TimerText.Text = "0:00";
            _timer.Start();

            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            try
            {
                var result = await _flow.Ai.BuildDayAsync(_day, _cts.Token);
                _timer.Stop();
                ShowResults(result);
            }
            catch (OperationCanceledException)
            {
                _timer.Stop();
                _back();
            }
            catch (Exception ex)
            {
                _timer.Stop();
                ShowError(ex);
            }
        }

        //-----------------------------
        //populates suggestions and flags after successful compilation
        private void ShowResults(DayBuildResult result)
        {
            BuildingPanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Visible;

            ModelText.Text = string.IsNullOrWhiteSpace(result.Model) ? string.Empty : $"Built with {result.Model}";
            _suggestions = result.Entries.ToList();

            RenderSuggestions();
            RenderFlags(result.Flags.Select(f => f.Message).ToList());
        }

        //-----------------------------
        //renders list of suggested entries with remove buttons
        private void RenderSuggestions()
        {
            SuggestionsContainer.Children.Clear();

            //disable add button when all suggestions removed
            if (_suggestions.Count == 0)
            {
                AddButton.IsEnabled = false;
                var empty = new TextBlock
                {
                    Text = "All suggestions removed.",
                    Style = (Style)FindResource("Text.Caption"),
                    Foreground = (Brush)FindResource("TextTertiaryBrush")
                };
                SuggestionsContainer.Children.Add(empty);
                return;
            }

            AddButton.IsEnabled = true;

            //generate card for each suggestion
            foreach (var suggestion in _suggestions)
            {
                var card = new Border
                {
                    Style = (Style)FindResource("Stat"),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var stack = new StackPanel();

                var topRow = new DockPanel();
                var removeBtn = new Button
                {
                    Style = (Style)FindResource("Button.Icon"),
                    ToolTip = "Remove suggestion",
                    Content = new Icon { Kind = IconKind.Close, Size = (double)FindResource("SizeIconSm") }
                };
                System.Windows.Automation.AutomationProperties.SetName(removeBtn, "Remove");
                DockPanel.SetDock(removeBtn, Dock.Right);

                var captured = suggestion;
                //removes entry from proposal list
                removeBtn.Click += (s, e) =>
                {
                    _suggestions.Remove(captured);
                    RenderSuggestions();
                };
                topRow.Children.Add(removeBtn);

                var timeAndProject = new TextBlock
                {
                    Text = $"{suggestion.Start:HH:mm} – {suggestion.End:HH:mm} · {string.Join(" › ", suggestion.ProjectPath)}",
                    Style = (Style)FindResource("Text.Body"),
                    FontWeight = (FontWeight)FindResource("FontWeightSemibold")
                };
                topRow.Children.Add(timeAndProject);

                stack.Children.Add(topRow);

                var activityText = new TextBlock
                {
                    Text = string.Join(" › ", suggestion.ActivityPath),
                    Style = (Style)FindResource("Text.Body"),
                    Margin = new Thickness(0, 2, 0, 0)
                };
                stack.Children.Add(activityText);

                //append note when present
                if (!string.IsNullOrWhiteSpace(suggestion.Note))
                {
                    var noteText = new TextBlock
                    {
                        Text = suggestion.Note,
                        Style = (Style)FindResource("Text.Caption"),
                        Foreground = (Brush)FindResource("TextSecondaryBrush"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    stack.Children.Add(noteText);
                }

                card.Child = stack;
                SuggestionsContainer.Children.Add(card);
            }
        }

        //-----------------------------
        //renders warnings and clipping flags below suggestions list
        private void RenderFlags(IReadOnlyList<string> flags)
        {
            FlagsContainer.Children.Clear();

            //render each compiler flag with alert icon
            foreach (var flag in flags)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var icon = new Icon
                {
                    Kind = IconKind.Alert,
                    Size = (double)FindResource("SizeIconSm"),
                    Foreground = (Brush)FindResource("TextAccentBrush"),
                    Margin = new Thickness(0, 2, 6, 0)
                };
                DockPanel.SetDock(icon, Dock.Left);
                row.Children.Add(icon);

                var text = new TextBlock
                {
                    Text = flag,
                    Style = (Style)FindResource("Text.Caption"),
                    TextWrapping = TextWrapping.Wrap
                };
                row.Children.Add(text);

                FlagsContainer.Children.Add(row);
            }
        }

        //-----------------------------
        //commits approved suggestions to timesheet
        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            if (_hasRefusedSuggestions)
            {
                await _flow.ShowTimesheetAsync(_day, _back);
                return;
            }

            AddButton.IsEnabled = false;
            AddButton.Content = "Adding…";
            AddErrorText.Text = string.Empty;
            AddErrorText.Visibility = Visibility.Collapsed;

            try
            {
                var (_, problems) = await _flow.Ai.AddSuggestionsAsync(_day, _suggestions);

                //when some suggestions were refused stay on view and list them
                if (problems.Count > 0)
                {
                    _hasRefusedSuggestions = true;
                    AddErrorText.Text = string.Join("\n", problems);
                    AddErrorText.Visibility = Visibility.Visible;
                    AddButton.Content = "Go to timesheet";
                    AddButton.IsEnabled = true;
                    return;
                }

                await _flow.ShowTimesheetAsync(_day, _back);
            }
            catch (Exception ex)
            {
                AddErrorText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Couldn't add suggestions to timesheet." : ex.Message;
                AddErrorText.Visibility = Visibility.Visible;
            }
            finally
            {
                if (!_hasRefusedSuggestions)
                {
                    AddButton.IsEnabled = true;
                    AddButton.Content = "Add to timesheet";
                }
            }
        }

        //-----------------------------
        //displays compilation failure and offers setup if model missing
        private void ShowError(Exception ex)
        {
            BuildingPanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;

            ErrorMessage.Text = ex.Message;

            var isMissingModel = ex.Message.Contains("model isn't installed", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("not installed", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("missing", StringComparison.OrdinalIgnoreCase);

            SetupModelButton.Visibility = isMissingModel ? Visibility.Visible : Visibility.Collapsed;
        }

        //-----------------------------
        //opens voice and ai settings to install model
        private void SetupModel_Click(object sender, RoutedEventArgs e)
        {
            _flow.ShowAiSettings(_back);
        }

        //-----------------------------
        //restarts compilation attempt
        private async void TryAgain_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                btn.IsEnabled = false;
            }

            try
            {
                await StartBuildAsync();
            }
            catch (Exception ex)
            {
                ShowError(ex);
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
        //cancels active compilation
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _back();
        }

        //-----------------------------
        //returns to previous screen
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            _back();
        }
    }
}
//------------------------------EOF-----------------------------\\
