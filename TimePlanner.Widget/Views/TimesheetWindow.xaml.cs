using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Core.Sync;
using TimePlanner.Widget.Controls;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //the wide timesheet: one row per entry, edited in place under the same rules as the widget's editor, with the date bar
    //and the send always in view. Restore goes back to the small page in the widget; its size, place and maximised state
    //are remembered in the preferences file
    public partial class TimesheetWindow : Window
    {
        // Start, End, Client › Project and Activity keep their width; Note takes what is left and gives it up first
        private static readonly double[] FixedColumns = [88, 88, 176, 176];
        private const double NoteMinWidth = 160;

        // The row's buttons: Save and Cancel while it is edited, and Edit and Delete with their words in large view
        private const double ActionsWidth = 144;
        private static readonly Size DefaultSize = new(920, 560);
        private static readonly Size MinimumSize = new(560, 360);
        private static readonly string[] TimeFormats = ["h:mm tt", "h:mmtt", "h:mm t", "H:mm", "HH:mm"];

        private readonly WidgetFlow _flow;
        private readonly DispatcherTimer _placeSaver = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly bool _placed;
        private double _scale = 1;
        private bool _shown;
        private DateOnly _day;
        private IReadOnlyList<TimesheetSlot> _slots = [];
        private TimesheetReviewModel? _model;
        private TimesheetActions? _actions;
        private RowEditor? _editing;
        private DispatcherTimer? _undoTimer;
        private Func<Task>? _undo;

        public TimesheetWindow(WidgetFlow flow)
        {
            InitializeComponent();
            _flow = flow;
            _placed = PlaceAsLeft(flow.Session.Preferences.TimesheetWindow);
            ColumnTitles.ColumnDefinitions.Clear();
            AddColumns(ColumnTitles);
            AddCell(ColumnTitles, 0, new TextBlock { Text = "Start", Style = (Style)FindResource("ColumnTitle") });
            AddCell(ColumnTitles, 1, new TextBlock { Text = "End", Style = (Style)FindResource("ColumnTitle") });
            AddCell(ColumnTitles, 2, new TextBlock { Text = "Client › Project", Style = (Style)FindResource("ColumnTitle") });
            AddCell(ColumnTitles, 3, new TextBlock { Text = "Activity", Style = (Style)FindResource("ColumnTitle") });
            AddCell(ColumnTitles, 4, new TextBlock { Text = "Note", Style = (Style)FindResource("ColumnTitle") });

            //the place is saved a moment after the window stops moving or resizing, and when it closes
            _placeSaver.Tick += (_, _) =>
            {
                _placeSaver.Stop();
                SavePlace();
            };
            LocationChanged += (_, _) => RestartPlaceSaver();
            SizeChanged += (_, _) => RestartPlaceSaver();
            StateChanged += (_, _) =>
            {
                FitMaximised();
                RestartPlaceSaver();
            };
            Closing += (_, _) =>
            {
                _placeSaver.Stop();
                SavePlace();
            };
            TableScroller.SizeChanged += (_, _) => FitTable();

            //entries logged in the widget meanwhile show when the window comes back to the front
            Activated += async (_, _) =>
            {
                if (_shown && _editing == null)
                    await ReloadAsync();
                _shown = true;
            };
            FitMaximised();
        }

        //-----------------------------
        //large view: the whole window 1.5 times, its click areas too, with a word on each icon button. The window's own size
        //grows or shrinks with it, within the screen
        public void ApplySize(bool large)
        {
            var scale = large ? WidgetCard.LargeScale : 1;
            WidgetCard.SetIsLarge(Root, large);
            if (large)
                Root.LayoutTransform = new ScaleTransform(scale, scale);
            else
                Root.ClearValue(LayoutTransformProperty);
            WindowChrome.GetWindowChrome(this).CaptionHeight = Header.Height * scale;

            var area = SystemParameters.WorkArea;
            MinWidth = Math.Min(MinimumSize.Width * scale, area.Width);
            MinHeight = Math.Min(MinimumSize.Height * scale, area.Height);

            //a first opening starts at the default size for the scale, and an open window grows or shrinks with what is in it.
            //A size the window was left at is the user's own, so it opens at that
            if (scale != _scale && WindowState == WindowState.Normal && (_shown || !_placed))
            {
                Width = Math.Min(Width * scale / _scale, area.Width);
                Height = Math.Min(Height * scale / _scale, area.Height);
            }
            _scale = scale;
            FitTable();
        }

        //-----------------------------
        //the status line from the widget, on the purple header: the date, and what the day is doing in words
        public void ShowStatus(CardStatus status)
        {
            StatusDate.Text = status.Date;
            StatusState.Text = status.State;
            StatusDot.Fill = (Brush)FindResource(status.Live ? "LiveBrush" : "TextOnActionSecondaryBrush");
            StatusPill.ToolTip = status.Spoken;
            AutomationProperties.SetName(StatusPill, status.Spoken);
        }

        //-----------------------------
        //shows one day: the date bar and a row per entry. Send now (from a notice that the day wasn't sent) starts the send once it shows
        public void Load(DateOnly day, IReadOnlyList<TimesheetSlot> slots, DayPreview preview, SentDay? sent, PendingDay? pending, bool sendNow = false)
        {
            //an undo belongs to the day it was offered on
            if (day != _day)
                HideUndo();

            _day = day;
            _slots = slots;
            _model = new TimesheetReviewModel(day, DateOnly.FromDateTime(_flow.Scheduler.Now), slots, preview, sent, pending);
            _actions = new TimesheetActions(_flow, day, Reopen);
            _editing = null;

            DayText.Text = _model.LongDayText;
            DayButton.ToolTip = _model.Subtitle;
            AutomationProperties.SetName(DayButton, $"Day shown: {Format.Date(day)}. Choose another day");
            TotalText.Text = _model.Total;
            TimesheetReviewView.ShowBadge(SendBadge, BadgeIcon, BadgeText, _model);
            StatusText.Text = _model.Status;
            StatusText.Visibility = _model.ShowsStatus ? Visibility.Visible : Visibility.Collapsed;
            PastDayHelp.Visibility = _model.IsPastDay ? Visibility.Visible : Visibility.Collapsed;
            TodayButton.Visibility = _model.IsPastDay ? Visibility.Visible : Visibility.Collapsed;
            NextDayButton.IsEnabled = _model.IsPastDay;
            SendButton.Content = _model.SendLabel;
            SendButton.IsEnabled = _model.CanSend;
            RenderRows();

            if (sendNow && _model.CanSend)
                _ = Dispatcher.InvokeAsync(SendAsync, DispatcherPriority.ApplicationIdle);
        }

        //-----------------------------
        //the same day again, after something changed it; through the flow, so a sign-in in the widget goes back too
        private void Reopen() => _ = _flow.ReopenWideTimesheetAsync(_day);

        //-----------------------------
        //the same day again, without bringing the window to the front
        private async Task ReloadAsync()
        {
            var (slots, preview, sent, pending) = await _flow.LoadTimesheetAsync(_day);
            Load(_day, slots, preview, sent, pending);
        }

        //-----------------------------
        //a row for each entry; the one being edited, or a new one being added at the end, as its boxes
        private void RenderRows()
        {
            Rows.Children.Clear();
            foreach (var slot in _slots.Where(s => s.Kind == SlotKind.Entry))
                Rows.Children.Add(_editing != null && _editing.EntryId == slot.EntryId ? _editing.Block : ReadRow(slot));

            if (_editing is { EntryId: null })
                Rows.Children.Add(_editing.Block);

            //shows message when day has nothing logged
            if (Rows.Children.Count == 0)
            {
                Rows.Children.Add(new TextBlock
                {
                    Text = "Nothing was logged on this day.",
                    Style = (Style)FindResource("Text.Help"),
                    Margin = new Thickness(8, 12, 0, 0),
                });
            }
        }

        //-----------------------------
        //an entry as it reads: its times, client and project, activity and note, then Edit and Delete. A click on the row edits it too
        private Border ReadRow(TimesheetSlot slot)
        {
            var row = NewRow();
            AddCell(row, 0, CellText(Format.Clock(slot.Start)));
            AddCell(row, 1, CellText(Format.Clock(slot.End)));
            AddCell(row, 2, CellText($"{slot.Company} › {slot.Project}"));
            AddCell(row, 3, CellText(string.Join(" › ", slot.ActivityPath)));
            AddCell(row, 4, string.IsNullOrWhiteSpace(slot.Note)
                ? CellText("Add a note", (Brush)FindResource("TextTertiaryBrush"))
                : CellText(slot.Note));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var edit = RowAction(IconKind.Edit, "Edit", $"Edit the entry from {Format.Range(slot.Start, slot.End)}");
            edit.Click += async (_, e) =>
            {
                e.Handled = true;
                await BeginEditAsync(slot, slot.Start, slot.End);
            };
            actions.Children.Add(edit);

            var delete = RowAction(IconKind.Trash, "Delete", $"Delete the entry from {Format.Range(slot.Start, slot.End)}");
            delete.Margin = new Thickness(4, 0, 0, 0);
            delete.Click += async (_, e) =>
            {
                e.Handled = true;
                await DeleteAsync(slot);
            };
            actions.Children.Add(delete);
            AddCell(row, 5, actions);

            var line = Underlined(row);
            line.Cursor = Cursors.Hand;
            line.MouseLeftButtonUp += async (_, _) => await BeginEditAsync(slot, slot.Start, slot.End);
            return line;
        }

        //-----------------------------
        //starts editing a row (or a new one, without an entry), in place. One row at a time: an edit already open is left unsaved
        private async Task BeginEditAsync(TimesheetSlot? slot, DateTime start, DateTime end)
        {
            var projects = await _flow.GetProjectsAsync();
            var activities = await _flow.GetActivityTreeAsync();
            _editing = new RowEditor(this, slot, start, end, projects, activities);
            RenderRows();
            _editing.Block.BringIntoView();
            await Dispatcher.InvokeAsync(() => _editing?.Start.Focus(), DispatcherPriority.Input);
        }

        //-----------------------------
        //leaves the row being edited, nothing saved
        private void CancelEdit()
        {
            _editing = null;
            RenderRows();
        }

        //-----------------------------
        //saves the row. An edit that would change the entries around it asks first, in a banner under the row, and nothing is
        //saved until Yes
        private async Task SaveAsync(RowEditor editor)
        {
            editor.HideMessages();

            //times are read to the minute
            if (!TryReadTime(editor.Start.Text, out var start) || !TryReadTime(editor.End.Text, out var end))
            {
                editor.ShowError("Use times like 9:30 AM.");
                return;
            }

            //validates project and activity
            if (!editor.Project.Validate())
            {
                editor.Project.FocusField();
                return;
            }
            if (!editor.Activity.Validate())
            {
                editor.Activity.FocusField();
                return;
            }

            var edit = new EntryEdit(_day.ToDateTime(start), _day.ToDateTime(end), editor.Project.SelectedPath, editor.Activity.SelectedPath, editor.Note.Text);
            editor.Save.IsEnabled = false;

            try
            {
                if (editor.EntryId is int entryId)
                {
                    var preview = await _flow.PreviewUpdateEntryAsync(entryId, edit);

                    if (!preview.Ok)
                    {
                        editor.ShowError(preview.Error ?? "Could not save entry.");
                        return;
                    }

                    if (preview.NeedsConfirmation)
                    {
                        editor.ShowBanner(edit, preview.Changes);
                        return;
                    }
                }

                var result = editor.EntryId is int id
                    ? await _flow.UpdateEntryAsync(id, edit)
                    : await _flow.AddEntryAsync(edit);

                //a refused save stays open with the reason
                if (!result.Ok)
                {
                    editor.ShowError(result.Error ?? "Could not save entry.");
                    return;
                }

                _editing = null;
                Reopen();
            }
            catch (ArgumentException ex)
            {
                //shows error message when a path fails
                editor.ShowError(ex.Message);
            }
            finally
            {
                editor.Save.IsEnabled = true;
            }
        }

        //-----------------------------
        //Yes on the banner: saves the edit and the changes it described, in one go, and offers an undo that reverses all of it
        private async Task ConfirmAsync(RowEditor editor)
        {
            if (editor.EntryId is not int entryId || editor.Pending is not { } edit)
            {
                return;
            }

            editor.Yes.IsEnabled = false;

            try
            {
                var result = await _flow.UpdateEntryAsync(entryId, edit, confirmed: true);

                //a refused save goes back to the row with the reason
                if (!result.Ok)
                {
                    editor.HideMessages();
                    editor.ShowError(result.Error ?? "Could not save entry.");
                    return;
                }

                _editing = null;
                ShowUndo("Entry updated · other entries changed", async () =>
                {
                    var undone = await _flow.RestoreEntriesAsync(result);

                    //an undo that can no longer be done safely says why
                    if (!undone.Ok)
                    {
                        MessageBox.Show(undone.Error ?? "Could not undo.", "Undo");
                    }

                    Reopen();
                });
                Reopen();
            }
            catch (ArgumentException ex)
            {
                //shows error message when a path fails
                editor.HideMessages();
                editor.ShowError(ex.Message);
            }
            finally
            {
                editor.Yes.IsEnabled = true;
            }
        }

        //-----------------------------
        //reads a time as the row shows it ("9:30 AM") or as the widget's editor takes it ("09:30")
        private static bool TryReadTime(string? text, out TimeOnly time) =>
            TimeOnly.TryParseExact(text?.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowInnerWhite, out time);

        //-----------------------------
        //deletes an entry with confirmation and shows undo bar
        private async Task DeleteAsync(TimesheetSlot slot)
        {
            //nothing deleted: no undo to offer
            if (_actions == null || await _actions.DeleteAsync(slot) is not { } undo)
            {
                return;
            }

            ShowUndo("Entry deleted", undo);
            Reopen();
        }

        //-----------------------------
        //shows temporary undo bar
        private void ShowUndo(string message, Func<Task> undo)
        {
            UndoText.Text = message;
            _undo = undo;
            UndoBar.Visibility = Visibility.Visible;

            _undoTimer?.Stop();
            _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _undoTimer.Tick += (_, _) => HideUndo();
            _undoTimer.Start();
        }

        private void HideUndo()
        {
            _undoTimer?.Stop();
            UndoBar.Visibility = Visibility.Collapsed;
            _undo = null;
        }

        //-----------------------------
        //executes pending undo action
        private async void Undo_Click(object sender, RoutedEventArgs e)
        {
            var undo = _undo;
            HideUndo();
            if (undo != null)
                await undo();
        }

        //-----------------------------
        //Restore: back to the small page in the widget, on the same day
        private void Restore_Click(object sender, RoutedEventArgs e) => _ = _flow.RestoreTimesheetAsync(_day);

        private void PrevDay_Click(object sender, RoutedEventArgs e) => _ = _flow.ReopenWideTimesheetAsync(_day.AddDays(-1));

        private void NextDay_Click(object sender, RoutedEventArgs e) => _ = _flow.ReopenWideTimesheetAsync(_day.AddDays(1));

        private void Today_Click(object sender, RoutedEventArgs e) =>
            _ = _flow.ReopenWideTimesheetAsync(DateOnly.FromDateTime(_flow.Scheduler.Now));

        //-----------------------------
        //opens the list of recent days with what was logged and sent on each
        private async void DayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_actions != null)
                await _actions.ShowDayMenuAsync(DayButton, picked => _ = _flow.ReopenWideTimesheetAsync(picked));
        }

        //-----------------------------
        //a new row at the end, starting where the last entry ended
        private async void AddTime_Click(object sender, RoutedEventArgs e)
        {
            var (start, end) = TimesheetReviewModel.NewEntryRange(_day, _slots);
            await BeginEditAsync(null, start, end);
        }

        private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

        //-----------------------------
        //asks, then sends the day (the same steps as the small page)
        private async Task SendAsync()
        {
            if (_actions != null && _model != null)
                await _actions.SendAsync(_model, SendButton);
        }

        private async void Export_Click(object sender, RoutedEventArgs e) => await _flow.ExportDayAsync(_day);

        //-----------------------------
        //the row's columns: the fixed ones, Note, and the row's buttons, the same width on every row
        private static void AddColumns(Grid grid)
        {
            foreach (var width in FixedColumns)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = NoteMinWidth });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "Actions" });
        }

        private static Grid NewRow()
        {
            var row = new Grid { MinHeight = 48, Background = Brushes.Transparent };
            AddColumns(row);
            return row;
        }

        private static void AddCell(Grid row, int column, UIElement cell)
        {
            Grid.SetColumn(cell, column);
            row.Children.Add(cell);
        }

        //-----------------------------
        //a row with the line under it
        private Border Underlined(Grid row) => new()
        {
            Child = row,
            BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        //-----------------------------
        //one cell's text, cut short with its whole text as the tooltip when the column is narrow
        private TextBlock CellText(string text, Brush? brush = null)
        {
            var cell = new TextBlock
            {
                Text = text,
                Style = (Style)FindResource("Text.Body"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 12, 0),
                ToolTip = text,
            };
            if (brush != null)
                cell.Foreground = brush;
            return cell;
        }

        //-----------------------------
        //an icon button on a row, with its word beside the icon in large view
        private Button RowAction(IconKind kind, string word, string spoken)
        {
            var button = new Button
            {
                Style = (Style)FindResource("RowAction"),
                ToolTip = word,
                Content = new Icon { Kind = kind, Size = (double)FindResource("SizeIconSm") },
            };
            WidgetCard.SetWord(button, word);
            AutomationProperties.SetName(button, spoken);
            return button;
        }

        //-----------------------------
        //the table takes the window's width, down to what its columns need; narrower than that, it scrolls
        private void FitTable()
        {
            Table.MinWidth = FixedColumns.Sum() + NoteMinWidth + ActionsWidth;
            Table.Width = Math.Max(TableScroller.ViewportWidth, Table.MinWidth);
        }

        //-----------------------------
        //puts the window where it was left, if that is still on a screen. Gives back whether it was
        private bool PlaceAsLeft(WindowPlace? place)
        {
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (place is not { IsValid: true } || !screen.IntersectsWith(new Rect(place.Left, place.Top, place.Width, place.Height)))
                return false;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = place.Left;
            Top = place.Top;
            Width = place.Width;
            Height = place.Height;
            if (place.Maximised)
                WindowState = WindowState.Maximized;
            return true;
        }

        private void RestartPlaceSaver()
        {
            _placeSaver.Stop();
            _placeSaver.Start();
        }

        //-----------------------------
        //saves the size and place the window goes back to, and whether it is maximised
        private void SavePlace()
        {
            if (!_shown)
                return;

            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (bounds.IsEmpty)
                return;

            _flow.Session.Preferences.TimesheetWindow = new WindowPlace(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                WindowState == WindowState.Maximized);
            try
            {
                _flow.SavePreferences();
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning("The timesheet window's place could not be saved.");
            }
        }

        //-----------------------------
        //a maximised window without the system frame would hang over the screen's edges by the frame's width, so it steps in by that much
        private void FitMaximised()
        {
            var frame = SystemParameters.WindowNonClientFrameThickness;
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(frame.Left, frame.Left, frame.Right, frame.Bottom) : new Thickness(0);
        }

        //-----------------------------
        //the row being edited: its boxes and pickers, Save and Cancel, and under it the question about the entries around it,
        //or what is wrong
        private sealed class RowEditor
        {
            private readonly TimesheetWindow _window;
            private readonly List<Project> _projects;
            private readonly TextBlock _bannerText;
            private readonly TextBlock _errorText;
            private readonly StackPanel _errorLine;
            private readonly Border _banner;
            private bool _ready;

            public RowEditor(TimesheetWindow window, TimesheetSlot? slot, DateTime start, DateTime end, List<Project> projects, ActivityChoices activities)
            {
                _window = window;
                _projects = projects;
                EntryId = slot?.EntryId;

                var row = NewRow();
                //the times keep their whole "10:30 AM" in the narrow columns
                Start = Box(Format.Clock(start), "9:00 AM", "Start time");
                End = Box(Format.Clock(end), "9:30 AM", "End time");
                Start.Padding = End.Padding = new Thickness(8, 0, 8, 0);
                AddCell(row, 0, Start);
                AddCell(row, 1, End);

                //the same pickers as the widget's editor
                Project = new ActivityPicker
                {
                    FieldName = "Project",
                    Placeholder = "Choose a client and project",
                    MaxDepth = 2,
                    RequiredDepth = 2,
                    AllowRootAdd = true,
                    GuidedChildAdd = true,
                    MaxNameLength = InputLimits.CompanyOrProject,
                    CleanName = InputLimits.CleanCompanyOrProjectName,
                    AddLabelFor = path => path.Count == 0 ? "New company…" : $"New project in {path[^1]}…",
                    RootLabel = "All clients",
                    CanRemove = null,
                    Margin = new Thickness(0, 4, 8, 4),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var selectedProject = slot != null && !string.IsNullOrEmpty(slot.Company) ? new[] { slot.Company, slot.Project } : Array.Empty<string>();
                Project.Load(EntryEditView.BuildProjectTree(projects),
                    selectedProject.Length > 0 ? [selectedProject] : [], selectedProject);
                Project.SelectionChanged += async (_, _) => await ProjectChangedAsync();
                AddCell(row, 2, Project);

                Activity = new ActivityPicker { Margin = new Thickness(0, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
                Activity.Load(activities.Tree, activities.Recent, slot?.ActivityPath ?? Array.Empty<string>());
                Activity.SelectionChanged += (_, _) => HideBanner();
                AddCell(row, 3, Activity);

                Note = Box(slot?.Note ?? string.Empty, "Add a note", "Note");
                Note.MaxLength = 500;
                AddCell(row, 4, Note);

                var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Save = new Button { Style = (Style)window.FindResource("Button.Primary"), Content = "Save", Height = (double)window.FindResource("Space32"), Padding = new Thickness(12, 0, 12, 0) };
                Save.Click += async (_, _) => await window.SaveAsync(this);
                var cancel = new Button { Style = (Style)window.FindResource("Button.Secondary"), Content = "Cancel", Height = (double)window.FindResource("Space32"), Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(4, 0, 0, 0) };
                cancel.Click += (_, _) => window.CancelEdit();
                buttons.Children.Add(Save);
                buttons.Children.Add(cancel);
                AddCell(row, 5, buttons);

                //"This changes Code review to 9:30 – 10:30 AM. Continue?" with No and Yes
                _bannerText = new TextBlock { Style = (Style)window.FindResource("Text.Body"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                AutomationProperties.SetLiveSetting(_bannerText, AutomationLiveSetting.Assertive);
                Yes = new Button { Style = (Style)window.FindResource("Button.Primary"), Content = "Yes", Height = (double)window.FindResource("Space32"), Padding = new Thickness(20, 0, 20, 0), Margin = new Thickness(8, 0, 0, 0) };
                AutomationProperties.SetName(Yes, "Yes, change the other entries too");
                Yes.Click += async (_, _) => await window.ConfirmAsync(this);
                var no = new Button { Style = (Style)window.FindResource("Button.Secondary"), Content = "No", Height = (double)window.FindResource("Space32"), Padding = new Thickness(20, 0, 20, 0) };
                AutomationProperties.SetName(no, "No, keep editing");
                no.Click += (_, _) => HideBanner();
                var bannerButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
                bannerButtons.Children.Add(no);
                bannerButtons.Children.Add(Yes);
                DockPanel.SetDock(bannerButtons, Dock.Right);
                var bannerContent = new DockPanel();
                bannerContent.Children.Add(bannerButtons);
                bannerContent.Children.Add(_bannerText);
                _banner = new Border
                {
                    Child = bannerContent,
                    Background = (Brush)window.FindResource("AccentFillBrush"),
                    BorderBrush = (Brush)window.FindResource("BorderSubtleBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = (CornerRadius)window.FindResource("RadiusControl"),
                    Padding = new Thickness(16, 8, 8, 8),
                    Margin = new Thickness(0, 0, 0, 8),
                    Visibility = Visibility.Collapsed,
                };

                //what is wrong: an alert and the reason, never a colour alone
                _errorText = new TextBlock { Style = (Style)window.FindResource("Text.Error"), TextWrapping = TextWrapping.Wrap };
                _errorLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 8), Visibility = Visibility.Collapsed };
                _errorLine.Children.Add(new Icon { Kind = IconKind.Alert, Size = (double)window.FindResource("SizeIconSm"), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Top });
                _errorLine.Children.Add(_errorText);

                Block.Children.Add(window.Underlined(row));
                Block.Children.Add(_errorLine);
                Block.Children.Add(_banner);

                //Enter saves and Esc leaves the row, as on the widget's forms
                Block.PreviewKeyDown += async (_, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        e.Handled = true;
                        window.CancelEdit();
                    }
                    else if (e.Key == Key.Enter && e.OriginalSource is TextBox)
                    {
                        e.Handled = true;
                        await window.SaveAsync(this);
                    }
                };
                _ready = true;
            }

            public int? EntryId { get; }
            public StackPanel Block { get; } = new();
            public TextBox Start { get; }
            public TextBox End { get; }
            public TextBox Note { get; }
            public ActivityPicker Project { get; }
            public ActivityPicker Activity { get; }
            public Button Save { get; }
            public Button Yes { get; }

            // The edit the banner describes: Yes saves exactly this, and any change to the row takes the banner away
            public EntryEdit? Pending { get; private set; }

            public void ShowBanner(EntryEdit edit, IReadOnlyList<NeighbourChange> changes)
            {
                Pending = edit;
                _bannerText.Inlines.Clear();
                foreach (var change in changes)
                {
                    if (_bannerText.Inlines.Count > 0)
                        _bannerText.Inlines.Add(new LineBreak());
                    _bannerText.Inlines.Add(new Run(change.IsRemoval ? "This removes " : "This changes "));
                    _bannerText.Inlines.Add(new Bold(new Run(change.Label)));
                    _bannerText.Inlines.Add(change.IsRemoval
                        ? new Run($" ({Format.Range(change.OldStart, change.OldEnd)}).")
                        : new Run(" to "));
                    if (!change.IsRemoval)
                    {
                        _bannerText.Inlines.Add(new Bold(new Run(Format.Range(change.NewStart!.Value, change.NewEnd!.Value))));
                        _bannerText.Inlines.Add(new Run("."));
                    }
                }
                _bannerText.Inlines.Add(new Run(" Continue?"));
                _banner.Visibility = Visibility.Visible;
            }

            public void ShowError(string message)
            {
                _errorText.Text = message;
                _errorLine.Visibility = Visibility.Visible;
            }

            public void HideMessages()
            {
                HideBanner();
                _errorLine.Visibility = Visibility.Collapsed;
            }

            //No keeps editing with nothing saved; so does any change to the row, so Yes never saves something the banner didn't say
            private void HideBanner()
            {
                if (!_ready)
                    return;
                Pending = null;
                _banner.Visibility = Visibility.Collapsed;
            }

            //-----------------------------
            //a box in the row, the widget's field style
            private TextBox Box(string text, string placeholder, string name)
            {
                var box = new TextBox
                {
                    Style = (Style)_window.FindResource("Input"),
                    Text = text,
                    Margin = new Thickness(0, 4, 8, 4),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Field.SetPlaceholder(box, placeholder);
                AutomationProperties.SetName(box, name);
                box.TextChanged += (_, _) => HideBanner();
                return box;
            }

            //-----------------------------
            //a new client and project offer that project's activities
            private async Task ProjectChangedAsync()
            {
                if (!_ready)
                    return;

                HideBanner();
                var path = Project.SelectedPath;
                var project = path.Count < 2 ? null : _projects.FirstOrDefault(p =>
                    string.Equals(p.Company?.Name, path[0], StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.Name, path[1], StringComparison.OrdinalIgnoreCase));
                var choices = project != null
                    ? await _window._flow.GetActivitiesAsync(project.ProjectID)
                    : await _window._flow.GetActivityTreeAsync();
                Activity.Load(choices.Tree, choices.Recent, Activity.SelectedPath);
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
