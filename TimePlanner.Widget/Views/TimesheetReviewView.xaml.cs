using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
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
        private readonly string _sendLabel;
        private DispatcherTimer? _undoTimer;
        private Action? _undoAction;

        public TimesheetReviewView(WidgetFlow flow, DateOnly day, IReadOnlyList<TimesheetSlot> slots, DayPreview preview, SentDay? sent, Action back,
            PendingDay? pending = null, bool sendNow = false)
        {
            InitializeComponent();
            _flow = flow;
            _day = day;
            _preview = preview;
            _back = back;

            var today = DateOnly.FromDateTime(flow.Scheduler.Now);
            var totalLogged = slots.Where(s => s.Kind == SlotKind.Entry).Sum(s => (s.End - s.Start).TotalMinutes);
            Subtitle.Text = $"{DayLabel(day, today)} · {Format.Duration(TimeSpan.FromMinutes(totalLogged))}";
            AutomationProperties.SetName(DayButton, $"Day shown: {Format.Date(day)}. Choose another day");

            //sets send status caption. A send that is still on its way comes first, whatever was sent before
            if (pending is { Rejected: false })
            {
                StatusText.Text = "Waiting to send · will retry";
            }
            else if (pending is { Rejected: true })
            {
                StatusText.Text = $"Not sent · {pending.Message ?? "the dashboard refused it"}";
            }
            else if (sent == null)
            {
                StatusText.Text = "Not sent";
            }
            else if (sent.ChangedAt > sent.SentAt)
            {
                StatusText.Text = "Changed since sent · send again";
            }
            else
            {
                StatusText.Text = DateOnly.FromDateTime(sent.SentAt) == today
                    ? $"Sent {Format.Clock(sent.SentAt)}"
                    : $"Sent {Format.Date(DateOnly.FromDateTime(sent.SentAt))}, {Format.Clock(sent.SentAt)}";
            }

            //an earlier day is still open to changes, and today is one click away
            PastDayHelp.Visibility = day < today ? Visibility.Visible : Visibility.Collapsed;
            TodayButton.Visibility = day < today ? Visibility.Visible : Visibility.Collapsed;

            //disables next day navigation on today
            NextDayButton.IsEnabled = day < today;

            //a day sent before goes again, which replaces it on the dashboard
            _sendLabel = sent != null ? "Send again" : "Send day";
            SendButton.Content = _sendLabel;
            SendButton.IsEnabled = preview.CanSend;

            RenderSlots(slots);
            _ = LoadAccountStatusAsync();

            //send now from a not-sent notice starts sending once the entries are in view
            if (sendNow && preview.CanSend)
            {
                Loaded += SendOnceLoaded;
            }
        }

        //-----------------------------
        //starts the send once the view has rendered
        private async void SendOnceLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= SendOnceLoaded;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await SendAsync();
        }

        //-----------------------------
        //today, yesterday or the date
        private static string DayLabel(DateOnly day, DateOnly today) =>
            (today.DayNumber - day.DayNumber) switch
            {
                0 => $"Today, {Format.Date(day)}",
                1 => $"Yesterday, {Format.Date(day)}",
                _ => Format.Date(day),
            };

        //-----------------------------
        //goes back to today
        private void Today_Click(object sender, RoutedEventArgs e) =>
            _ = _flow.ShowTimesheetAsync(DateOnly.FromDateTime(_flow.Scheduler.Now), _back);

        //-----------------------------
        //opens the list of recent days with what was logged and sent on each
        private async void DayButton_Click(object sender, RoutedEventArgs e)
        {
            var today = DateOnly.FromDateTime(_flow.Scheduler.Now);
            var days = await _flow.GetRecentDaysAsync();

            var menu = new ContextMenu { Style = (Style)FindResource("Menu.Dropdown"), PlacementTarget = DayButton };
            AutomationProperties.SetName(menu, "Choose a day");
            foreach (var choice in days)
            {
                var caption = choice.Entries == 0 ? "Nothing logged"
                    : choice.Sent == null ? $"{Format.Duration(choice.Logged)} · Not sent"
                    : choice.Sent.ChangedAt > choice.Sent.SentAt ? $"{Format.Duration(choice.Logged)} · Changed"
                    : $"{Format.Duration(choice.Logged)} · Sent";

                var header = new DockPanel();
                var right = new TextBlock
                {
                    Text = caption,
                    Style = (Style)FindResource("Text.Caption"),
                    Foreground = (Brush)FindResource(choice.Entries > 0 && choice.Sent == null && choice.Day < today ? "TextAccentBrush" : "TextTertiaryBrush"),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                DockPanel.SetDock(right, Dock.Right);
                header.Children.Add(right);
                header.Children.Add(new TextBlock
                {
                    Text = (today.DayNumber - choice.Day.DayNumber) switch { 0 => "Today", 1 => "Yesterday", _ => Format.Date(choice.Day) },
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });

                var row = new MenuItem { Style = (Style)FindResource("Menu.Row"), Header = header, IsChecked = choice.Day == _day };
                AutomationProperties.SetName(row, $"{Format.Date(choice.Day)}, {caption}");
                var picked = choice.Day;
                row.Click += (_, _) => _ = _flow.ShowTimesheetAsync(picked, _back);
                menu.Items.Add(row);
            }

            Field.SetIsOpen(DayButton, true);
            menu.Closed += (_, _) => Field.SetIsOpen(DayButton, false);
            menu.IsOpen = true;
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
        private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

        //-----------------------------
        //asks, then sends the day
        private async Task SendAsync()
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
            SendButton.Content = "Sending…";

            try
            {
                await SendCoreAsync();
            }
            finally
            {
                SendButton.IsEnabled = true;
                SendButton.Content = _sendLabel;
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

            //a day that is now waiting to be sent shows that in its status
            if (outcome.Retryable)
            {
                ReopenTimesheet();
            }
        }

        //-----------------------------
        //exports day entries as csv file
        private async void Export_Click(object sender, RoutedEventArgs e) => await _flow.ExportDayAsync(_day);
    }
}
