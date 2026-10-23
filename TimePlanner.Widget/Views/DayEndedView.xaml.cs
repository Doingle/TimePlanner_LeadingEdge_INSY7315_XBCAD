using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    public partial class DayEndedView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly DaySummary _day;

        public DayEndedView(WidgetFlow flow, DaySummary day)
        {
            InitializeComponent();
            _flow = flow;
            _day = day;
            DateText.Text = Format.Day(day.Day);

            if (day.Entries.Count > 0)
            {
                // A day with entries can be sent, so it can be not sent yet
                ReminderNotice.ShownChanged += (_, _) => ShowOpenTimesheet();
                ReminderNotice.Initialise(flow, DateOnly.FromDateTime(day.Day));
                ShowOpenTimesheet();
                LoggedValue.Text = Format.Duration(day.Logged);
                EntriesValue.Text = day.Entries.Count.ToString(CultureInfo.InvariantCulture);
                return;
            }

            LoggedValue.Text = "0m";
            EntriesValue.Text = "0";
            LoggedValue.Foreground = EntriesValue.Foreground = (Brush)FindResource("TextTertiaryBrush");
            EmptyHelp.Visibility = Visibility.Visible;
            ExportActions.Visibility = Visibility.Collapsed;
            EmptyActions.Visibility = Visibility.Visible;
            Card.Padding = (Thickness)FindResource("ShellPadding");
        }

        private void ShowOpenTimesheet() =>
            OpenTimesheetButton.Visibility = ReminderNotice.IsShown ? Visibility.Collapsed : Visibility.Visible;

        private async void Export_Click(object sender, RoutedEventArgs e) => await _flow.ExportCsvAsync(_day);

        private void OpenTimesheet_Click(object sender, RoutedEventArgs e) => _flow.ShowTimesheet(_day);

        private async void Resume_Click(object sender, RoutedEventArgs e) => await _flow.ResumeDayAsync();

        private void Close_Click(object sender, RoutedEventArgs e) => _flow.DismissDayEnded();
    }
}
