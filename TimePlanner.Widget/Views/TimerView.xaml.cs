using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TimePlanner.Widget.Controls;

namespace TimePlanner.Widget.Views
{
    /// <summary>
    /// Interaction logic for TimerView.xaml
    /// </summary>
    public partial class TimerView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly DispatcherTimer _leaveTimer;

        public TimerView(WidgetFlow flow)
        {
            InitializeComponent();
            _flow = flow;

            _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _leaveTimer.Tick += (_, _) =>
            {
                _leaveTimer.Stop();
                if (!Card.IsMouseOver && IsLoaded)
                    _flow.ShowIdle();
            };

            ShowState();
            Loaded += (_, _) => flow.Scheduler.Ticked += Scheduler_Ticked;
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
            var paused = scheduler.PausedAt;
            PauseIcon.Kind = paused != null ? IconKind.Play : IconKind.Pause;
            var action = paused != null ? "Resume the timer" : "Pause the timer";
            PauseButton.ToolTip = action;
            AutomationProperties.SetName(PauseButton, action);

            var left = scheduler.TimeLeft;
            TimerText.Text = left is { } time ? Format.Countdown(time) : "--:--";
            AutomationProperties.SetName(TimerText, left is { } spoken ? Format.Spoken(spoken) : "No more check-ins today");

            TimerText.Foreground = (Brush)FindResource(paused != null ? "TextTertiaryBrush" : "TextPrimaryBrush");
            StatusText.Text = paused is { } pausedAt ? $"Paused at {Format.Clock(pausedAt)}"
                : left == null ? "No more check-ins today"
                : _flow.Snooze.SnoozedUntil is { } until && until >= scheduler.CheckInAt ? $"Check-in snoozed until {Format.Clock(until)}"
                : "Until your next check-in";
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_flow.Scheduler.IsPaused)
                _flow.Scheduler.Resume();
            else
                _flow.Scheduler.Pause();
            ShowState();
        }

        private void Settings_Click(object sender, RoutedEventArgs e) => _flow.ShowSettings();

        private async void LogNow_Click(object sender, RoutedEventArgs e) => await _flow.LogTimeAsync(LogEntryMode.LogNow);

        private void EndDay_Click(object sender, RoutedEventArgs e) => _flow.AskEndDay();

        private void Card_MouseEnter(object sender, MouseEventArgs e) => _leaveTimer.Stop();

        private void Card_MouseLeave(object sender, MouseEventArgs e) => _leaveTimer.Start();
    }
}
