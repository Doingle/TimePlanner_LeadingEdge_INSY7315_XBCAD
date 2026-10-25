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
            AddedPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Collapsed;
            SetupModelButton.Visibility = Visibility.Collapsed;

            BuildingTitle.Text = "Reading your notes…";
            BuildingCaption.Text = "Looking for voice notes for this day…";

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
            BuildingTitle.Text = "Building your day…";
            BuildingCaption.Text = $"Using {ready.Count} {noteWord}. On this PC that can take about a minute.";

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
            //handle user cancellation
            catch (OperationCanceledException)
            {
                _timer.Stop();
                _back();
            }
            //catch and report build errors
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
        //renders list of suggested entries with remove buttons and unmatched cards
        private void RenderSuggestions()
        {
            SuggestionsContainer.Children.Clear();

            var matchedCount = _suggestions.Count(s => s.ProjectPath != null && s.ProjectPath.Count > 0);

            //disable add button when all suggestions removed
            if (_suggestions.Count == 0)
            {
                AddButton.IsEnabled = false;
                AddButton.Content = "Add entries";
                var empty = new TextBlock
                {
                    Text = "All suggestions removed.",
                    Style = (Style)FindResource("Text.Caption"),
                    Foreground = (Brush)FindResource("TextTertiaryBrush")
                };
                SuggestionsContainer.Children.Add(empty);
                return;
            }

            AddButton.IsEnabled = matchedCount > 0;
            AddButton.Content = matchedCount == 1 ? "Add 1 entry" : $"Add {matchedCount} entries";
            System.Windows.Automation.AutomationProperties.SetName(AddButton, AddButton.Content.ToString()!);

            //generate card for each suggestion
            foreach (var suggestion in _suggestions)
            {
                var card = new Border
                {
                    Style = (Style)FindResource("Stat"),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var stack = new StackPanel();

                var topRow = new DockPanel();
                var removeBtn = new Button
                {
                    Style = (Style)FindResource("Button.Icon"),
                    ToolTip = "Don't add this",
                    Content = new Icon { Kind = IconKind.Close, Size = (double)FindResource("SizeIconSm") }
                };
                System.Windows.Automation.AutomationProperties.SetName(removeBtn, "Don't add this");
                DockPanel.SetDock(removeBtn, Dock.Right);

                var captured = suggestion;
                //removes entry from proposal list
                removeBtn.Click += (s, e) =>
                {
                    _suggestions.Remove(captured);
                    RenderSuggestions();
                };
                topRow.Children.Add(removeBtn);

                var timeText = new TextBlock
                {
                    Text = $"{suggestion.Start:HH:mm} – {suggestion.End:HH:mm}",
                    Style = (Style)FindResource("Text.Label"),
                    Margin = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                topRow.Children.Add(timeText);

                stack.Children.Add(topRow);

                var isMatched = suggestion.ProjectPath != null && suggestion.ProjectPath.Count > 0;

                //format client project activity or unmatched notice
                if (isMatched)
                {
                    var projectPath = string.Join(" › ", suggestion.ProjectPath!);
                    var fullPath = suggestion.ActivityPath != null && suggestion.ActivityPath.Count > 0
                        ? $"{projectPath} · {string.Join(" › ", suggestion.ActivityPath!)}"
                        : projectPath;

                    var pathText = new TextBlock
                    {
                        Text = fullPath,
                        Style = (Style)FindResource("Text.Body"),
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    stack.Children.Add(pathText);
                }
                //show needs a project indicator
                else
                {
                    var needsProject = new TextBlock
                    {
                        Text = "Needs a project",
                        Style = (Style)FindResource("Text.Body"),
                        Foreground = (Brush)FindResource("TextAccentBrush"),
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    stack.Children.Add(needsProject);
                }

                //append note when present
                if (!string.IsNullOrWhiteSpace(suggestion.Note))
                {
                    //for unmatched cards show note with copy button per a5
                    if (!isMatched)
                    {
                        var noteRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
                        var copyBtn = new Button
                        {
                            Style = (Style)FindResource("Button.Icon"),
                            ToolTip = "Copy note",
                            Content = new Icon { Kind = IconKind.Copy, Size = (double)FindResource("SizeIconSm") },
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(4, 0, 0, 0)
                        };
                        System.Windows.Automation.AutomationProperties.SetName(copyBtn, "Copy note");
                        DockPanel.SetDock(copyBtn, Dock.Right);

                        var capturedNote = suggestion.Note;
                        //copies note text to clipboard
                        copyBtn.Click += (s, e) =>
                        {
                            try
                            {
                                Clipboard.SetText(capturedNote);
                            }
                            //catch clipboard access errors
                            catch
                            {
                            }
                        };
                        noteRow.Children.Add(copyBtn);

                        var noteText = new TextBlock
                        {
                            Text = suggestion.Note,
                            Style = (Style)FindResource("Text.Caption"),
                            Foreground = (Brush)FindResource("TextSecondaryBrush"),
                            TextWrapping = TextWrapping.Wrap
                        };
                        noteRow.Children.Add(noteText);
                        stack.Children.Add(noteRow);
                    }
                    //show standard note on matched cards
                    else
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
                }

                //add by hand button for unmatched suggestions
                if (!isMatched)
                {
                    var addByHandBtn = new Button
                    {
                        Style = (Style)FindResource("Button.Secondary"),
                        Content = "Add by hand",
                        ToolTip = "Add this entry by hand",
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 8, 0, 0)
                    };
                    System.Windows.Automation.AutomationProperties.SetName(addByHandBtn, "Add by hand");

                    //opens entry editor for this time span
                    addByHandBtn.Click += async (s, e) =>
                    {
                        await _flow.ShowEntryEditorAsync(_day, null, captured.Start, captured.End, () => _flow.ShowDayBuildAsync(_day, _back));
                    };
                    stack.Children.Add(addByHandBtn);
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
            var matched = _suggestions.Where(s => s.ProjectPath != null && s.ProjectPath.Count > 0).ToList();

            //ensure there are matched entries to add
            if (matched.Count == 0)
            {
                return;
            }

            AddButton.IsEnabled = false;
            AddButton.Content = "Adding…";
            AddErrorText.Text = string.Empty;
            AddErrorText.Visibility = Visibility.Collapsed;

            try
            {
                var (added, problems) = await _flow.Ai.AddSuggestionsAsync(_day, matched);

                ResultsPanel.Visibility = Visibility.Collapsed;
                AddedPanel.Visibility = Visibility.Visible;

                var entryWord = added == 1 ? "entry" : "entries";
                AddedTitle.Text = problems.Count > 0 && added == 0 ? "Couldn't add entries" : $"Added {added} {entryWord}";
                AddedSummary.Text = problems.Count == 0
                    ? "All suggestions were added to your timesheet."
                    : $"{added} {entryWord} added. Some suggestions couldn't be added:";

                RefusedContainer.Children.Clear();

                //render refused entry explanations
                foreach (var prob in problems)
                {
                    var text = new TextBlock
                    {
                        Text = prob,
                        Style = (Style)FindResource("Text.Caption"),
                        Foreground = (Brush)FindResource("TextSecondaryBrush"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 2)
                    };
                    RefusedContainer.Children.Add(text);
                }
            }
            //catch and report addition errors
            catch (Exception ex)
            {
                AddErrorText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Couldn't add suggestions to timesheet." : ex.Message;
                AddErrorText.Visibility = Visibility.Visible;
                var matchedCount = _suggestions.Count(s => s.ProjectPath != null && s.ProjectPath.Count > 0);
                AddButton.IsEnabled = matchedCount > 0;
                AddButton.Content = matchedCount == 1 ? "Add 1 entry" : $"Add {matchedCount} entries";
            }
        }

        //-----------------------------
        //navigates to timesheet after adding entries
        private async void GoToTimesheet_Click(object sender, RoutedEventArgs e)
        {
            await _flow.ShowTimesheetAsync(_day, _back);
        }

        //-----------------------------
        //displays compilation failure and offers setup if model missing
        private void ShowError(Exception ex)
        {
            BuildingPanel.Visibility = Visibility.Collapsed;
            ResultsPanel.Visibility = Visibility.Collapsed;
            AddedPanel.Visibility = Visibility.Collapsed;
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
            //disable button while retrying
            if (btn != null)
            {
                btn.IsEnabled = false;
            }

            try
            {
                await StartBuildAsync();
            }
            //catch and show error
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                //re-enable button after attempt
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
