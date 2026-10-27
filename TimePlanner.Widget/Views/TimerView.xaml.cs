using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TimePlanner.Widget.Controls;
using TimePlanner.Widget.Models;
using System.Windows.Input;
using System.Windows.Threading;

namespace TimePlanner.Widget.Views
{
    /// <summary>
    /// Interaction logic for TimerView.xaml
    /// </summary>
    public partial class TimerView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromSeconds(5) };

        public TimerView(WidgetFlow flow)
        {
            InitializeComponent();
            _flow = flow;
            _leaveTimer.Tick += (_, _) =>
            {
                _leaveTimer.Stop();
                if (!IsLoaded || Card.IsMouseOver) return;
                if (Mouse.LeftButton == MouseButtonState.Pressed || IsMouseCaptureWithin)
                    _leaveTimer.Start();
                else
                    _flow.ShowIdle();
            };

            ShowState();
            Loaded += (_, _) =>
            {
                flow.Scheduler.Ticked += Scheduler_Ticked;
                if (!Card.IsMouseOver) _leaveTimer.Start();
            };
            Unloaded += (_, _) =>
            {
                _leaveTimer.Stop();
                flow.Scheduler.Ticked -= Scheduler_Ticked;
            };
        }

        private void Scheduler_Ticked(object? sender, EventArgs e) => ShowState();

        private void ShowState()
        {
            var scheduler = _flow.Scheduler;
            LogButton.Content = $"Log time since {Format.Clock(_flow.GetLogPeriod().Start)}";
            var paused = scheduler.PausedAt;
            PauseIcon.Kind = paused != null ? IconKind.Play : IconKind.Pause;
            var action = paused != null ? "Resume the timer" : "Pause the timer";
            PauseButton.ToolTip = action;
            AutomationProperties.SetName(PauseButton, action);
            LargeView.SetWord(PauseButton, paused != null ? "Resume" : "Pause");

            TimerText.Foreground = (Brush)FindResource(paused != null ? "TextTertiaryBrush" : "TextPrimaryBrush");

            // A day off has no check-ins to count down to, so it counts up the time not logged yet
            if (scheduler.IsDayOff)
            {
                TimerText.Text = Format.Countdown(scheduler.Unlogged);
                AutomationProperties.SetName(TimerText, $"{Format.Spoken(scheduler.Unlogged)} not logged yet");
                StatusText.Text = paused is { } breakAt ? $"Paused at {Format.Clock(breakAt)}" : "not logged yet";
                return;
            }

            var left = scheduler.TimeLeft;
            TimerText.Text = left is { } time ? Format.Countdown(time) : "--:--";
            AutomationProperties.SetName(TimerText, left is { } spoken ? Format.Spoken(spoken) : "No more check-ins today");

            StatusText.Text = paused is { } pausedAt ? $"Paused at {Format.Clock(pausedAt)}"
                : left == null ? "No more check-ins today"
                : scheduler.IsSnoozed && _flow.Snooze.SnoozedUntil is { } until ? $"Check-in snoozed until {Format.Clock(until)}"
                : "until next check-in";

            var ai = _flow.Session.Preferences.Ai;
            var voiceAvailable = ai.VoiceEnabled && _flow.AiSetup.IsVoiceReady;
            VoiceLogButton.Visibility = voiceAvailable ? Visibility.Visible : Visibility.Collapsed;
            //configure voice button tooltip when enabled
            if (voiceAvailable)
            {
                var tip = $"Speak your update since {Format.Clock(_flow.GetLogPeriod().Start)}";
                VoiceLogButton.ToolTip = tip;
                AutomationProperties.SetName(VoiceLogButton, tip);
            }
        }

        private async void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_flow.Scheduler.IsPaused)
                await _flow.Scheduler.ResumeAsync();
            else
                await _flow.Scheduler.PauseAsync();
            ShowState();
        }

        private void Settings_Click(object sender, RoutedEventArgs e) => _flow.ShowSettings();

        private void Exit_Click(object sender, RoutedEventArgs e) => _flow.AskExit();

        private void Card_MouseEnter(object sender, MouseEventArgs e) => _leaveTimer.Stop();
        private void Card_MouseLeave(object sender, MouseEventArgs e)
        {
            _leaveTimer.Stop();
            _leaveTimer.Start();
        }

        private async void LogNow_Click(object sender, RoutedEventArgs e) => await _flow.LogTimeAsync(LogEntryMode.LogNow);

        //-----------------------------
        //handles voice update button click from timer
        private void VoiceLog_Click(object sender, RoutedEventArgs e)
        {
            _flow.StartVoiceNote();
        }

        private void EndDay_Click(object sender, RoutedEventArgs e) => _flow.AskEndDay();
    }
}
//------------------------------EOF-----------------------------\\
