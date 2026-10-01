using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    /// <summary>
    /// Interaction logic for TimesheetReviewView.xaml
    /// </summary>
    public partial class TimesheetReviewView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly DaySummary _day;

        public TimesheetReviewView(WidgetFlow flow, DaySummary day)
        {
            InitializeComponent();
            _flow = flow;
            _day = day;
            Summary.Text = $"{Format.Day(day.Day)} · {Format.Duration(day.Logged)}";

            EntryList.ItemsSource = day.Entries.Select(entry =>
            {
                var task = entry.Task;
                var project = task?.Project;
                return new Row(
                    Format.Range(entry.StartTime, entry.EndTime),
                    Format.Duration(entry.GetDuration()),
                    task != null ? string.Join(ActivityPath.Separator, ActivityPath.Of(task)) : string.Empty,
                    project?.Company is { } company ? $"{project.Name} · {company.Name}" : project?.Name ?? string.Empty,
                    entry.Note);
            }).ToList();

            if (day.Entries.Count == 0)
            {
                EntryScroller.Visibility = Visibility.Collapsed;
                ExportButton.Visibility = Visibility.Collapsed;
                EmptyHelp.Visibility = Visibility.Visible;
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => _flow.ShowDayEnded(_day);

        private async void Export_Click(object sender, RoutedEventArgs e) => await _flow.ExportCsvAsync(_day);

        public sealed record Row(string Time, string Length, string Activity, string Project, string? Note)
        {
            public Visibility NoteVisibility => string.IsNullOrEmpty(Note) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
