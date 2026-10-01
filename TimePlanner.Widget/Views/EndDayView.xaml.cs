using System.Windows;
using System.Windows.Controls;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    /// <summary>
    /// Interaction logic for EndDayView.xaml
    /// </summary>
    public partial class EndDayView : UserControl
    {
        private readonly WidgetFlow _flow;

        public EndDayView(WidgetFlow flow)
        {
            InitializeComponent();
            _flow = flow;

            var scheduler = flow.Scheduler;
            var unlogged = scheduler.Unlogged;
            if (unlogged >= TimeSpan.FromMinutes(1))
            {
                Subtitle.Text = $"{Format.Duration(unlogged)} since {Format.Clock(scheduler.PeriodStart)} is not logged yet.";
                LogLastButton.Content = $"Log the last {Format.Duration(unlogged)}";
            }
            else
            {
                Subtitle.Text = $"Everything is logged up to {Format.Clock(scheduler.PeriodStart)}.";
                UnloggedActions.Visibility = Visibility.Collapsed;
                LoggedActions.Visibility = Visibility.Visible;
            }
        }

        private async void LogLast_Click(object sender, RoutedEventArgs e) => await _flow.LogTimeAsync(LogEntryMode.EndOfDay);

        private async void EndDay_Click(object sender, RoutedEventArgs e) => await _flow.EndDayAsync();

        private void Cancel_Click(object sender, RoutedEventArgs e) => _flow.ShowTimer(activate: true);
    }
}
