using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
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
        private readonly TimesheetReviewModel _model;
        private readonly TimesheetActions _actions;
        private readonly Action _back;
        private DispatcherTimer? _undoTimer;
        private Action? _undoAction;

        public TimesheetReviewView(WidgetFlow flow, DateOnly day, IReadOnlyList<TimesheetSlot> slots, DayPreview preview, SentDay? sent, Action back,
            PendingDay? pending = null, bool sendNow = false)
        {
            InitializeComponent();
            _flow = flow;
            _day = day;
            _back = back;

            _model = new TimesheetReviewModel(day, DateOnly.FromDateTime(flow.Scheduler.Now), slots, preview, sent, pending);
            _actions = new TimesheetActions(flow, day, ReopenTimesheet);
            DayText.Text = _model.DayText;
            DayButton.ToolTip = _model.Subtitle;
            TotalText.Text = _model.Total;
            ShowBadge(SendBadge, BadgeIcon, BadgeText, _model);
            StatusText.Text = _model.Status;
            StatusText.Visibility = _model.ShowsStatus ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(DayButton, $"Day shown: {Format.Date(day)}. Choose another day");
            PastDayHelp.Visibility = _model.IsPastDay ? Visibility.Visible : Visibility.Collapsed;
            TodayButton.Visibility = _model.IsPastDay ? Visibility.Visible : Visibility.Collapsed;
            NextDayButton.IsEnabled = _model.IsPastDay;
            SendButton.Content = _model.SendLabel;
            SendButton.IsEnabled = _model.CanSend;
            RenderSlots();
            _ = LoadAccountStatusAsync();

            Loaded += async (s, e) => await LoadAiAndRenderAsync();
            _flow.Ai.NoteChanged += OnNoteChanged;
            Unloaded += (s, e) => _flow.Ai.NoteChanged -= OnNoteChanged;
            //an edit that also changed other entries offers to undo all of it. The editor has already closed, so the offer waits in the flow
            if (flow.TakeUndo() is { } offer)
            {
                ShowUndo(offer.Message, async () =>
                {
                    var undone = await offer.Undo();

                    //an undo that can no longer be done safely says why
                    if (!undone.Ok)
                    {
                        MessageBox.Show(undone.Error ?? "Could not undo.", "Undo");
                    }

                    ReopenTimesheet();
                });
            }

            //send now from a not-sent notice starts sending once the entries are in view
            if (sendNow && _model.CanSend)
            {
                Loaded += SendOnceLoaded;
            }
        }

        //-----------------------------
        //the send state as a badge: warning colours and a send arrow until the day is on the dashboard, its full status as the tooltip
        internal static void ShowBadge(Border badge, Icon icon, TextBlock text, TimesheetReviewModel model)
        {
            badge.Style = (Style)badge.FindResource(model.BadgeWarns ? "Pill.Warning" : "Pill");
            icon.Kind = model.BadgeWarns ? IconKind.Send : IconKind.Check;
            text.Text = model.Badge;
            badge.ToolTip = model.Status;
            AutomationProperties.SetName(badge, model.Status);
        }

        //-----------------------------
        //opens the wide window on this day; the widget goes back to where the timesheet was opened from
        private void Maximise_Click(object sender, RoutedEventArgs e) => _ = _flow.ShowWideTimesheetAsync(_day, _back);

        //-----------------------------
        //starts the send once the view has rendered
        private async void SendOnceLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= SendOnceLoaded;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await SendAsync();
        }

        //-----------------------------
        //goes back to today
        private void Today_Click(object sender, RoutedEventArgs e) =>
            _ = _flow.ShowTimesheetAsync(DateOnly.FromDateTime(_flow.Scheduler.Now), _back);

        //-----------------------------
        //opens the list of recent days with what was logged and sent on each
        private async void DayButton_Click(object sender, RoutedEventArgs e) =>
            await _actions.ShowDayMenuAsync(DayButton, picked => _ = _flow.ShowTimesheetAsync(picked, _back));

        //-----------------------------
        //reloads notes when transcription state updates
        private void OnNoteChanged(Guid id) => _ = Dispatcher.InvokeAsync(LoadAiAndRenderAsync);

        private Action? _bannerAction;

        //-----------------------------
        //loads voice notes and ai markers for the day and renders slots
        private async Task LoadAiAndRenderAsync()
        {
            var notes = await _flow.Ai.GetNotesAsync(_day);
            var aiIds = await _flow.Ai.GetAiEntryIdsAsync(_day);
            _model.UpdateAi(notes, aiIds);

            UpdateUnbuiltBanner();
            RenderSlots();
        }

        //-----------------------------
        //updates banner offering the next step in priority order
        private void UpdateUnbuiltBanner()
        {
            var banner = _model.Banner;
            UnbuiltBanner.Visibility = banner != null ? Visibility.Visible : Visibility.Collapsed;
            _bannerAction = null;
            if (banner == null) return;

            UnbuiltBannerText.Text = banner.Text;
            BuildDayButton.Visibility = banner.ButtonLabel != null ? Visibility.Visible : Visibility.Collapsed;
            if (banner.ButtonLabel == null) return;

            BuildDayButton.Content = banner.ButtonLabel;
            BuildDayButton.ToolTip = banner.ToolTip;
            AutomationProperties.SetName(BuildDayButton, banner.ToolTip!);
            BuildDayButton.IsEnabled = true;
            _bannerAction = banner.NoteId is { } id
                ? async () => await _flow.ShowVoiceNoteAsync(id, ReopenTimesheet)
                : async () => await _flow.ShowDayBuildAsync(_day, ReopenTimesheet);
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
            var items = _model.Items;
            if (items.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "Nothing was logged on this day.",
                    Style = (Style)FindResource("Text.Help")
                };
                SlotContainer.Children.Add(empty);
                return;
            }

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
        //builds UI card for a logged entry: its times and length, what it was, and its Edit and Delete
        private Border BuildEntryCard(TimesheetSlot slot)
        {
            var border = new Border
            {
                Style = (Style)FindResource("EntryCard"),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var stack = new StackPanel();

            var topRow = new DockPanel();
            var lenText = new TextBlock
            {
                Text = Format.Duration(slot.End - slot.Start),
                Style = (Style)FindResource("Text.Body"),
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(8, 0, 0, 0)
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
            if (_model.IsAiEntry(slot))
            {
                projectText.Inlines.Add(new Run("AI · ")
                {
                    Foreground = (Brush)FindResource("TextAccentBrush"),
                    FontWeight = (FontWeight)FindResource("FontWeightSemibold")
                });
            }

            projectText.Inlines.Add(new Run($"{slot.Company} › {slot.Project}")
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

            //edit and delete, under the entry on the right
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var editBtn = EntryAction(IconKind.Edit, "Edit", "Edit entry");
            editBtn.Click += async (sender, e) =>
            {
                e.Handled = true;
                await _flow.ShowEntryEditorAsync(_day, slot, slot.Start, slot.End, ReopenTimesheet);
            };
            actions.Children.Add(editBtn);

            var deleteBtn = EntryAction(IconKind.Trash, "Delete", "Delete entry");
            deleteBtn.Margin = new Thickness(4, 0, 0, 0);
            deleteBtn.Click += async (sender, e) =>
            {
                e.Handled = true;
                await DeleteEntryAsync(slot);
            };
            actions.Children.Add(deleteBtn);
            stack.Children.Add(actions);

            border.Child = stack;

            //opens entry editor on click
            border.MouseLeftButtonUp += async (sender, e) =>
            {
                await _flow.ShowEntryEditorAsync(_day, slot, slot.Start, slot.End, ReopenTimesheet);
            };

            return border;
        }

        //-----------------------------
        //an icon button on an entry card, with its word beside the icon in large view
        private Button EntryAction(IconKind kind, string word, string tip)
        {
            var button = new Button
            {
                Style = (Style)FindResource("EntryAction"),
                ToolTip = tip,
                Content = new Icon { Kind = kind, Size = (double)FindResource("SizeIconSm") }
            };
            WidgetCard.SetWord(button, word);
            AutomationProperties.SetName(button, tip);
            return button;
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
            //nothing deleted: no undo to offer
            if (await _actions.DeleteAsync(slot) is not { } undo)
            {
                return;
            }

            ShowUndo("Entry deleted", async () => await undo());

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
            var (start, end) = TimesheetReviewModel.NewEntryRange(_day, slots);

            await _flow.ShowEntryEditorAsync(_day, null, start, end, ReopenTimesheet);
        }

        //-----------------------------
        //sends current day to dashboard
        private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

        //-----------------------------
        //asks, then sends the day (the same steps as the wide window)
        private Task SendAsync() => _actions.SendAsync(_model, SendButton);

        //-----------------------------
        //exports day entries as csv file
        private async void Export_Click(object sender, RoutedEventArgs e) => await _flow.ExportDayAsync(_day);
    }
}
