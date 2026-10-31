using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Sync;
using TimePlanner.Widget.Controls;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //what both timesheets (the page in the widget and the wide window) do to send a day or delete an entry: the questions
    //they ask, the sign-in a send may need, and what they say afterwards. Reopen shows the day again once something changed
    internal sealed class TimesheetActions(WidgetFlow flow, DateOnly day, Action reopen)
    {
        //-----------------------------
        //opens the list of recent days under the day's button, with what was logged and sent on each; picking one shows it
        public async Task ShowDayMenuAsync(Button anchor, Action<DateOnly> pick)
        {
            var today = DateOnly.FromDateTime(flow.Scheduler.Now);
            var days = await flow.GetRecentDaysAsync();

            var menu = new ContextMenu { Style = (Style)anchor.FindResource("Menu.Dropdown"), PlacementTarget = anchor };
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
                    Style = (Style)anchor.FindResource("Text.Caption"),
                    Foreground = (Brush)anchor.FindResource(choice.Entries > 0 && choice.Sent == null && choice.Day < today ? "TextAccentBrush" : "TextTertiaryBrush"),
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

                var row = new MenuItem { Style = (Style)anchor.FindResource("Menu.Row"), Header = header, IsChecked = choice.Day == day };
                AutomationProperties.SetName(row, $"{Format.Date(choice.Day)}, {caption}");
                var picked = choice.Day;
                row.Click += (_, _) => pick(picked);
                menu.Items.Add(row);
            }

            Field.SetIsOpen(anchor, true);
            menu.Closed += (_, _) => Field.SetIsOpen(anchor, false);
            menu.IsOpen = true;
        }

        //-----------------------------
        //asks, then sends the day
        public async Task SendAsync(TimesheetReviewModel model, Button sendButton)
        {
            var confirm = MessageBox.Show(model.SendConfirmation, "Send day", MessageBoxButton.YesNo);

            //user cancelled send
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            sendButton.IsEnabled = false;
            sendButton.Content = "Sending…";

            try
            {
                await SendCoreAsync(model);
            }
            finally
            {
                sendButton.IsEnabled = true;
                sendButton.Content = model.SendLabel;
            }
        }

        //-----------------------------
        //tells the user the day reached the dashboard
        private static void ConfirmSent(TimesheetReviewModel model)
        {
            MessageBox.Show(model.SentConfirmation, "Day sent");
        }

        //-----------------------------
        //handles day send outcomes
        private async Task SendCoreAsync(TimesheetReviewModel model)
        {
            var outcome = await flow.SendDayAsync(day);

            //reloads timesheet when sent
            if (outcome.Status == SendStatus.Sent)
            {
                await flow.Ai.ClearAiEntryIdsAsync(day);
                ConfirmSent(model);
                reopen();
                return;
            }

            //prompts user to sign in
            if (outcome.Status == SendStatus.NeedsSignIn)
            {
                flow.ShowSignIn(async () =>
                {
                    var retry = await flow.SendDayAsync(day);

                    //confirms sent or shows failure on retry
                    if (retry.Status == SendStatus.Sent)
                    {
                        await flow.Ai.ClearAiEntryIdsAsync(day);
                        ConfirmSent(model);
                    }
                    else
                    {
                        MessageBox.Show(retry.Message ?? "The day was not sent.", "Send day");
                    }

                    reopen();
                }, reopen);
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
                reopen();
            }
        }

        //-----------------------------
        //asks, then deletes an entry. Gives back what puts it back again for the undo bar, or null when nothing was deleted
        public async Task<Func<Task>?> DeleteAsync(TimesheetSlot slot)
        {
            var confirm = MessageBox.Show("Delete this entry?", "Delete entry", MessageBoxButton.YesNo);

            //user cancelled deletion
            if (confirm != MessageBoxResult.Yes)
            {
                return null;
            }

            var result = await flow.DeleteEntryAsync(slot.EntryId!.Value);

            //shows error if deletion fails
            if (!result.Ok)
            {
                MessageBox.Show(result.Error ?? "Could not delete entry.", "Delete");
                return null;
            }

            return async () =>
            {
                //restores deleted entry
                if (result.Previous != null)
                {
                    await flow.AddEntryAsync(result.Previous);
                    reopen();
                }
            };
        }
    }
}
//------------------------------EOF-----------------------------\\
