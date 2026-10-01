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
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Widget.Controls;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    /// <summary>
    /// Interaction logic for LogEntryView.xaml
    /// </summary>
    public partial class LogEntryView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly LogEntryMode _mode;
        private readonly LogPeriod _period;
        private readonly bool _ready;

        public LogEntryView(WidgetFlow flow, LogEntryMode mode, LogPeriod period, IReadOnlyList<Project> projects, Project? project,
            ActivityChoices activities)
        {
            InitializeComponent();
            _flow = flow;
            _mode = mode;
            _period = period;

            var worked = Format.Duration(period.Worked);
            Subtitle.Text = mode == LogEntryMode.LogNow
                ? $"Since {Format.Clock(period.Start)} ({worked})"
                : $"{Format.Range(period.Start, period.End)} ({worked})";

            ProjectSelect.ItemsSource = projects;
            ProjectSelect.SelectedItem = project;
            NoProjectsHelp.Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LoadActivities(activities);

            if (mode == LogEntryMode.CheckIn)
            {
                Card.Padding = (Thickness)FindResource("ShellPaddingWithFoot");
                SnoozeRow.Visibility = Visibility.Visible;
                var snoozes = flow.Snooze.Left;
                SnoozeButton.Content = $"Snooze {Format.Interval(flow.Snooze.Minutes)} ({snoozes} left)";
                SnoozeButton.Visibility = snoozes > 0 ? Visibility.Visible : Visibility.Collapsed;
                NoSnoozes.Visibility = snoozes > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            else
            {
                CancelButton.Visibility = Visibility.Visible;
                CancelButton.IsCancel = true;
            }

            _ready = true;
        }

        public ActivityPicker Activity => ActivityPicker;

        private void LoadActivities(ActivityChoices choices)
        {
            var chosen = _flow.Session.Activity;
            if (chosen.Count > 0)
                ActivityPath.Include(choices.Tree, chosen);
            ActivityPicker.Load(choices.Tree, choices.Recent, chosen);
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectSelect.SelectedItem is not Project project)
            {
                Field.SetIsInvalid(ProjectSelect, true);
                ProjectSelect.Focus();
                return;
            }
            if (!ActivityPicker.Validate())
            {
                ActivityPicker.FocusField();
                return;
            }

            SaveButton.IsEnabled = false;
            try
            {
                await _flow.SaveEntryAsync(_mode, _period, project.ProjectID, ActivityPicker.SelectedPath, NoteBox.Text);
            }
            finally
            {
                SaveButton.IsEnabled = true;
            }
        }

        private async void Snooze_Click(object sender, RoutedEventArgs e) => await _flow.SnoozeCheckInAsync();

        private void Cancel_Click(object sender, RoutedEventArgs e) => _flow.CancelLogEntry(_mode);

        private async void Project_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || ProjectSelect.SelectedItem is not Project project)
                return;

            Field.SetIsInvalid(ProjectSelect, false);
            _flow.Session.ProjectId = project.ProjectID;
            var choices = await _flow.GetActivitiesAsync(project.ProjectID);
            if (ProjectSelect.SelectedItem == project)
                LoadActivities(choices);
        }

        private void Activity_SelectionChanged(object? sender, EventArgs e) => _flow.Session.Activity = ActivityPicker.SelectedPath;
    }
}
