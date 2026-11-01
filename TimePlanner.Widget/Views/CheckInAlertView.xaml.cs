using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //alert view prompting user check-in with text, voice, or snooze options
    public partial class CheckInAlertView : UserControl
    {
        private readonly WidgetFlow _flow;

        //-----------------------------
        //initializes check in alert view and configures action availability
        public CheckInAlertView(WidgetFlow flow)
        {
            InitializeComponent();
            _flow = flow;

            var timeStr = Format.Clock(flow.Scheduler.PeriodStart);
            Subtitle.Text = $"Log what you did since {timeStr}.";

            var ai = flow.Session.Preferences.Ai;
            var voiceAvailable = ai.VoiceEnabled && flow.AiSetup.IsVoiceReady;
            SpeakUpdateButton.Visibility = voiceAvailable ? Visibility.Visible : Visibility.Collapsed;

            //configure tooltip and automation properties when voice enabled
            if (voiceAvailable)
            {
                var tip = $"Record what you did since {timeStr}. Up to 3 minutes.";
                SpeakUpdateButton.ToolTip = tip;
                AutomationProperties.SetName(SpeakUpdateButton, tip);
            }

            var tipVisible = !ai.SetupCompleted && !flow.Session.Preferences.VoiceTipDismissed;
            FirstUseTipPanel.Visibility = tipVisible ? Visibility.Visible : Visibility.Collapsed;
            DismissTipButton.Visibility = FirstUseTipPanel.Visibility;

            var snoozes = flow.Snooze.Left;
            SnoozeButton.Content = $"Snooze {Format.Interval(flow.Snooze.Minutes)} ({snoozes} left)";
            SnoozeButton.Visibility = snoozes > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoSnoozes.Visibility = snoozes > 0 ? Visibility.Collapsed : Visibility.Visible;

            // Shown straight away when there was an earlier choice, so the card doesn't grow a moment later
            if (flow.Session.ProjectId != null && flow.Session.Activity.Count > 0)
            {
                SameTaskText.Text = flow.Session.Activity[^1];
                SameTaskButton.Visibility = Visibility.Visible;
            }
            _ = ShowSameTaskAsync(timeStr);
        }

        //-----------------------------
        //names the last entry's project and activity on the button, or hides it when they are no longer offered
        private async Task ShowSameTaskAsync(string since)
        {
            var same = await _flow.GetSameTaskAsync();
            if (same == null)
            {
                SameTaskButton.Visibility = Visibility.Collapsed;
                return;
            }

            SameTaskText.Text = same.Short;
            SameTaskButton.ToolTip = same.Full;
            AutomationProperties.SetName(SameTaskButton, $"Still on the same task. Log since {since} to {same.Full}");
            SameTaskButton.Visibility = Visibility.Visible;
        }

        //-----------------------------
        //logs the period to the last entry's project and activity
        private async void SameTask_Click(object sender, RoutedEventArgs e)
        {
            SameTaskButton.IsEnabled = false;
            try
            {
                await _flow.LogSameTaskAsync();
            }
            finally
            {
                SameTaskButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //handles manual log time click
        private async void LogTime_Click(object sender, RoutedEventArgs e)
        {
            await _flow.LogTimeAsync(LogEntryMode.CheckIn);
        }

        //-----------------------------
        //handles voice update button click
        private void SpeakUpdate_Click(object sender, RoutedEventArgs e)
        {
            _flow.StartVoiceNote();
        }

        //-----------------------------
        //handles snooze button click
        private async void Snooze_Click(object sender, RoutedEventArgs e)
        {
            await _flow.SnoozeCheckInAsync();
        }

        //-----------------------------
        //dismisses first use tip permanently
        private void DismissTip_Click(object sender, RoutedEventArgs e)
        {
            _flow.Session.Preferences.VoiceTipDismissed = true;
            _flow.SavePreferences();
            FirstUseTipPanel.Visibility = Visibility.Collapsed;
            DismissTipButton.Visibility = Visibility.Collapsed;
        }

        //-----------------------------
        //navigates to voice and ai settings from first use tip
        private void TipLink_Click(object sender, RoutedEventArgs e)
        {
            _flow.ShowAiSettings(() => _flow.CheckIn());
        }
    }
}
//------------------------------EOF-----------------------------\\
