using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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
        private DispatcherTimer? _undoTimer;
        private Action? _undoAction;

        public TimesheetReviewView(WidgetFlow flow, DateOnly day, IReadOnlyList<TimesheetSlot> slots, DayPreview preview, SentDay? sent, Action back)
        {
            InitializeComponent();
            _flow = flow;
            _day = day;
            _preview = preview;
            _back = back;

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

            RenderSlots(slots);
        }

        //-----------------------------
        //renders timesheet slot cards
        private void RenderSlots(IReadOnlyList<TimesheetSlot> slots)
        {
            SlotContainer.Children.Clear();

            //shows message when day has no slots
            if (slots.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "Nothing was logged on this day.",
                    Style = (Style)FindResource("Text.Help")
                };
                SlotContainer.Children.Add(empty);
                return;
            }

            //builds card for each slot
            foreach (var slot in slots)
            {
                if (slot.Kind == SlotKind.Entry)
                {
                    SlotContainer.Children.Add(BuildEntryCard(slot));
                }
                else if (slot.Kind == SlotKind.Gap)
                {
                    SlotContainer.Children.Add(BuildGapCard(slot));
                }
                else if (slot.Kind == SlotKind.Break)
                {
                    SlotContainer.Children.Add(BuildBreakCard(slot));
                }
            }
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
                Text = $"{slot.Project} · {slot.Company}",
                Style = (Style)FindResource("Text.Caption"),
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            };
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
            var confirm = MessageBox.Show(
                $"Send {_preview.Rows.Count} entries ({_preview.Hours:0.##} h) for {Format.Day(_day.ToDateTime(TimeOnly.MinValue))} to the dashboard?",
                "Send day",
                MessageBoxButton.YesNo);

            //user cancelled send
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            SendButton.IsEnabled = false;

            try
            {
                await SendCoreAsync();
            }
            finally
            {
                SendButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //handles day send outcomes
        private async Task SendCoreAsync()
        {
            var outcome = await _flow.SendDayAsync(_day);

            //reloads timesheet when sent
            if (outcome.Status == SendStatus.Sent)
            {
                ReopenTimesheet();
                return;
            }

            //prompts user to sign in
            if (outcome.Status == SendStatus.NeedsSignIn)
            {
                _flow.ShowSignIn(async () =>
                {
                    await _flow.SendDayAsync(_day);
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
