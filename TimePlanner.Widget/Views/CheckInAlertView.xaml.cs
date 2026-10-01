using System.Windows;
using System.Windows.Controls;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    /// <summary>
    /// Interaction logic for CheckInAlertView.xaml
    /// </summary>
    public partial class CheckInAlertView : UserControl
    {
        private readonly WidgetFlow _flow;

        public CheckInAlertView(WidgetFlow flow)
        {
            InitializeComponent();
            _flow = flow;
            Subtitle.Text = $"Log what you did since {Format.Clock(flow.Scheduler.PeriodStart)}.";

            var snoozes = flow.Snooze.Left;
            SnoozeButton.Content = $"Snooze {Format.Interval(flow.Snooze.Minutes)} ({snoozes} left)";
            SnoozeButton.Visibility = snoozes > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoSnoozes.Visibility = snoozes > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void LogTime_Click(object sender, RoutedEventArgs e) => await _flow.LogTimeAsync(LogEntryMode.CheckIn);

        private void Snooze_Click(object sender, RoutedEventArgs e) => _flow.SnoozeCheckIn();
    }
}
