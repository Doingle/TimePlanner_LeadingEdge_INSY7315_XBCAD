using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //summary screen presented when tracking for the day finishes
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
            }
            else
            {
                LoggedValue.Text = "0m";
                EntriesValue.Text = "0";
                LoggedValue.Foreground = EntriesValue.Foreground = (Brush)FindResource("TextTertiaryBrush");
                EmptyHelp.Visibility = Visibility.Visible;
                ExportActions.Visibility = Visibility.Collapsed;
                EmptyActions.Visibility = Visibility.Visible;
                Card.Padding = (Thickness)FindResource("ShellPadding");
            }

            Loaded += async (s, e) => await CheckUnbuiltAsync();
        }

        //-----------------------------
        //checks for unbuilt voice notes and shows build day button
        private async Task CheckUnbuiltAsync()
        {
            var dayOnly = DateOnly.FromDateTime(_day.Day);
            var unbuilt = await _flow.Ai.CountUnbuiltAsync(dayOnly);

            //offer day building when unbuilt notes exist
            if (unbuilt > 0)
            {
                var noteWord = unbuilt == 1 ? "voice note" : "voice notes";
                var label = $"Build my day ({unbuilt} {noteWord})";

                BuildMyDayExportButton.Content = label;
                BuildMyDayExportButton.Visibility = Visibility.Visible;
                ExportButton.Style = (Style)FindResource("Button.Secondary");
                OpenTimesheetButton.Style = (Style)FindResource("Button.Secondary");

                BuildMyDayEmptyButton.Content = label;
                BuildMyDayEmptyButton.Visibility = Visibility.Visible;
                ResumeDayEmptyButton.Style = (Style)FindResource("Button.Secondary");
                OpenTimesheetEmptyButton.Style = (Style)FindResource("Button.Secondary");
            }
        }

        //-----------------------------
        //opens day build view from day ended screen
        private async void BuildMyDay_Click(object sender, RoutedEventArgs e)
        {
            await _flow.ShowDayBuildAsync(DateOnly.FromDateTime(_day.Day), () => _flow.ShowDayEnded(_day));
        }

        //-----------------------------
        //exports day entries as csv file
        private void ShowOpenTimesheet() =>
            OpenTimesheetButton.Visibility = ReminderNotice.IsShown ? Visibility.Collapsed : Visibility.Visible;

        private async void Export_Click(object sender, RoutedEventArgs e) => await _flow.ExportCsvAsync(_day);

        //-----------------------------
        //opens timesheet review screen
        private void OpenTimesheet_Click(object sender, RoutedEventArgs e) => _flow.ShowTimesheet(_day);

        //-----------------------------
        //resumes tracking for the day
        private async void Resume_Click(object sender, RoutedEventArgs e) => await _flow.ResumeDayAsync();

        //-----------------------------
        //dismisses day ended screen
        private void Close_Click(object sender, RoutedEventArgs e) => _flow.DismissDayEnded();
    }
}
//------------------------------EOF-----------------------------\\
