using System;
using System.Collections.Generic;
using System.Globalization;
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
    //-----------------------------
    //screen for creating or editing a timesheet entry
    public partial class EntryEditView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly DateOnly _day;
        private readonly TimesheetSlot? _entry;
        private readonly List<Project> _projects;
        private readonly Action _back;
        private readonly bool _ready;

        public EntryEditView(WidgetFlow flow, DateOnly day, TimesheetSlot? entry, DateTime start, DateTime end, IReadOnlyList<Project> projects, ActivityChoices activities, Action back)
        {
            InitializeComponent();
            _flow = flow;
            _day = day;
            _entry = entry;
            _projects = projects.ToList();
            _back = back;

            TitleText.Text = entry != null ? "Edit entry" : "Add time";
            SubtitleText.Text = Format.Day(day.ToDateTime(TimeOnly.MinValue));

            StartBox.Text = FormatTime(start);
            EndBox.Text = FormatTime(end);

            //configures project picker for company and project selection
            ProjectPicker.FieldName = "Project";
            ProjectPicker.Placeholder = "Choose a client and project";
            ProjectPicker.MaxDepth = 2;
            ProjectPicker.RequiredDepth = 2;
            ProjectPicker.AllowRootAdd = true;
            ProjectPicker.GuidedChildAdd = true;
            ProjectPicker.MaxNameLength = InputLimits.CompanyOrProject;
            ProjectPicker.CleanName = InputLimits.CleanCompanyOrProjectName;
            ProjectPicker.AddLabelFor = path => path.Count == 0 ? "New company…" : $"New project in {path[^1]}…";
            ProjectPicker.CanRemove = null;

            var selectedProjectPath = entry != null && !string.IsNullOrEmpty(entry.Company) ? new[] { entry.Company, entry.Project } : Array.Empty<string>();
            var recentProjectPath = selectedProjectPath.Length > 0 ? new List<IReadOnlyList<string>> { selectedProjectPath } : new List<IReadOnlyList<string>>();
            ProjectPicker.Load(BuildProjectTree(_projects), recentProjectPath, selectedProjectPath);

            ActivityPicker.Load(activities.Tree, activities.Recent, entry?.ActivityPath ?? Array.Empty<string>());

            //prefills note box when editing
            if (entry != null)
            {
                NoteBox.Text = entry.Note ?? string.Empty;
            }

            _ready = true;
        }

        //-----------------------------
        //saves the entry edits
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorLine.Visibility = Visibility.Collapsed;

            //parses start time from box
            if (!TimeOnly.TryParseExact(StartBox.Text?.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime))
            {
                ShowError("Use times like 09:30.");
                return;
            }

            //parses end time from box
            if (!TimeOnly.TryParseExact(EndBox.Text?.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime))
            {
                ShowError("Use times like 09:30.");
                return;
            }

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

            var startDateTime = _day.ToDateTime(startTime);
            var endDateTime = _day.ToDateTime(endTime);
            var edit = new EntryEdit(startDateTime, endDateTime, ProjectPicker.SelectedPath, ActivityPicker.SelectedPath, NoteBox.Text);

            SaveButton.IsEnabled = false;

            try
            {
                var result = _entry != null && _entry.EntryId.HasValue
                    ? await _flow.UpdateEntryAsync(_entry.EntryId.Value, edit)
                    : await _flow.AddEntryAsync(edit);

                //displays validation error when edit fails
                if (!result.Ok)
                {
                    ShowError(result.Error ?? "Could not save entry.");
                    return;
                }

                _back();
            }
            catch (ArgumentException ex)
            {
                //shows error message when activity path fails
                ShowError(ex.Message);
            }
            finally
            {
                SaveButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //shows error text in the card
        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorLine.Visibility = Visibility.Visible;
        }

        //-----------------------------
        //formats date time as HH:mm string
        private static string FormatTime(DateTime dt) => dt.ToString("HH:mm", CultureInfo.InvariantCulture);

        //-----------------------------
        //cancels editing and navigates back
        private void Cancel_Click(object sender, RoutedEventArgs e) => _back();

        //-----------------------------
        //handles project picker selection change
        private async void Project_SelectionChanged(object? sender, EventArgs e)
        {
            //skips when UI is loading
            if (!_ready)
            {
                return;
            }

            var project = FindProject(ProjectPicker.SelectedPath);
            _flow.Session.ProjectId = project?.ProjectID;
            var choices = project != null
                ? await _flow.GetActivitiesAsync(project.ProjectID)
                : await _flow.GetActivityTreeAsync();

            ActivityPicker.Load(choices.Tree, choices.Recent, ActivityPicker.SelectedPath);
        }

        //-----------------------------
        //tracks activity picker selection
        private void Activity_SelectionChanged(object? sender, EventArgs e) => _flow.Session.Activity = ActivityPicker.SelectedPath;

        //-----------------------------
        //builds project tree from project list
        private static List<ActivityNode> BuildProjectTree(IReadOnlyList<Project> projects)
        {
            var result = new List<ActivityNode>();
            var grouped = projects
                .GroupBy(p => p.Company?.Name ?? string.Empty)
                .OrderByDescending(g => BillingRules.IsInternal(g.Key))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            //groups projects under company nodes
            foreach (var group in grouped)
            {
                var companyName = group.Key;
                var childNodes = group
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new ActivityNode(p.Name, []))
                    .ToArray();

                result.Add(new ActivityNode(companyName, childNodes));
            }

            return result;
        }

        //-----------------------------
        //finds project by company and project path
        private Project? FindProject(IReadOnlyList<string> path)
        {
            //path requires company and project name
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
