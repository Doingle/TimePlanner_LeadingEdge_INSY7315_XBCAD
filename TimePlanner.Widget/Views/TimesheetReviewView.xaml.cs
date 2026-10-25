using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using TimePlanner.Core.Ai;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Sync;
using TimePlanner.Widget.Controls;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //displays and manages a day's timesheet slots
    public partial class TimesheetReviewView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly DateOnly _day;
        private readonly DayPreview _preview;
        private readonly Action _back;
        private readonly IReadOnlyList<TimesheetSlot> _slots;
        private HashSet<int> _aiEntryIds = new();
        private List<VoiceNote> _unbuiltNotes = new();
        private DispatcherTimer? _undoTimer;
        private Action? _undoAction;

        public TimesheetReviewView(WidgetFlow flow, DateOnly day, IReadOnlyList<TimesheetSlot> slots, DayPreview preview, SentDay? sent, Action back)
        {
            InitializeComponent();
            _flow = flow;
            _day = day;
            _preview = preview;
            _back = back;
            _slots = slots;

            var dayDate = day.ToDateTime(TimeOnly.MinValue);
            var totalLogged = slots.Where(s => s.Kind == SlotKind.Entry).Sum(s => (s.End - s.Start).TotalMinutes);
            Subtitle.Text = $"{Format.Day(dayDate)} · {Format.Duration(TimeSpan.FromMinutes(totalLogged))}";

            //sets send status caption
            if (sent == null)
            {
                StatusText.Text = "Not sent";
            }
            else if (sent.ChangedAt > sent.SentAt)
            {
                StatusText.Text = "Changed since sent · send again";
            }
            else
            {
                StatusText.Text = $"Sent {Format.Clock(sent.SentAt)}";
            }

            //disables next day navigation on today
            var today = DateOnly.FromDateTime(DateTime.Now);
            NextDayButton.IsEnabled = day < today;

            SendButton.IsEnabled = preview.CanSend;

            RenderSlots();
            _ = LoadAccountStatusAsync();

            Loaded += async (s, e) => await LoadAiAndRenderAsync();
            _flow.Ai.NoteChanged += OnNoteChanged;
            Unloaded += (s, e) => _flow.Ai.NoteChanged -= OnNoteChanged;
        }

        //-----------------------------
        //reloads notes when transcription state updates
        private void OnNoteChanged(Guid id) => _ = Dispatcher.InvokeAsync(LoadAiAndRenderAsync);

        private IReadOnlyList<VoiceNote> _allNotes = Array.Empty<VoiceNote>();
        private Action? _bannerAction;

        //-----------------------------
        //loads voice notes and ai markers for the day and renders slots
        private async Task LoadAiAndRenderAsync()
        {
            var notes = await _flow.Ai.GetNotesAsync(_day);
            _allNotes = notes;
            _unbuiltNotes = notes.Where(n => n.Status != VoiceNoteStatus.Built).ToList();

            var aiIds = await _flow.Ai.GetAiEntryIdsAsync(_day);
            _aiEntryIds = aiIds.ToHashSet();

            UpdateUnbuiltBanner();
            RenderSlots();
        }

        //-----------------------------
        //updates banner offering the next step in priority order
        private void UpdateUnbuiltBanner()
        {
            //priority 1: failed note
            var failedNote = _allNotes.FirstOrDefault(n => n.Status == VoiceNoteStatus.Failed);
            if (failedNote != null)
            {
                UnbuiltBanner.Visibility = Visibility.Visible;
                var count = _allNotes.Count(n => n.Status == VoiceNoteStatus.Failed);
                UnbuiltBannerText.Text = count == 1
                    ? "1 voice note couldn't be turned into text"
                    : $"{count} voice notes couldn't be turned into text";
                BuildDayButton.Visibility = Visibility.Visible;
                BuildDayButton.Content = "View";
                BuildDayButton.ToolTip = "Open this voice note";
                System.Windows.Automation.AutomationProperties.SetName(BuildDayButton, "Open this voice note");
                BuildDayButton.IsEnabled = true;
                _bannerAction = async () => await _flow.ShowVoiceNoteAsync(failedNote.Id, ReopenTimesheet);
                return;
            }

            //priority 2: notes are transcribing
            var transcribingCount = _allNotes.Count(n => n.Status == VoiceNoteStatus.Recorded);
            if (transcribingCount > 0)
            {
                UnbuiltBanner.Visibility = Visibility.Visible;
                UnbuiltBannerText.Text = transcribingCount == 1
                    ? "Turning 1 voice note into text…"
                    : $"Turning {transcribingCount} voice notes into text…";
                BuildDayButton.Visibility = Visibility.Collapsed;
                _bannerAction = null;
                return;
            }

            //priority 3: notes haven't been built
            var unbuiltCount = _allNotes.Count(n => n.Status == VoiceNoteStatus.Transcribed);
            if (unbuiltCount > 0)
            {
                UnbuiltBanner.Visibility = Visibility.Visible;
                UnbuiltBannerText.Text = unbuiltCount == 1
                    ? "1 voice note isn't in your timesheet yet"
                    : $"{unbuiltCount} voice notes aren't in your timesheet yet";
                BuildDayButton.Visibility = Visibility.Visible;
                BuildDayButton.Content = "Build my day";
                var tip = "Turn today's voice notes into timesheet entries. You check them before anything is added.";
                BuildDayButton.ToolTip = tip;
                System.Windows.Automation.AutomationProperties.SetName(BuildDayButton, tip);
                BuildDayButton.IsEnabled = true;
                _bannerAction = async () => await _flow.ShowDayBuildAsync(_day, ReopenTimesheet);
                return;
            }

            //priority 4: there are ai entries
            if (_aiEntryIds.Count > 0)
            {
                UnbuiltBanner.Visibility = Visibility.Visible;
                UnbuiltBannerText.Text = "Check the AI entries before sending";
                BuildDayButton.Visibility = Visibility.Collapsed;
                _bannerAction = null;
                return;
            }

            //otherwise hide banner
            UnbuiltBanner.Visibility = Visibility.Collapsed;
            _bannerAction = null;
        }

        //-----------------------------
        //executes action associated with timesheet banner
        private void BuildDay_Click(object sender, RoutedEventArgs e)
        {
            _bannerAction?.Invoke();
        }

        //-----------------------------
        //loads signed in account email and shows account row
        private async Task LoadAccountStatusAsync()
        {
            var email = await _flow.GetDashboardEmailAsync();

            //shows signed in email when present
            if (!string.IsNullOrEmpty(email))
            {
                AccountText.Text = $"Signed in as {email}";
                AccountRow.Visibility = Visibility.Visible;
            }
            else
            {
                AccountRow.Visibility = Visibility.Collapsed;
            }
        }

        //-----------------------------
        //signs out of dashboard and hides account row
        private async void SignOut_Click(object sender, RoutedEventArgs e)
        {
            await _flow.SignOutOfDashboardAsync();
            AccountRow.Visibility = Visibility.Collapsed;
        }

        //-----------------------------
        //renders timesheet slot cards and voice note cards in chronological order
        private void RenderSlots()
        {
            SlotContainer.Children.Clear();

            //shows message when day has no slots and no voice notes
            if (_slots.Count == 0 && _unbuiltNotes.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "Nothing was logged on this day.",
                    Style = (Style)FindResource("Text.Help")
                };
                SlotContainer.Children.Add(empty);
                return;
            }

            var items = new List<(DateTime Start, TimesheetSlot? Slot, VoiceNote? Note)>();
            foreach (var slot in _slots)
            {
                items.Add((slot.Start, slot, null));
            }
            foreach (var note in _unbuiltNotes)
            {
                items.Add((note.WindowStart, null, note));
            }
            items.Sort((a, b) => a.Start.CompareTo(b.Start));

            //builds card for each slot or unbuilt voice note
            foreach (var item in items)
            {
                if (item.Note != null)
                {
                    SlotContainer.Children.Add(BuildVoiceNoteCard(item.Note));
                }
                else if (item.Slot != null)
                {
                    if (item.Slot.Kind == SlotKind.Entry)
                    {
                        SlotContainer.Children.Add(BuildEntryCard(item.Slot));
                    }
                    else if (item.Slot.Kind == SlotKind.Gap)
                    {
                        SlotContainer.Children.Add(BuildGapCard(item.Slot));
                    }
                    else if (item.Slot.Kind == SlotKind.Break)
                    {
                        SlotContainer.Children.Add(BuildBreakCard(item.Slot));
                    }
                }
            }
        }

        //-----------------------------
        //builds UI card for an unbuilt voice note
        private Border BuildVoiceNoteCard(VoiceNote note)
        {
            var border = new Border
            {
                Style = (Style)FindResource("Stat"),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Open this voice note"
            };
            System.Windows.Automation.AutomationProperties.SetName(border, "Open this voice note");

            var dock = new DockPanel();

            var icon = new Icon
            {
                Kind = IconKind.Mic,
                Size = (double)FindResource("SizeIconSm"),
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(icon, Dock.Left);
            dock.Children.Add(icon);

            var statusText = note.Status switch
            {
                VoiceNoteStatus.Transcribed => "Transcribed",
                VoiceNoteStatus.Recorded => "Transcribing…",
                VoiceNoteStatus.Failed => "Couldn't transcribe · Retry",
                _ => note.Status.ToString()
            };

            var text = new TextBlock
            {
                Text = $"Voice note · {Format.Range(note.WindowStart, note.WindowEnd)} · {statusText}",
                Style = (Style)FindResource("Text.Body"),
                VerticalAlignment = VerticalAlignment.Center
            };

            //highlight failed status in accent color
            if (note.Status == VoiceNoteStatus.Failed)
            {
                text.Foreground = (Brush)FindResource("TextAccentBrush");
            }

            dock.Children.Add(text);
            border.Child = dock;

            //open voice note details on card click
            border.MouseLeftButtonUp += async (sender, e) =>
            {
                await _flow.ShowVoiceNoteAsync(note.Id, ReopenTimesheet);
            };

            return border;
        }

        //-----------------------------
        //builds UI card for a logged entry
        private Border BuildEntryCard(TimesheetSlot slot)
        {
            var border = new Border
            {
                Style = (Style)FindResource("Stat"),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var stack = new StackPanel();

            var topRow = new DockPanel();
            var deleteBtn = new Button
            {
                Style = (Style)FindResource("Button.Icon"),
                ToolTip = "Delete entry",
                Content = new Icon { Kind = IconKind.Close, Size = (double)FindResource("SizeIconSm") }
            };
            DockPanel.SetDock(deleteBtn, Dock.Right);

            //deletes entry when close button is clicked
            deleteBtn.Click += async (sender, e) =>
            {
                e.Handled = true;
                await DeleteEntryAsync(slot);
            };

            topRow.Children.Add(deleteBtn);

            var lenText = new TextBlock
            {
                Text = Format.Duration(slot.End - slot.Start),
                Style = (Style)FindResource("Text.Body"),
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(8, 0, 8, 0)
            };
            DockPanel.SetDock(lenText, Dock.Right);
            topRow.Children.Add(lenText);

            var timeText = new TextBlock
            {
                Text = Format.Range(slot.Start, slot.End),
                Style = (Style)FindResource("Text.Body"),
                FontWeight = (FontWeight)FindResource("FontWeightSemibold")
            };
            topRow.Children.Add(timeText);

            stack.Children.Add(topRow);

            var activityText = new TextBlock
            {
                Text = string.Join(" › ", slot.ActivityPath),
                Style = (Style)FindResource("Text.Body")
            };
            stack.Children.Add(activityText);

            var projectText = new TextBlock
            {
                Style = (Style)FindResource("Text.Caption")
            };

            //add ai tag when entry was created by day compilation
            if (slot.EntryId.HasValue && _aiEntryIds.Contains(slot.EntryId.Value))
            {
                projectText.Inlines.Add(new Run("AI · ")
                {
                    Foreground = (Brush)FindResource("TextAccentBrush"),
                    FontWeight = (FontWeight)FindResource("FontWeightSemibold")
                });
            }

            projectText.Inlines.Add(new Run($"{slot.Project} · {slot.Company}")
            {
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            });

            stack.Children.Add(projectText);

            //adds note line when present
            if (!string.IsNullOrWhiteSpace(slot.Note))
            {
                var noteText = new TextBlock
                {
                    Text = slot.Note,
                    Style = (Style)FindResource("Text.Caption"),
                    Foreground = (Brush)FindResource("TextTertiaryBrush"),
                    Margin = new Thickness(0, 4, 0, 0)
                };
                stack.Children.Add(noteText);
            }

            border.Child = stack;

            //opens entry editor on click
            border.MouseLeftButtonUp += async (sender, e) =>
            {
                await _flow.ShowEntryEditorAsync(_day, slot, slot.Start, slot.End, ReopenTimesheet);
            };

            return border;
        }

        //-----------------------------
        //builds UI card for an untracked gap
        private Border BuildGapCard(TimesheetSlot slot)
        {
            var border = new Border
            {
                Style = (Style)FindResource("Stat"),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var dock = new DockPanel();
            var fillBtn = new Button
            {
                Style = (Style)FindResource("Button.Text"),
                Content = "Fill"
            };
            DockPanel.SetDock(fillBtn, Dock.Right);

            //opens editor to fill the gap
            fillBtn.Click += async (sender, e) =>
            {
                await _flow.ShowEntryEditorAsync(_day, null, slot.Start, slot.End, ReopenTimesheet);
            };

            dock.Children.Add(fillBtn);

            var text = new TextBlock
            {
                Text = $"Untracked {Format.Range(slot.Start, slot.End)} ({Format.Duration(slot.End - slot.Start)})",
                Style = (Style)FindResource("Text.Body"),
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            dock.Children.Add(text);

            border.Child = dock;
            return border;
        }

        //-----------------------------
        //builds UI card for a break
        private Border BuildBreakCard(TimesheetSlot slot)
        {
            var border = new Border
            {
                Style = (Style)FindResource("Stat"),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var text = new TextBlock
            {
                Text = $"Break {Format.Range(slot.Start, slot.End)} ({Format.Duration(slot.End - slot.Start)})",
                Style = (Style)FindResource("Text.Body"),
                Foreground = (Brush)FindResource("TextTertiaryBrush")
            };

            border.Child = text;
            return border;
        }

        //-----------------------------
        //deletes an entry with confirmation and shows undo bar
        private async Task DeleteEntryAsync(TimesheetSlot slot)
        {
            var confirm = MessageBox.Show("Delete this entry?", "Delete entry", MessageBoxButton.YesNo);

            //user cancelled deletion
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var result = await _flow.DeleteEntryAsync(slot.EntryId!.Value);

            //shows error if deletion fails
            if (!result.Ok)
            {
                MessageBox.Show(result.Error ?? "Could not delete entry.", "Delete");
                return;
            }

            ShowUndo("Entry deleted", async () =>
            {
                //restores deleted entry
                if (result.Previous != null)
                {
                    await _flow.AddEntryAsync(result.Previous);
                    ReopenTimesheet();
                }
            });

            ReopenTimesheet();
        }

        //-----------------------------
        //shows temporary undo bar
        private void ShowUndo(string message, Action undoAction)
        {
            UndoText.Text = message;
            _undoAction = undoAction;
            UndoBar.Visibility = Visibility.Visible;

            _undoTimer?.Stop();
            _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _undoTimer.Tick += (sender, e) =>
            {
                _undoTimer?.Stop();
                UndoBar.Visibility = Visibility.Collapsed;
                _undoAction = null;
            };
            _undoTimer.Start();
        }

        //-----------------------------
        //executes pending undo action
        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            _undoTimer?.Stop();
            UndoBar.Visibility = Visibility.Collapsed;
            var action = _undoAction;
            _undoAction = null;
            action?.Invoke();
        }

        //-----------------------------
        //reopens current timesheet day
        private void ReopenTimesheet() => _ = _flow.ShowTimesheetAsync(_day, _back);

        //-----------------------------
        //navigates back
        private void Back_Click(object sender, RoutedEventArgs e) => _back();

        //-----------------------------
        //navigates to previous day
        private void PrevDay_Click(object sender, RoutedEventArgs e) => _ = _flow.ShowTimesheetAsync(_day.AddDays(-1), _back);

        //-----------------------------
        //navigates to next day
        private void NextDay_Click(object sender, RoutedEventArgs e) => _ = _flow.ShowTimesheetAsync(_day.AddDays(1), _back);

        //-----------------------------
        //opens add entry view
        private async void AddTime_Click(object sender, RoutedEventArgs e)
        {
            var slots = await _flow.GetTimesheetAsync(_flow.Session.User.UserId, _day);
            var lastEntry = slots.LastOrDefault(s => s.Kind == SlotKind.Entry);
            var start = lastEntry?.End ?? _day.ToDateTime(new TimeOnly(9, 0));
            var end = start.AddMinutes(30);

            await _flow.ShowEntryEditorAsync(_day, null, start, end, ReopenTimesheet);
        }

        //-----------------------------
        //sends current day to dashboard
        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            var aiCount = _slots.Count(s => s.EntryId.HasValue && _aiEntryIds.Contains(s.EntryId.Value));
            var unbuiltCount = _unbuiltNotes.Count;

            var sb = new System.Text.StringBuilder();

            //warn about ai entries
            if (aiCount > 0)
            {
                var entryWord = aiCount == 1 ? "AI-built entry" : "AI-built entries";
                sb.AppendLine($"This day includes {aiCount} {entryWord}. AI can be inaccurate, so check them before sending.");
            }

            sb.Append($"Send {_preview.Rows.Count} entries ({_preview.Hours:0.##} h) for {Format.Day(_day.ToDateTime(TimeOnly.MinValue))} to the dashboard?");

            //warn about unbuilt notes
            if (unbuiltCount > 0)
            {
                var noteWord = unbuiltCount == 1 ? "voice note hasn't" : "voice notes haven't";
                var themWord = unbuiltCount == 1 ? "it" : "them";
                sb.AppendLine();
                sb.Append($"{unbuiltCount} {noteWord} been added yet. Send without {themWord}?");
            }

            var confirm = MessageBox.Show(sb.ToString(), "Send day", MessageBoxButton.YesNo);

            //user cancelled send
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            SendButton.IsEnabled = false;
            SendButton.Content = "Sending…";

            try
            {
                await SendCoreAsync();
            }
            finally
            {
                SendButton.IsEnabled = true;
                SendButton.Content = "Send day";
            }
        }

        //-----------------------------
        //tells the user the day reached the dashboard
        private void ConfirmSent()
        {
            MessageBox.Show($"Sent {_preview.Rows.Count} entries ({_preview.Hours:0.##} h) for {Format.Day(_day.ToDateTime(TimeOnly.MinValue))} to the dashboard.", "Day sent");
        }

        //-----------------------------
        //handles day send outcomes
        private async Task SendCoreAsync()
        {
            var outcome = await _flow.SendDayAsync(_day);

            //reloads timesheet when sent
            if (outcome.Status == SendStatus.Sent)
            {
                await _flow.Ai.ClearAiEntryIdsAsync(_day);
                ConfirmSent();
                ReopenTimesheet();
                return;
            }

            //prompts user to sign in
            if (outcome.Status == SendStatus.NeedsSignIn)
            {
                _flow.ShowSignIn(async () =>
                {
                    var retry = await _flow.SendDayAsync(_day);

                    //confirms sent or shows failure on retry
                    if (retry.Status == SendStatus.Sent)
                    {
                        await _flow.Ai.ClearAiEntryIdsAsync(_day);
                        ConfirmSent();
                    }
                    else
                    {
                        MessageBox.Show(retry.Message ?? "The day was not sent.", "Send day");
                    }

                    ReopenTimesheet();
                }, ReopenTimesheet);
                return;
            }

            //shows rejected errors
            if (outcome.Status == SendStatus.Rejected)
            {
                var msg = $"{outcome.Message}\n" + string.Join("\n", outcome.Errors);
                MessageBox.Show(msg, "Send day");
                return;
            }

            MessageBox.Show(outcome.Message, "Send day");
        }

        //-----------------------------
        //exports day entries as csv file
        private async void Export_Click(object sender, RoutedEventArgs e) => await _flow.ExportDayAsync(_day);
    }
}
