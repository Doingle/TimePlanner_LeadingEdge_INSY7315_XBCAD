using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
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
        private readonly List<Project> _projects;
        private readonly bool _ready;

        public LogEntryView(WidgetFlow flow, LogEntryMode mode, LogPeriod period, IReadOnlyList<Project> projects, Project? project, ActivityChoices activities)
        {
            InitializeComponent();
            _flow = flow;
            _mode = mode;
            _period = period;
            _projects = projects.ToList();

            var worked = Format.Duration(period.Worked);
            Subtitle.Text = mode == LogEntryMode.LogNow
                ? $"Since {Format.Clock(period.Start)} ({worked})"
                : $"{Format.Range(period.Start, period.End)} ({worked})";

            //client then project with guided adding
            ProjectPicker.FieldName = "Project";
            ProjectPicker.Placeholder = "Choose a client and project";
            ProjectPicker.MaxDepth = 2;
            ProjectPicker.RequiredDepth = 2;
            ProjectPicker.AllowRootAdd = true;
            ProjectPicker.GuidedChildAdd = true;
            ProjectPicker.MaxNameLength = InputLimits.CompanyOrProject;
            ProjectPicker.CleanName = InputLimits.CleanCompanyOrProjectName;
            ProjectPicker.AddLabelFor = path => path.Count == 0 ? "New company…" : $"New project in {path[^1]}…";
            ProjectPicker.CanRemove = path => !BillingRules.IsInternal(path[0]);
            ProjectPicker.RemoveRequested += ProjectPicker_RemoveRequested;
            ProjectPicker.Load(BuildProjectTree(_projects), ProjectRecent(project), ProjectPath(project));

            //activity picker removal handler
            ActivityPicker.CanRemove = path => path.Count > 1;
            ActivityPicker.RemoveRequested += ActivityPicker_RemoveRequested;

            LoadActivities(activities);

            //check in mode has snooze options
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

        //-----------------------------
        //loads activity choices into the picker
        private void LoadActivities(ActivityChoices choices)
        {
            var chosen = _flow.Session.Activity;

            //include chosen path if present
            if (chosen.Count > 0)
            {
                ActivityPath.Include(choices.Tree, chosen);
            }

            ActivityPicker.Load(choices.Tree, choices.Recent, chosen);
        }

        //-----------------------------
        //saves the entry after validation
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            //validates project selection
            if (!ProjectPicker.Validate())
            {
                ProjectPicker.FocusField();
                return;
            }

            //validates activity selection
            if (!ActivityPicker.Validate())
            {
                ActivityPicker.FocusField();
                return;
            }

            SaveButton.IsEnabled = false;

            try
            {
                await _flow.SaveEntryAsync(_mode, _period, ProjectPicker.SelectedPath, ActivityPicker.SelectedPath, NoteBox.Text);
            }
            catch (ArgumentException ex)
            {
                //shows popup when saving fails
                MessageBox.Show(ex.Message, "Can't save entry");
            }
            finally
            {
                SaveButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //snoozes the check in prompt
        private async void Snooze_Click(object sender, RoutedEventArgs e) => await _flow.SnoozeCheckInAsync();

        //-----------------------------
        //cancels entry creation
        private void Cancel_Click(object sender, RoutedEventArgs e) => _flow.CancelLogEntry(_mode);

        //-----------------------------
        //handles project picker selection change
        private async void Project_SelectionChanged(object? sender, EventArgs e)
        {
            //skips when control is not ready
            if (!_ready)
            {
                return;
            }

            var project = FindProject(ProjectPicker.SelectedPath);
            _flow.Session.ProjectId = project?.ProjectID;
            var choices = project != null
                ? await _flow.GetActivitiesAsync(project.ProjectID)
                : await _flow.GetActivityTreeAsync();

            LoadActivities(choices);
        }

        //-----------------------------
        //tracks selected activity path
        private void Activity_SelectionChanged(object? sender, EventArgs e) => _flow.Session.Activity = ActivityPicker.SelectedPath;

        //-----------------------------
        //handles removing a company or project
        private async void ProjectPicker_RemoveRequested(object? sender, IReadOnlyList<string> path)
        {
            var confirm = MessageBox.Show(
                $"Remove {string.Join(" › ", path)}?\nUnused items are deleted. Used ones are hidden and logged time is kept.",
                "Remove",
                MessageBoxButton.YesNo);

            //user cancelled removal
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var outcome = path.Count == 1
                ? await _flow.RemoveCompanyAsync(path[0])
                : await _flow.RemoveProjectAsync(path[0], path[1]);

            //removes node from UI if allowed
            if (outcome.Result != RemoveResult.NotAllowed)
            {
                ProjectPicker.RemoveNode(path);

                //updates local project cache
                if (path.Count == 1)
                {
                    _projects.RemoveAll(p => string.Equals(p.Company?.Name, path[0], StringComparison.OrdinalIgnoreCase));
                }
                else if (path.Count == 2)
                {
                    _projects.RemoveAll(p => string.Equals(p.Company?.Name, path[0], StringComparison.OrdinalIgnoreCase) && string.Equals(p.Name, path[1], StringComparison.OrdinalIgnoreCase));
                }
            }

            //shows message unless deleted cleanly
            if (outcome.Result != RemoveResult.Deleted)
            {
                MessageBox.Show(outcome.Message, "Remove");
            }
        }

        //-----------------------------
        //handles removing an activity
        private async void ActivityPicker_RemoveRequested(object? sender, IReadOnlyList<string> path)
        {
            var confirm = MessageBox.Show(
                $"Remove {string.Join(" › ", path)}?\nUnused items are deleted. Used ones are hidden and logged time is kept.",
                "Remove",
                MessageBoxButton.YesNo);

            //user cancelled removal
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var outcome = await _flow.RemoveActivityAsync(path);

            //removes node from UI if allowed
            if (outcome.Result != RemoveResult.NotAllowed)
            {
                ActivityPicker.RemoveNode(path);
            }

            //shows message unless deleted cleanly
            if (outcome.Result != RemoveResult.Deleted)
            {
                MessageBox.Show(outcome.Message, "Remove");
            }
        }

        //-----------------------------
        //builds tree of company and project nodes
        private static List<ActivityNode> BuildProjectTree(IReadOnlyList<Project> projects)
        {
            var result = new List<ActivityNode>();
            var grouped = projects
                .GroupBy(p => p.Company?.Name ?? string.Empty)
                .OrderByDescending(g => BillingRules.IsInternal(g.Key))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            //builds nodes for each company group
            foreach (var group in grouped)
            {
                var companyName = group.Key;
                var childNodes = group
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new ActivityNode(p.Name, []))
                    .ToList();

                result.Add(new ActivityNode(companyName, childNodes.ToArray()));
            }

            return result;
        }

        //-----------------------------
        //gets path array for a project
        private static IReadOnlyList<string> ProjectPath(Project? p) =>
            p != null && !string.IsNullOrEmpty(p.Company?.Name) ? [p.Company.Name, p.Name] : Array.Empty<string>();

        //-----------------------------
        //gets recent project paths
        private static List<IReadOnlyList<string>> ProjectRecent(Project? p)
        {
            var path = ProjectPath(p);

            return path.Count > 0 ? [path] : [];
        }

        //-----------------------------
        //finds project by company and project path
        private Project? FindProject(IReadOnlyList<string> path)
        {
            //path must have company and project name
            if (path.Count < 2)
            {
                return null;
            }

            return _projects.FirstOrDefault(p =>
                string.Equals(p.Company?.Name, path[0], StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Name, path[1], StringComparison.OrdinalIgnoreCase));
        }
    }
}
//------------------------------EOF-----------------------------\\
