using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TimePlanner.Core.Services;
using TimePlanner.Core.Services.Models;
using TimePlanner.Widget.Controls;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    /// <summary>
    /// Settings → Manage lists: add, rename and hide clients, projects and activities. Nothing is
    /// ever deleted, so logged time keeps its names; hidden items can be shown again. Renaming
    /// warns when days already sent use the name, because the dashboard keeps the old one until
    /// the day is sent again.
    /// </summary>
    public partial class ManageListsView : UserControl
    {
        private enum EditMode
        {
            Rename,
            AddChild,
            AddClient,
        }

        /// <summary>The one inline editor that is open: what it edits, and the parts of it that change after it is built.</summary>
        private sealed class Editor(EditMode mode, ListKind kind, int id, string name)
        {
            public EditMode Mode { get; } = mode;
            public ListKind Kind { get; } = kind;
            public int Id { get; } = id;
            public string Name { get; } = name;
            public TextBox? First { get; set; }
            public TextBox? Second { get; set; }
            public DockPanel? WarningLine { get; set; }
            public TextBlock? WarningText { get; set; }
            public DockPanel? ErrorLine { get; set; }
            public TextBlock? ErrorText { get; set; }
            public IReadOnlyList<DateOnly>? SentDays { get; set; }
        }

        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        private readonly WidgetFlow _flow;
        private ManagedLists _lists;
        private Editor? _editor;
        private bool _showHidden;
        private bool _focusHiddenHeader;
        private bool _busy;
        private (ListKind Kind, int Id)? _focusAfter;
        private Func<Task>? _undo;
        private DispatcherTimer? _noticeTimer;

        public ManageListsView(WidgetFlow flow, ManagedLists lists)
        {
            InitializeComponent();
            _flow = flow;
            _lists = lists;
            Layout.Height = Math.Clamp(SystemParameters.WorkArea.Height - 160, 360, 560);
            Render();
        }

        private bool OnProjects => ProjectsTab.IsChecked == true;

        /// <summary>Opens a tab with the hidden items shown, or adding under an item. The screen previews use it.</summary>
        public void Open(bool activities = false, bool showHidden = false, string? addingUnder = null)
        {
            _showHidden = showHidden;
            (activities ? ActivitiesTab : ProjectsTab).IsChecked = true;
            if (addingUnder != null)
            {
                var id = activities
                    ? FindActivityByName(_lists.Activities, addingUnder)?.Id
                    : _lists.Clients.FirstOrDefault(c => Same(c.Name, addingUnder))?.Id;
                if (id is { } found)
                    _editor = new Editor(EditMode.AddChild, activities ? ListKind.Activity : ListKind.Client, found, addingUnder);
            }
            Render();
        }

        private static ManagedActivity? FindActivityByName(IEnumerable<ManagedActivity> nodes, string name) =>
            nodes.Select(n => Same(n.Name, name) ? n : FindActivityByName(n.Children, name)).FirstOrDefault(n => n != null);

        //-----------------------------
        //navigation and keys
        private void Back_Click(object sender, RoutedEventArgs e) => _flow.ShowSettings();

        private void View_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            if (_editor != null)
                CloseEditor();
            else
                _flow.ShowSettings();
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            // Checked fires while the XAML is still loading, before the list exists
            if (Rows == null)
                return;
            _editor = null;
            Render();
        }

        private void AddClient_Click(object sender, RoutedEventArgs e)
        {
            _editor = new Editor(EditMode.AddClient, ListKind.Client, 0, "");
            Render();
            ListScroll.ScrollToTop();
        }

        //-----------------------------
        //builds every row from the lists and the open editor
        private void Render()
        {
            Rows.Children.Clear();
            Footer.Visibility = OnProjects ? Visibility.Visible : Visibility.Collapsed;

            if (_editor?.Mode == EditMode.AddClient)
                Rows.Children.Add(BuildEditor(_editor, depth: 1));

            if (OnProjects)
                RenderProjects();
            else
                RenderActivities();

            RenderHidden();
            RestoreFocus();
        }

        private void RenderProjects()
        {
            foreach (var client in _lists.Clients.Where(c => !c.Hidden))
            {
                var group = new StackPanel();
                group.Children.Add(Item(ListKind.Client, client.Id, client.Name, depth: 1, client.Locked,
                    addLabel: $"Add a project to {client.Name}"));

                foreach (var project in client.Projects.Where(p => !p.Hidden))
                    group.Children.Add(Item(ListKind.Project, project.Id, project.Name, depth: 2, project.Locked));

                if (_editor is { Mode: EditMode.AddChild, Kind: ListKind.Client } add && add.Id == client.Id)
                    group.Children.Add(BuildEditor(add, depth: 2));

                AddGroup(group);
            }
        }

        private void RenderActivities()
        {
            Rows.Children.Add(new TextBlock
            {
                Style = Res<Style>("Text.Caption"),
                Foreground = Res<Brush>("TextSecondaryBrush"),
                Margin = new Thickness(0, 0, 0, 4),
                Text = "Main activities are shared by everyone, so they stay as they are. Add your own under them.",
            });

            foreach (var root in _lists.Activities)
            {
                var group = new StackPanel();
                AddActivityRows(group, root);
                AddGroup(group);
            }
        }

        private void AddActivityRows(Panel group, ManagedActivity node)
        {
            group.Children.Add(Item(ListKind.Activity, node.Id, node.Name, node.Depth, node.Locked,
                addLabel: node.Depth < ActivityService.MaxDepth ? $"Add an activity under {node.Name}" : null));

            if (_editor is { Mode: EditMode.AddChild, Kind: ListKind.Activity } add && add.Id == node.Id)
                group.Children.Add(BuildEditor(add, node.Depth + 1));

            foreach (var child in node.Children.Where(c => !c.Hidden))
                AddActivityRows(group, child);
        }

        //-----------------------------
        //hidden items, folded away under their own header until asked for
        private void RenderHidden()
        {
            var hidden = OnProjects ? HiddenProjects() : HiddenActivities(_lists.Activities, []).ToList();
            if (hidden.Count == 0)
                return;

            var group = new StackPanel();
            var header = new Button { Style = Res<Style>("RowButton"), Content = HiddenHeader(hidden.Count) };
            AutomationProperties.SetName(header, _showHidden ? "Hidden items, expanded" : $"{hidden.Count} hidden items, collapsed");
            header.Click += (_, _) =>
            {
                _showHidden = !_showHidden;
                _focusHiddenHeader = true;
                Render();
            };
            group.Children.Add(header);

            if (_showHidden)
            {
                group.Children.Add(new TextBlock
                {
                    Style = Res<Style>("Text.Caption"),
                    Foreground = Res<Brush>("TextSecondaryBrush"),
                    Margin = new Thickness(0, 0, 0, 8),
                    Text = "Hidden items keep their logged time. Show one to use it again.",
                });

                foreach (var (kind, id, name, where) in hidden)
                    group.Children.Add(HiddenRow(kind, id, name, where));
            }

            AddGroup(group);
            if (_focusHiddenHeader)
            {
                _focusHiddenHeader = false;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => header.Focus());
            }
        }

        // "Hidden", a count badge, and a chevron that points up while the list is open
        private DockPanel HiddenHeader(int count)
        {
            var content = new DockPanel();
            var chevron = new Icon
            {
                Kind = IconKind.ChevronDown,
                Size = Res<double>("SizeIconSm"),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res<Brush>("TextSecondaryBrush"),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(_showHidden ? 180 : 0),
            };
            DockPanel.SetDock(chevron, Dock.Right);
            content.Children.Add(chevron);

            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock
            {
                Text = "Hidden",
                Style = Res<Style>("Text.Body"),
                FontWeight = Res<FontWeight>("FontWeightSemibold"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            // A circle for one digit and round ends for more: the radius is half its height, as a larger one would draw an oval
            label.Children.Add(new Border
            {
                Background = Res<Brush>("SurfaceSubtleBrush"),
                CornerRadius = new CornerRadius(Res<double>("SizeIcon") / 2),
                MinWidth = Res<double>("SizeIcon"),
                Height = Res<double>("SizeIcon"),
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = count.ToString(Culture),
                    Style = Res<Style>("Text.Caption"),
                    Foreground = Res<Brush>("TextSecondaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
            content.Children.Add(label);
            return content;
        }

        private List<(ListKind Kind, int Id, string Name, string Where)> HiddenProjects()
        {
            var hidden = new List<(ListKind, int, string, string)>();
            foreach (var client in _lists.Clients)
            {
                if (client.Hidden)
                    hidden.Add((ListKind.Client, client.Id, client.Name, client.Projects.Count == 1 ? "Client · 1 project" : $"Client · {client.Projects.Count} projects"));
                else
                    hidden.AddRange(client.Projects.Where(p => p.Hidden).Select(p => (ListKind.Project, p.Id, p.Name, client.Name)));
            }
            return hidden;
        }

        // Only the highest hidden level is listed: showing it brings back what was under it
        private static IEnumerable<(ListKind Kind, int Id, string Name, string Where)> HiddenActivities(IEnumerable<ManagedActivity> nodes, IReadOnlyList<string> parents)
        {
            foreach (var node in nodes)
            {
                if (node.Hidden)
                {
                    yield return (ListKind.Activity, node.Id, node.Name, string.Join(" › ", parents));
                    continue;
                }
                foreach (var below in HiddenActivities(node.Children, [.. parents, node.Name]))
                    yield return below;
            }
        }

        //-----------------------------
        //row builders

        // Groups sit under a hairline, like the tab strip above them
        private void AddGroup(StackPanel group)
        {
            if (Rows.Children.OfType<StackPanel>().Any())
            {
                Rows.Children.Add(new Border
                {
                    Height = Res<double>("Hairline"),
                    Background = Res<Brush>("BorderSubtleBrush"),
                    Margin = new Thickness(0, 8, 0, 8),
                });
            }
            Rows.Children.Add(group);
        }

        /// <summary>One client, project or activity: its name, then + (where things can go under it) and rename.</summary>
        private FrameworkElement Item(ListKind kind, int id, string name, int depth, bool locked, string? addLabel = null)
        {
            if (_editor is { Mode: EditMode.Rename } rename && rename.Kind == kind && rename.Id == id)
                return BuildEditor(rename, depth);

            var row = new DockPanel
            {
                MinHeight = Res<double>("SizeControl"),
                Margin = new Thickness(Indent(depth), 0, 0, 0),
                Background = Brushes.Transparent,
            };

            if (locked)
            {
                var lockIcon = new Icon
                {
                    Kind = IconKind.Lock,
                    Size = Res<double>("SizeIconSm"),
                    Width = Res<double>("Space32"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Res<Brush>("TextDisabledBrush"),
                    ToolTip = kind == ListKind.Activity ? "Main activities stay as they are" : "Internal work stays as it is",
                };
                DockPanel.SetDock(lockIcon, Dock.Right);
                row.Children.Add(lockIcon);
            }
            else
            {
                var edit = IconButton(IconKind.Edit, "Edit", "Rename or hide", $"Rename or hide {name}", () => OpenRename(kind, id, name));
                edit.Tag = (kind, id);
                DockPanel.SetDock(edit, Dock.Right);
                row.Children.Add(edit);
            }

            if (addLabel != null)
            {
                var add = IconButton(IconKind.Plus, "Add", addLabel, addLabel, () =>
                {
                    _editor = new Editor(EditMode.AddChild, kind, id, name);
                    Render();
                });
                add.Margin = new Thickness(0, 0, 4, 0);
                DockPanel.SetDock(add, Dock.Right);
                row.Children.Add(add);

                // Under a group, + shows only for the row being pointed at or tabbed to, so the list stays calm
                if (depth > 1)
                {
                    add.Opacity = 0;
                    void Reveal() => add.Opacity = row.IsMouseOver || row.IsKeyboardFocusWithin ? 1 : 0;
                    row.MouseEnter += (_, _) => Reveal();
                    row.MouseLeave += (_, _) => Reveal();
                    row.IsKeyboardFocusWithinChanged += (_, _) => Reveal();
                }
            }

            row.Children.Add(new TextBlock
            {
                Text = name,
                Style = Res<Style>("Text.Body"),
                FontWeight = Res<FontWeight>(depth == 1 ? "FontWeightSemibold" : "FontWeightRegular"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = name,
            });
            return row;
        }

        private DockPanel HiddenRow(ListKind kind, int id, string name, string where)
        {
            var row = new DockPanel { MinHeight = Res<double>("SizeControl"), Margin = new Thickness(0, 0, 0, 4) };

            var show = new Button
            {
                Style = Res<Style>("Button.Secondary"),
                Content = "Show",
                Height = Res<double>("Space32"),
                Padding = new Thickness(12, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(show, $"Show {name} again");
            show.Click += async (_, _) => await ChangeAsync(() => _flow.SetListItemHiddenAsync(kind, id, false), focus: (kind, id),
                undo: () => _flow.SetListItemHiddenAsync(kind, id, true));
            DockPanel.SetDock(show, Dock.Right);
            row.Children.Add(show);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            text.Children.Add(new TextBlock
            {
                Text = name,
                Style = Res<Style>("Text.Body"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (where.Length > 0)
            {
                text.Children.Add(new TextBlock
                {
                    Text = where,
                    Style = Res<Style>("Text.Caption"),
                    Foreground = Res<Brush>("TextAccentBrush"),
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
            row.Children.Add(text);
            return row;
        }

        // The word shows beside the icon in large view
        private Button IconButton(IconKind kind, string word, string tip, string spoken, Action click)
        {
            var button = new Button
            {
                Style = Res<Style>("RowIconButton"),
                ToolTip = tip,
                Content = new Icon { Kind = kind, Size = Res<double>("SizeIconSm") },
            };
            WidgetCard.SetWord(button, word);
            AutomationProperties.SetName(button, spoken);
            button.Click += (_, _) => click();
            return button;
        }

        // Each level steps in by the small icon size, so a three-level path still leaves room for names
        private double Indent(int depth) => (depth - 1) * Res<double>("SizeIconSm");

        //-----------------------------
        //the inline editor: rename (with hide), add under an item, or add a client
        private void OpenRename(ListKind kind, int id, string name)
        {
            var editor = new Editor(EditMode.Rename, kind, id, name);
            _editor = editor;
            Render();
            _ = LoadSentDaysAsync(editor);
        }

        private async Task LoadSentDaysAsync(Editor editor)
        {
            var days = await _flow.GetSentDaysUsingAsync(editor.Kind, editor.Id);
            editor.SentDays = days;
            if (_editor != editor || days.Count == 0 || editor.WarningLine == null || editor.WarningText == null)
                return;
            editor.WarningText.Text = SentDaysWarning(days);
            editor.WarningLine.Visibility = Visibility.Visible;
        }

        private static string SentDaysWarning(IReadOnlyList<DateOnly> days)
        {
            var shown = string.Join(", ", days.Take(3).Select(d => d.ToString("MMM d", Culture)));
            var more = days.Count > 3 ? $" and {days.Count - 3} more" : "";
            return days.Count == 1
                ? $"Used on a day you already sent ({shown}). The dashboard keeps the old name until you send that day again."
                : $"Used on {days.Count} days you already sent ({shown}{more}). The dashboard keeps the old name until you send them again.";
        }

        private Border BuildEditor(Editor editor, int depth)
        {
            var panel = new StackPanel();
            var border = new Border
            {
                Style = Res<Style>("Stat"),
                Padding = new Thickness(12),
                Margin = new Thickness(Indent(depth), 4, 0, 4),
                Child = panel,
            };

            var (heading, placeholder, max) = editor.Mode switch
            {
                EditMode.Rename => ($"Rename {editor.Name}", editor.Name, MaxLength(editor.Kind)),
                EditMode.AddClient => ("New client", "Client name", InputLimits.CompanyOrProject),
                _ when editor.Kind == ListKind.Client => ($"New project in {editor.Name}", "Project name", InputLimits.CompanyOrProject),
                _ => ($"New activity under {editor.Name}", "Activity name", InputLimits.ActivityName),
            };

            editor.First = AddField(panel, heading, placeholder, max, editor.Mode == EditMode.Rename ? editor.Name : "");
            if (editor.Mode == EditMode.AddClient)
                editor.Second = AddField(panel, "First project", "Project name", InputLimits.CompanyOrProject, "", topGap: true);

            (editor.WarningLine, editor.WarningText) = AlertLine(panel, "Text.Help");
            (editor.ErrorLine, editor.ErrorText) = AlertLine(panel, "Text.Error");
            if (editor.SentDays is { Count: > 0 } days)
            {
                editor.WarningText.Text = SentDaysWarning(days);
                editor.WarningLine.Visibility = Visibility.Visible;
            }

            var actions = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
            var save = SmallButton("Button.Primary", editor.Mode == EditMode.Rename ? "Save" : "Add");
            save.Margin = new Thickness(8, 0, 0, 0);
            save.Click += async (_, _) => await SaveAsync(editor);
            var cancel = SmallButton("Button.Secondary", "Cancel");
            cancel.Click += (_, _) => CloseEditor();
            DockPanel.SetDock(save, Dock.Right);
            DockPanel.SetDock(cancel, Dock.Right);
            actions.Children.Add(save);
            actions.Children.Add(cancel);

            if (editor.Mode == EditMode.Rename)
            {
                var hide = SmallButton("Button.Secondary", "Hide");
                AutomationProperties.SetName(hide, $"Hide {editor.Name}");
                hide.ToolTip = "Takes it out of the pickers. Logged time keeps its name.";
                hide.Click += async (_, _) => await HideAsync(editor);
                DockPanel.SetDock(hide, Dock.Left);
                actions.Children.Add(hide);
            }
            panel.Children.Add(actions);

            foreach (var box in new[] { editor.First, editor.Second }.OfType<TextBox>())
            {
                box.KeyDown += async (_, e) =>
                {
                    if (e.Key != Key.Enter)
                        return;
                    e.Handled = true;
                    await SaveAsync(editor);
                };
                box.TextChanged += (_, _) => ShowError(editor, null);
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (_editor != editor || editor.First == null)
                    return;
                editor.First.Focus();
                editor.First.SelectAll();
                border.BringIntoView();
            });
            return border;
        }

        private Button SmallButton(string style, string text) => new()
        {
            Style = Res<Style>(style),
            Content = text,
            Height = Res<double>("Space32"),
            Padding = new Thickness(12, 0, 12, 0),
        };

        private TextBox AddField(Panel panel, string label, string placeholder, int max, string text, bool topGap = false)
        {
            var labelBlock = new TextBlock
            {
                Style = Res<Style>("Text.Caption"),
                FontWeight = Res<FontWeight>("FontWeightSemibold"),
                Foreground = Res<Brush>("TextSecondaryBrush"),
                Margin = new Thickness(0, topGap ? 12 : 0, 0, 8),
                Text = label,
            };
            var box = new TextBox { Style = Res<Style>("Input"), MaxLength = max, Text = text };
            Field.SetPlaceholder(box, placeholder);
            AutomationProperties.SetName(box, label);
            panel.Children.Add(labelBlock);
            panel.Children.Add(box);
            return box;
        }

        // Icon and text, never a colour change alone
        private (DockPanel Line, TextBlock Text) AlertLine(Panel panel, string style)
        {
            var line = new DockPanel { Margin = Res<Thickness>("GapControlToHelp"), Visibility = Visibility.Collapsed };
            var icon = new Icon
            {
                Kind = IconKind.Alert,
                Size = Res<double>("SizeIconSm"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 4, 0),
            };
            DockPanel.SetDock(icon, Dock.Left);
            var text = new TextBlock { Style = Res<Style>(style), Margin = new Thickness(0) };
            line.Children.Add(icon);
            line.Children.Add(text);
            panel.Children.Add(line);
            return (line, text);
        }

        private static void ShowError(Editor editor, string? message)
        {
            if (editor.ErrorLine == null || editor.ErrorText == null)
                return;
            editor.ErrorText.Text = message ?? "";
            editor.ErrorLine.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
            if (editor.First != null)
                Field.SetIsInvalid(editor.First, message != null);
        }

        private void CloseEditor()
        {
            var closed = _editor;
            _editor = null;
            if (closed is { Mode: not EditMode.AddClient })
                _focusAfter = (closed.Kind, closed.Id);
            Render();
        }

        //-----------------------------
        //saving
        private async Task SaveAsync(Editor editor)
        {
            if (_busy || _editor != editor || editor.First == null)
                return;

            var isActivity = editor.Kind == ListKind.Activity;
            var name = Clean(editor.First.Text, isActivity);
            if (name.Length == 0)
            {
                ShowError(editor, "Type a name first.");
                return;
            }

            switch (editor.Mode)
            {
                case EditMode.Rename when name == editor.Name:
                    CloseEditor();
                    return;

                case EditMode.Rename:
                    var sent = editor.SentDays ?? await _flow.GetSentDaysUsingAsync(editor.Kind, editor.Id);
                    var resend = sent.Count == 0 ? "" : sent.Count == 1 ? " Send that day again." : $" Send those {sent.Count} days again.";
                    await ChangeAsync(() => _flow.RenameListItemAsync(editor.Kind, editor.Id, name), editor, (editor.Kind, editor.Id), suffix: resend);
                    return;

                case EditMode.AddClient:
                    var project = Clean(editor.Second?.Text ?? "", activity: false);
                    if (_lists.Clients.FirstOrDefault(c => !c.Hidden && Same(c.Name, name)) is { } existing)
                    {
                        ShowError(editor, $"{existing.Name} is already in the list. Use + beside it to add a project.");
                        return;
                    }
                    if (project.Length == 0)
                    {
                        ShowError(editor, "Add the client's first project too.");
                        editor.Second?.Focus();
                        return;
                    }
                    await ChangeAsync(() => _flow.AddProjectAsync(name, project), editor);
                    return;

                case EditMode.AddChild when editor.Kind == ListKind.Client:
                    var client = _lists.Clients.First(c => c.Id == editor.Id);
                    if (client.Projects.FirstOrDefault(p => !p.Hidden && Same(p.Name, name)) is { } taken)
                    {
                        ShowError(editor, $"{client.Name} already has {taken.Name}.");
                        return;
                    }
                    await ChangeAsync(() => _flow.AddProjectAsync(client.Name, name), editor,
                        suffix: client.Projects.Any(p => p.Hidden && Same(p.Name, name)) ? " It was hidden, so it's back." : "");
                    return;

                default:
                    var parent = FindActivity(_lists.Activities, editor.Id);
                    if (parent?.Children.FirstOrDefault(c => !c.Hidden && Same(c.Name, name)) is { } there)
                    {
                        ShowError(editor, $"{there.Name} is already under {parent.Name}.");
                        return;
                    }
                    await ChangeAsync(() => _flow.AddActivityAsync(editor.Id, name), editor,
                        suffix: parent?.Children.Any(c => c.Hidden && Same(c.Name, name)) == true ? " It was hidden, so it's back." : "");
                    return;
            }
        }

        private async Task HideAsync(Editor editor)
        {
            var (kind, id) = (editor.Kind, editor.Id);
            await ChangeAsync(() => _flow.SetListItemHiddenAsync(kind, id, true), editor,
                undo: () => _flow.SetListItemHiddenAsync(kind, id, false));
        }

        /// <summary>Runs one change, then reloads the lists and says what happened. A refusal stays in the open editor.</summary>
        private async Task ChangeAsync(Func<Task<ListChangeOutcome>> change, Editor? editor = null, (ListKind, int)? focus = null,
            Func<Task<ListChangeOutcome>>? undo = null, string suffix = "")
        {
            if (_busy)
                return;
            _busy = true;
            IsEnabled = false;
            try
            {
                var outcome = await change();
                if (!outcome.Ok)
                {
                    if (editor != null)
                        ShowError(editor, outcome.Message);
                    else
                        ShowNotice(outcome.Message, null);
                    return;
                }

                _editor = null;
                _focusAfter = focus;
                _lists = await _flow.GetManagedListsAsync();
                Render();
                ShowNotice(outcome.Message + suffix, undo);
            }
            finally
            {
                IsEnabled = true;
                _busy = false;
            }
        }

        //-----------------------------
        //the notice bar, with Undo for hide and show
        private void ShowNotice(string message, Func<Task<ListChangeOutcome>>? undo)
        {
            _noticeTimer?.Stop();
            NoticeText.Text = message;
            NoticeText.ToolTip = message;
            _undo = undo == null ? null : async () => await ChangeAsync(undo);
            UndoButton.Visibility = undo == null ? Visibility.Collapsed : Visibility.Visible;
            NoticeBar.Visibility = Visibility.Visible;

            _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _noticeTimer.Tick += (_, _) =>
            {
                _noticeTimer?.Stop();
                NoticeBar.Visibility = Visibility.Collapsed;
                _undo = null;
            };
            _noticeTimer.Start();
        }

        private async void Undo_Click(object sender, RoutedEventArgs e)
        {
            var undo = _undo;
            _undo = null;
            _noticeTimer?.Stop();
            NoticeBar.Visibility = Visibility.Collapsed;
            if (undo != null)
                await undo();
        }

        //-----------------------------
        //helpers
        private void RestoreFocus()
        {
            if (_focusAfter is not { } key)
                return;
            _focusAfter = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                var target = FindButton(Rows, key);
                if (target != null)
                {
                    target.Focus();
                    target.BringIntoView();
                }
            });
        }

        private static Button? FindButton(DependencyObject parent, (ListKind, int) key)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button { Tag: ValueTuple<ListKind, int> tag } button && tag == key)
                    return button;
                if (FindButton(child, key) is { } found)
                    return found;
            }
            return null;
        }

        private static ManagedActivity? FindActivity(IEnumerable<ManagedActivity> nodes, int id)
        {
            foreach (var node in nodes)
            {
                if (node.Id == id)
                    return node;
                if (FindActivity(node.Children, id) is { } found)
                    return found;
            }
            return null;
        }

        private static string Clean(string typed, bool activity) =>
            activity ? InputLimits.CleanActivityName(typed) : InputLimits.CleanCompanyOrProjectName(typed);

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string Noun(ListKind kind) => kind switch
        {
            ListKind.Client => "client",
            ListKind.Project => "project",
            _ => "activity",
        };

        private static int MaxLength(ListKind kind) => kind == ListKind.Activity ? InputLimits.ActivityName : InputLimits.CompanyOrProject;

        private T Res<T>(string key) => (T)FindResource(key);
    }
}
