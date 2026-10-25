using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimePlanner.Core.Services;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    /// <summary>
    /// Day ended. On the day itself it offers Export CSV, the timesheet and Resume day. Once the date has
    /// changed, Resume day goes: the main button is Start today, and the ended day is one click away.
    /// A day the widget closed because it was left open overnight says so.
    /// </summary>
    public partial class DayEndedView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly DaySummary _day;
        private readonly DateOnly _date;
        private bool? _showingToday;

        public DayEndedView(WidgetFlow flow, DaySummary day)
        {
            InitializeComponent();
            _flow = flow;
            _day = day;
            _date = DateOnly.FromDateTime(day.Day);

            var scheduler = flow.Scheduler;
            var overnight = scheduler.ClosedOvernight is { } closed && DayRules.DateOf(closed) == _date ? closed : null;
            var ended = overnight ?? (scheduler.LastEnded is { } last && DayRules.DateOf(last) == _date ? last : null);
            // The status line already has today's date, so only an earlier day names its date here
            var date = IsToday ? null : Format.Date(_date);
            DateText.Text = ended?.EndedAt is { } endedAt
                ? date == null ? $"Ended at {Format.Clock(endedAt)}" : $"{date} · ended at {Format.Clock(endedAt)}"
                : date ?? "Today";
            if (overnight?.EndedAt is { } closedAt)
            {
                OvernightText.Text = $"It was left open overnight, so it ended at {Format.Clock(closedAt)}, your last activity.";
                OvernightNote.Visibility = Visibility.Visible;
            }

            if (day.Entries.Count > 0)
            {
                // A day with entries can be sent, so it can be not sent yet
                ReminderNotice.ShownChanged += (_, _) => ShowOpenTimesheet();
                ReminderNotice.Initialise(flow, _date);
                _ = EarlierNotice.LoadAsync(flow, () => flow.ShowDayEnded(day), only: _date);
                LoggedValue.Text = Format.Duration(day.Logged);
                EntriesValue.Text = day.Entries.Count.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                LoggedValue.Text = "0m";
                EntriesValue.Text = "0";
                LoggedValue.Foreground = EntriesValue.Foreground = (Brush)FindResource("TextTertiaryBrush");
                EmptyHelp.Visibility = Visibility.Visible;
            }

            ShowActions();

            // Left open past midnight, the screen moves on to Start today by itself
            Loaded += (_, _) => flow.Scheduler.Ticked += Scheduler_Ticked;
            Unloaded += (_, _) => flow.Scheduler.Ticked -= Scheduler_Ticked;
            Loaded += async (s, e) => await CheckUnbuiltAsync();
        }

        private bool IsToday => DateOnly.FromDateTime(_flow.Scheduler.Now) == _date;

        private void Scheduler_Ticked(object? sender, EventArgs e)
        {
            if (_showingToday != IsToday)
                ShowActions();
        }

        private void ShowActions()
        {
            var today = IsToday;
            var entries = _day.Entries.Count > 0;
            _showingToday = today;

            ExportActions.Visibility = today && entries ? Visibility.Visible : Visibility.Collapsed;
            EmptyActions.Visibility = today && !entries ? Visibility.Visible : Visibility.Collapsed;
            EarlierActions.Visibility = today ? Visibility.Collapsed : Visibility.Visible;
            EarlierExportRow.Visibility = entries ? Visibility.Visible : Visibility.Collapsed;
            EmptyHelp.Text = today
                ? "There is nothing to export. Resume the day if you are still working."
                : "Nothing was logged on this day.";

            // With entries, both layouts end in a text button, which sits closer to the edge
            Card.Padding = (Thickness)FindResource(entries ? "ShellPaddingWithFoot" : "ShellPadding");
            ShowOpenTimesheet();
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

        private async void StartToday_Click(object sender, RoutedEventArgs e) => await _flow.StartTrackingAsync();

        //-----------------------------
        //dismisses day ended screen
        private void Close_Click(object sender, RoutedEventArgs e) => _flow.DismissDayEnded();
    }
}
//------------------------------EOF-----------------------------\\
