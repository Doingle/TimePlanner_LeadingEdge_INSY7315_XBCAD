using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TimePlanner.Core.Services;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Controls
{
    //-----------------------------
    //breadcrumb tree picker for activities companies and projects
    //the dropdown shows one level at a time: a branch opens in place and its header goes back up
    public partial class ActivityPicker : UserControl
    {
        private const string Chevron = "›";
        private const int RecentLimit = 3;

        private static readonly BooleanToVisibilityConverter ShowWhenTrue = new();

        private List<ActivityNode> _tree = [];
        private List<IReadOnlyList<string>> _recent = [];
        private IReadOnlyList<string> _selected = [];
        private IReadOnlyList<string> _level = [];
        private ContextMenu? _menu;
        private IReadOnlyList<string>? _addPath;
        private MenuItem? _addField;
        private TextBox? _addInput;
        private IReadOnlyList<string>? _renamePath;
        private bool _renaming;
        private bool _renameSaving;

        public ActivityPicker()
        {
            InitializeComponent();
            RenderValue();
            Unloaded += (_, _) =>
            {
                //closes open menu when control unloads
                if (_menu is { IsOpen: true })
                {
                    _menu.IsOpen = false;
                }
            };
        }

        //-----------------------------
        //what one item is called in messages
        public string FieldName { get; set; } = "Activity";

        //-----------------------------
        //text shown when nothing is chosen
        public string Placeholder { get; set; } = "Choose an activity";

        //-----------------------------
        //label over the top level when recent choices are shown above it
        public string RootLabel { get; set; } = "All activities";

        //-----------------------------
        //deepest level the picker shows
        public int MaxDepth { get; set; } = ActivityService.MaxDepth;

        //-----------------------------
        //how deep a choice must be to count
        public int RequiredDepth { get; set; } = 1;

        //-----------------------------
        //whether new top level items may be typed
        public bool AllowRootAdd { get; set; }

        //-----------------------------
        //opens the child add field after adding a top level item
        public bool GuidedChildAdd { get; set; }

        //-----------------------------
        //longest name the add field accepts
        public int MaxNameLength { get; set; } = InputLimits.ActivityName;

        //-----------------------------
        //tidies typed names before adding
        public Func<string, string> CleanName { get; set; } = InputLimits.CleanActivityName;

        //-----------------------------
        //label for the add row at a path
        public Func<IReadOnlyList<string>, string>? AddLabelFor { get; set; }

        //-----------------------------
        //which rows show a remove button
        public Func<IReadOnlyList<string>, bool>? CanRemove { get; set; }

        //-----------------------------
        //raised when the user asks to remove a row
        public event EventHandler<IReadOnlyList<string>>? RemoveRequested;

        //-----------------------------
        //which rows show a rename button
        public Func<IReadOnlyList<string>, bool>? CanRename { get; set; }

        //-----------------------------
        //saves a new name for the row at a path, giving back why it was refused or null when it was saved
        public Func<IReadOnlyList<string>, string, Task<string?>>? RenameAsync { get; set; }

        //-----------------------------
        //what to warn about before renaming the row at a path, or null
        public Func<IReadOnlyList<string>, Task<string?>>? RenameWarningAsync { get; set; }

        public event EventHandler? SelectionChanged;

        public IReadOnlyList<string> SelectedPath => _selected;

        //-----------------------------
        //loads tree choices recent paths and selected item
        public void Load(List<ActivityNode> tree, List<IReadOnlyList<string>> recent, IReadOnlyList<string> selected)
        {
            _tree = tree;
            _recent = recent;
            _selected = selected;
            RenderValue();
        }

        //-----------------------------
        //checks selection meets required depth
        public bool Validate()
        {
            //selection must reach required depth
            if (_selected.Count >= RequiredDepth)
                return true;

            Field.SetIsInvalid(PickerButton, true);
            var detail = Placeholder.Replace("Choose ", string.Empty).ToLowerInvariant();
            ErrorText.Text = $"Choose {detail} to save this entry.";
            ErrorLine.Visibility = Visibility.Visible;
            AutomationProperties.SetHelpText(PickerButton, $"Choose {detail} to save this entry.");
            return false;
        }

        //-----------------------------
        //moves keyboard focus to the select button
        public void FocusField() => PickerButton.Focus();

        //-----------------------------
        //opens the dropdown at the top, at a level, or adding at a level
        public void Open(IReadOnlyList<string>? expand = null, IReadOnlyList<string>? addingAt = null)
        {
            _addPath = addingAt;
            _level = addingAt ?? expand ?? [];

            //a level that no longer exists opens at the top
            if (_level.Count > 0 && NodeAt(_level) == null)
                _level = [];

            _menu = new ContextMenu { Style = Res<Style>("Menu.Dropdown"), PlacementTarget = PickerButton };
            AutomationProperties.SetName(_menu, FieldName);
            foreach (var item in BuildLevel(_level))
                _menu.Items.Add(item);
            _menu.Closed += Menu_Closed;
            Field.SetIsOpen(PickerButton, true);
            _menu.IsOpen = true;

            //focuses input box when adding
            if (_addInput != null)
                FocusLater(_addInput);
        }

        //-----------------------------
        //this method handles click on the picker button
        private void PickerButton_Click(object sender, RoutedEventArgs e)
        {
            //toggles open menu closed
            if (_menu is { IsOpen: true })
            {
                _menu.IsOpen = false;
                return;
            }
            Open();
        }

        //-----------------------------
        //renders the chosen path as breadcrumbs
        private void RenderValue()
        {
            PathPanel.Children.Clear();
            //unselected state shows placeholder
            if (_selected.Count == 0)
            {
                PathPanel.Children.Add(Text(Placeholder, Res<double>("FontSizeBody"), Res<Brush>("TextTertiaryBrush")));
                PickerButton.ToolTip = null;
                AutomationProperties.SetName(PickerButton, $"{FieldName}, {Placeholder}");
                return;
            }

            //renders each segment of chosen path
            for (var i = 0; i < _selected.Count; i++)
            {
                var last = i == _selected.Count - 1;
                //inserts chevron between path parts
                if (i > 0)
                {
                    var sep = Text(Chevron, Res<double>("FontSizeCaption"), Res<Brush>("TextTertiaryBrush"));
                    BreadcrumbPanel.SetShrink(sep, 0);
                    PathPanel.Children.Add(sep);
                }

                var part = last
                    ? Text(_selected[i], Res<double>("FontSizeBody"), Res<Brush>("TextPrimaryBrush"))
                    : Text(_selected[i], Res<double>("FontSizeCaption"), Res<Brush>("TextSecondaryBrush"));
                BreadcrumbPanel.SetShrink(part, i == 0 || last ? 1 : 999);
                PathPanel.Children.Add(part);
            }

            var spoken = string.Join($" {Chevron} ", _selected);
            PickerButton.ToolTip = spoken;
            AutomationProperties.SetName(PickerButton, $"{FieldName}, " + spoken);
        }

        //-----------------------------
        //the rows of one level: recent and the top level, or a back header and the children
        private List<object> BuildLevel(IReadOnlyList<string> path)
        {
            _addField = null;
            _addInput = null;
            var items = new List<object>();
            var nodes = path.Count == 0 ? _tree : NodeAt(path)?.Children ?? [];

            if (path.Count == 0)
            {
                //adds recent section when recent choices exist
                if (_recent.Count > 0)
                {
                    items.Add(new Separator { Style = Res<Style>("Menu.Label"), Tag = "Recent" });
                    //adds recent rows
                    foreach (var recent in _recent)
                        items.Add(RecentRow(recent));
                    items.Add(new Separator { Style = Res<Style>("Menu.Separator") });
                    items.Add(new Separator { Style = Res<Style>("Menu.Label"), Tag = RootLabel });
                }
            }
            else
            {
                items.Add(BackRow(path));
                items.Add(new Separator { Style = Res<Style>("Menu.Separator") });

                //the branch itself can still be chosen when it is deep enough to count
                if (path.Count >= RequiredDepth)
                    items.Add(OnlyRow(path));
            }

            foreach (var node in nodes)
                items.Add(NodeRow([.. path, node.Label], node));

            //top level offers an add row only when allowed, the deepest level never
            if ((path.Count == 0 && !AllowRootAdd) || path.Count >= MaxDepth)
                return items;

            //separates items from add row
            if (nodes.Count > 0)
                items.Add(new Separator { Style = Res<Style>("Menu.Separator") });

            items.Add(_addPath != null && Same(_addPath, path) ? AddField(path) : AddRow(path));
            return items;
        }

        //-----------------------------
        //swaps the open dropdown to another level, keeping focus inside it
        private void ShowLevel(IReadOnlyList<string> path, IReadOnlyList<string>? focusPath = null)
        {
            if (_menu == null)
                return;

            _level = path;
            _renamePath = null;
            _renaming = false;
            var items = BuildLevel(path);

            //focus lands on the row asked for, the add field, or the first row
            var target = (UIElement?)_addInput
                ?? items.OfType<MenuItem>().FirstOrDefault(i => focusPath != null && i.Tag is IReadOnlyList<string> p && Same(p, focusPath))
                ?? items.OfType<MenuItem>().FirstOrDefault(i => i.Tag is IReadOnlyList<string> p && p.Count > path.Count)
                ?? items.OfType<MenuItem>().FirstOrDefault();
            ReplaceAll(_menu, items, target);
        }

        //-----------------------------
        //header of an opened branch that goes back up a level
        private MenuItem BackRow(IReadOnlyList<string> path)
        {
            var header = new DockPanel();
            var icon = new Icon { Kind = IconKind.ChevronLeft, Size = Res<double>("SizeIconSm"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            header.Children.Add(icon);
            var label = Label(path[^1]);
            label.FontWeight = Res<FontWeight>("FontWeightSemibold");
            header.Children.Add(label);

            var row = new MenuItem { Style = Res<Style>("Picker.Row"), Header = header };
            AutomationProperties.SetName(row, path.Count == 1 ? "Back to all" : $"Back to {path[^2]}");
            row.PreviewMouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                GoBack();
            };
            row.PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Left or Key.Back && ReferenceEquals(e.OriginalSource, row))
                {
                    e.Handled = true;
                    GoBack();
                }
            };
            return row;
        }

        //-----------------------------
        //goes up one level to the branch that was open
        private void GoBack()
        {
            if (_level.Count == 0)
                return;
            var from = _level;
            _addPath = null;
            ShowLevel(from.Take(from.Count - 1).ToList(), focusPath: from);
        }

        //-----------------------------
        //creates a row for one item: its name, then rename and remove while pointed at, then a check or a chevron
        private MenuItem NodeRow(IReadOnlyList<string> nodePath, ActivityNode node)
        {
            var branch = IsBranch(nodePath, node);
            var header = new DockPanel();

            //a branch shows where it goes, the chosen item shows a check
            var marker = branch
                ? new Icon { Kind = IconKind.ChevronRight, Foreground = Res<Brush>("TextSecondaryBrush") }
                : Same(nodePath, _selected) ? new Icon { Kind = IconKind.Check, Foreground = Res<Brush>("TextAccentBrush") } : null;
            if (marker != null)
            {
                marker.Size = Res<double>("SizeIconSm");
                marker.VerticalAlignment = VerticalAlignment.Center;
                marker.Margin = new Thickness(8, 0, 4, 0);
                DockPanel.SetDock(marker, Dock.Right);
                header.Children.Add(marker);
            }

            var row = new MenuItem { Style = Res<Style>("Picker.Row"), Header = header, Tag = nodePath };

            //rename and remove show only on the row being pointed at or moved to
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.SetBinding(VisibilityProperty, new Binding(nameof(MenuItem.IsHighlighted)) { Source = row, Converter = ShowWhenTrue });
            if (CanRename?.Invoke(nodePath) == true)
                actions.Children.Add(RowAction(IconKind.Edit, "Rename", $"Rename {node.Label}", () => BeginRenaming(row, nodePath)));
            if (CanRemove?.Invoke(nodePath) == true)
                actions.Children.Add(RowAction(IconKind.Close, "Remove", $"Remove {node.Label}", () => RequestRemove(nodePath)));
            if (actions.Children.Count > 0)
            {
                DockPanel.SetDock(actions, Dock.Right);
                header.Children.Add(actions);
            }

            header.Children.Add(Label(node.Label));

            var spoken = node.Label + (branch ? ", has more" : "") + (CanRename?.Invoke(nodePath) == true ? ", F2 to rename" : "");
            AutomationProperties.SetName(row, spoken);
            row.PreviewMouseLeftButtonUp += Row_PreviewMouseLeftButtonUp;
            row.PreviewKeyDown += Row_PreviewKeyDown;
            return row;
        }

        //-----------------------------
        //a small icon button inside a row
        private Button RowAction(IconKind kind, string tip, string spoken, Action click)
        {
            var button = new Button
            {
                Style = Res<Style>("Button.Icon"),
                Width = Res<double>("Space24"),
                Height = Res<double>("Space24"),
                Focusable = false,
                ToolTip = tip,
                Content = new Icon { Kind = kind, Size = Res<double>("SizeIconSm") },
            };
            AutomationProperties.SetName(button, spoken);
            button.Click += (_, e) =>
            {
                e.Handled = true;
                click();
            };
            return button;
        }

        //-----------------------------
        //creates a menu item for a recent choice
        private MenuItem RecentRow(IReadOnlyList<string> path)
        {
            var text = Label(string.Empty);
            //formats parent path breadcrumb
            if (path.Count > 1)
            {
                var crumb = string.Join($" {Chevron} ", path.Take(path.Count - 1)) + $" {Chevron} ";
                text.Inlines.Add(new Run(crumb) { FontSize = Res<double>("FontSizeCaption"), Foreground = Res<Brush>("TextSecondaryBrush") });
            }
            text.Inlines.Add(new Run(path[^1]));
            return ChoiceRow(path, text, string.Join($" {Chevron} ", path));
        }

        //-----------------------------
        //first row of an opened branch that chooses the branch itself, such as Coding only
        private MenuItem OnlyRow(IReadOnlyList<string> path) => ChoiceRow(path, Label($"{path[^1]} only"), $"{path[^1]} only");

        //-----------------------------
        //a row that chooses a path straight away, with a check when it is the chosen one
        private MenuItem ChoiceRow(IReadOnlyList<string> path, TextBlock text, string spoken)
        {
            var header = new DockPanel();
            if (Same(path, _selected))
            {
                var tick = new Icon
                {
                    Kind = IconKind.Check,
                    Size = Res<double>("SizeIconSm"),
                    Foreground = Res<Brush>("TextAccentBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 4, 0),
                };
                DockPanel.SetDock(tick, Dock.Right);
                header.Children.Add(tick);
            }
            header.Children.Add(text);

            var row = new MenuItem { Style = Res<Style>("Picker.Row"), Header = header };
            AutomationProperties.SetName(row, spoken);
            row.PreviewMouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Choose(path);
            };
            row.PreviewKeyDown += (_, e) =>
            {
                if (!ReferenceEquals(e.OriginalSource, row))
                    return;

                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    Choose(path);
                }
                else if (e.Key is Key.Left or Key.Back && _level.Count > 0)
                {
                    e.Handled = true;
                    GoBack();
                }
            };
            return row;
        }

        //-----------------------------
        //creates the add row button
        private MenuItem AddRow(IReadOnlyList<string> path)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new Icon { Kind = IconKind.Plus, Size = Res<double>("SizeIconSm"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var label = Label(AddLabel(path));
            label.FontWeight = Res<FontWeight>("FontWeightSemibold");
            header.Children.Add(label);

            var row = new MenuItem { Style = Res<Style>("Picker.Row"), Header = header, Foreground = Res<Brush>("TextAccentBrush") };
            AutomationProperties.SetName(row, AddLabel(path));
            row.PreviewMouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                BeginAdding(row, path);
            };
            row.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && ReferenceEquals(e.OriginalSource, row))
                {
                    e.Handled = true;
                    BeginAdding(row, path);
                }
            };
            return row;
        }

        //-----------------------------
        //creates the inline text field for adding an item
        private MenuItem AddField(IReadOnlyList<string> path)
        {
            var field = InlineField(AddLabel(path), "New item", "", "Add", text => Commit(path, text), CancelAdding);
            _addPath = path;
            _addInput = field.Input;
            _addField = field.Item;
            return field.Item;
        }

        //-----------------------------
        //the parts of an inline name field
        private sealed record InlineEdit(MenuItem Item, TextBox Input, DockPanel Notice, TextBlock NoticeText)
        {
            //-----------------------------
            //shows a warning or why the name was refused under the field
            public void Say(string message, bool refused)
            {
                NoticeText.Text = message;
                Notice.Visibility = Visibility.Visible;
                Field.SetIsInvalid(Input, refused);
                if (refused)
                    Input.Focus();
            }
        }

        //-----------------------------
        //a name field with Cancel and a confirm button, and a line under it for warnings and errors
        private InlineEdit InlineField(string spokenName, string placeholder, string text, string confirmLabel, Action<string> confirm, Action cancel)
        {
            var input = new TextBox
            {
                Style = Res<Style>("Input"),
                Height = Res<double>("Space32"),
                Padding = new Thickness(8, 0, 8, 0),
                MaxLength = MaxNameLength,
                Text = text,
            };
            Field.SetPlaceholder(input, placeholder);
            AutomationProperties.SetName(input, spokenName);
            input.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    confirm(input.Text);
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    cancel();
                }
            };

            var ok = SmallButton("Button.Primary", confirmLabel);
            ok.Margin = new Thickness(8, 0, 0, 0);
            ok.Click += (_, _) => confirm(input.Text);
            var no = SmallButton("Button.Secondary", "Cancel");
            no.Margin = new Thickness(8, 0, 0, 0);
            no.Click += (_, _) => cancel();

            var line = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            DockPanel.SetDock(ok, Dock.Right);
            DockPanel.SetDock(no, Dock.Right);
            line.Children.Add(ok);
            line.Children.Add(no);
            line.Children.Add(input);

            //icon and text, never a colour change alone
            var notice = new DockPanel { Margin = new Thickness(0, 0, 0, 4), Visibility = Visibility.Collapsed };
            var alert = new Icon { Kind = IconKind.Alert, Size = Res<double>("SizeIconSm"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 4, 0) };
            DockPanel.SetDock(alert, Dock.Left);
            var noticeText = new TextBlock { Style = Res<Style>("Text.Caption"), Foreground = Res<Brush>("TextSecondaryBrush") };
            notice.Children.Add(alert);
            notice.Children.Add(noticeText);
            input.TextChanged += (_, _) =>
            {
                if (Field.GetIsInvalid(input))
                {
                    Field.SetIsInvalid(input, false);
                    notice.Visibility = Visibility.Collapsed;
                }
            };

            var panel = new StackPanel();
            panel.Children.Add(line);
            panel.Children.Add(notice);
            return new InlineEdit(new MenuItem { Style = Res<Style>("Menu.AddField"), Header = panel }, input, notice, noticeText);
        }

        private Button SmallButton(string style, string text) => new()
        {
            Style = Res<Style>(style),
            Content = text,
            Height = Res<double>("Space32"),
            Padding = new Thickness(12, 0, 12, 0),
        };

        private void BeginAdding(MenuItem addRow, IReadOnlyList<string> path)
        {
            CancelAdding();
            var field = AddField(path);
            Replace(addRow, field, _addInput);
            FocusLater(_addInput);
        }

        private void CancelAdding()
        {
            if (_addField != null && _addPath != null)
            {
                var row = AddRow(_addPath);
                Replace(_addField, row, row);
            }
            _addPath = null;
            _addField = null;
            _addInput = null;
        }

        //-----------------------------
        //swaps a row for a rename field, with a warning under it when one applies
        private void BeginRenaming(MenuItem row, IReadOnlyList<string> path)
        {
            if (RenameAsync == null || _renaming)
                return;

            CancelAdding();
            _renaming = true;
            _renamePath = path;
            InlineEdit? field = null;
            field = InlineField($"Rename {path[^1]}", path[^1], path[^1], "Save",
                text => _ = CommitRenameAsync(field!, path, text),
                () => ShowLevel(_level, focusPath: path));
            Replace(row, field.Item, field.Input);
            FocusLater(field.Input, selectAll: true);
            _ = ShowRenameWarningAsync(path, field);
        }

        private async Task ShowRenameWarningAsync(IReadOnlyList<string> path, InlineEdit field)
        {
            if (RenameWarningAsync == null || await RenameWarningAsync(path) is not { } warning)
                return;

            //the field may have closed, or already be showing a refusal, while the warning loaded
            if (_renamePath == null || !Same(_renamePath, path) || field.Notice.Visibility == Visibility.Visible)
                return;
            field.Say(warning, refused: false);
        }

        private async Task CommitRenameAsync(InlineEdit field, IReadOnlyList<string> path, string text)
        {
            var name = CleanName(text);
            void Refuse(string message) => field.Say(message, refused: true);

            if (name.Length == 0)
            {
                Refuse("Type a name first.");
                return;
            }

            //an unchanged name just closes the field
            if (name == path[^1])
            {
                ShowLevel(_level, focusPath: path);
                return;
            }

            var siblings = path.Count == 1 ? _tree : NodeAt(path.Take(path.Count - 1).ToList())?.Children ?? [];
            if (siblings.Any(n => n.Label != path[^1] && string.Equals(n.Label, name, StringComparison.OrdinalIgnoreCase)))
            {
                Refuse($"{name} is already here.");
                return;
            }

            // Disabling the field would take focus out of the menu and close it, so a second Enter is ignored instead
            if (_renameSaving)
                return;
            _renameSaving = true;
            string? refused;
            try { refused = RenameAsync == null ? null : await RenameAsync(path, name); }
            finally { _renameSaving = false; }
            if (refused != null)
            {
                Refuse(refused);
                return;
            }

            IReadOnlyList<string> renamed = [.. path.Take(path.Count - 1), name];
            ApplyRename(path, name);
            ShowLevel(_level, focusPath: renamed);
        }

        //-----------------------------
        //renames a node in the tree and in every path that runs through it
        private void ApplyRename(IReadOnlyList<string> path, string name)
        {
            var level = path.Count == 1 ? _tree : NodeAt(path.Take(path.Count - 1).ToList())?.Children;
            var index = level?.FindIndex(n => n.Label == path[^1]) ?? -1;
            if (level == null || index < 0)
                return;
            level[index] = new ActivityNode(name, [.. level[index].Children]);

            IReadOnlyList<string> Rename(IReadOnlyList<string> p) =>
                p.Count >= path.Count && Same(p.Take(path.Count).ToList(), path)
                    ? [.. path.Take(path.Count - 1), name, .. p.Skip(path.Count)]
                    : p;

            for (var i = 0; i < _recent.Count; i++)
                _recent[i] = Rename(_recent[i]);
            if (_level.Count >= path.Count)
                _level = Rename(_level);

            var selected = Rename(_selected);
            if (!Same(selected, _selected))
            {
                _selected = selected;
                RenderValue();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void RequestRemove(IReadOnlyList<string> path)
        {
            //closes menu before raising remove
            if (_menu != null)
                _menu.IsOpen = false;
            RemoveRequested?.Invoke(this, path);
        }

        // Puts one menu item in place of another. Keyboard focus moves to the new one before the old one
        // goes: removing the focused item would take focus out of the menu, and a context menu closes then.
        private static void Replace(MenuItem current, MenuItem replacement, UIElement? focus)
        {
            if (ItemsControl.ItemsControlFromItemContainer(current) is not { } parent)
                return;

            var index = parent.Items.IndexOf(current);
            if (index < 0)
                return;

            parent.Items.Insert(index, replacement);
            replacement.UpdateLayout();
            focus?.Focus();
            parent.Items.Remove(current);
        }

        // The same for a whole level: the new rows go in and take focus before the old ones leave
        private static void ReplaceAll(ContextMenu menu, IReadOnlyList<object> items, UIElement? focus)
        {
            var old = menu.Items.Cast<object>().ToList();
            foreach (var item in items)
                menu.Items.Add(item);
            menu.UpdateLayout();
            focus?.Focus();
            foreach (var item in old)
                menu.Items.Remove(item);
        }

        private void FocusLater(UIElement? target, bool selectAll = false) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            target?.Focus();
            if (selectAll && target is TextBox box)
                box.SelectAll();
        });

        private void Commit(IReadOnlyList<string> path, string text)
        {
            var name = CleanName(text);
            if (name.Length == 0)
                return;

            var level = path.Count == 0 ? _tree : NodeAt(path)?.Children;
            if (level == null)
                return;

            var existing = level.FirstOrDefault(n => string.Equals(n.Label, name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new ActivityNode(name);
                level.Add(existing);
            }

            //guided child add opens the new item with its add field ready
            if (GuidedChildAdd && path.Count == 0)
            {
                _addPath = [existing.Label];
                ShowLevel(_addPath);
            }
            else
            {
                Choose([.. path, existing.Label]);
            }
        }

        private void Choose(IReadOnlyList<string> path)
        {
            _selected = path;
            _recent.RemoveAll(r => Same(r, path));
            _recent.Insert(0, path);
            if (_recent.Count > RecentLimit)
                _recent.RemoveRange(RecentLimit, _recent.Count - RecentLimit);

            Field.SetIsInvalid(PickerButton, false);
            ErrorLine.Visibility = Visibility.Collapsed;
            AutomationProperties.SetHelpText(PickerButton, string.Empty);
            RenderValue();

            if (_menu != null)
                _menu.IsOpen = false;
            PickerButton.Focus();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Menu_Closed(object sender, RoutedEventArgs e)
        {
            Field.SetIsOpen(PickerButton, false);
            _addPath = null;
            _addField = null;
            _addInput = null;
            _renamePath = null;
            _renaming = false;
            if (ReferenceEquals(sender, _menu))
                _menu = null;
        }

        //-----------------------------
        //a branch opens in place, anything else is chosen
        private void Activate(IReadOnlyList<string> path)
        {
            if (NodeAt(path) is { } node && IsBranch(path, node))
                ShowLevel(path);
            else
                Choose(path);
        }

        private void Row_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            //rename and remove buttons get the click instead of the row
            if (IsInsideButton(e.OriginalSource as DependencyObject))
                return;

            if (sender is MenuItem { Tag: IReadOnlyList<string> path })
            {
                e.Handled = true;
                Activate(path);
            }
        }

        private void Row_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, sender) || sender is not MenuItem { Tag: IReadOnlyList<string> path } row)
                return;

            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    Activate(path);
                    break;
                case Key.Right when NodeAt(path) is { } node && IsBranch(path, node):
                    e.Handled = true;
                    ShowLevel(path);
                    break;
                case Key.Left or Key.Back when _level.Count > 0:
                    e.Handled = true;
                    GoBack();
                    break;
                case Key.F2 when CanRename?.Invoke(path) == true:
                    e.Handled = true;
                    BeginRenaming(row, path);
                    break;
                case Key.Delete when CanRemove?.Invoke(path) == true:
                    e.Handled = true;
                    RequestRemove(path);
                    break;
            }
        }

        //-----------------------------
        //drops a removed row from the tree and recent list
        public void RemoveNode(IReadOnlyList<string> path)
        {
            //top level path removes from root level
            if (path.Count == 1)
            {
                _tree.RemoveAll(n => string.Equals(n.Label, path[0], StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                var parent = NodeAt(path.Take(path.Count - 1).ToList());
                //removes child from parent node
                if (parent != null)
                {
                    parent.Children.RemoveAll(n => string.Equals(n.Label, path[^1], StringComparison.OrdinalIgnoreCase));
                }
            }

            //removes recent entries starting with path
            _recent.RemoveAll(r => r.Count >= path.Count && Same(r.Take(path.Count).ToList(), path));

            //clears selected path if it starts with path
            if (_selected.Count >= path.Count && Same(_selected.Take(path.Count).ToList(), path))
            {
                _selected = Array.Empty<string>();
            }

            RenderValue();
        }

        //-----------------------------
        //true when the click landed on a button inside a row
        private static bool IsInsideButton(DependencyObject? d)
        {
            //walk up until the row itself
            while (d != null && d is not MenuItem)
            {
                //a button such as remove handles its own click
                if (d is System.Windows.Controls.Primitives.ButtonBase)
                {
                    return true;
                }

                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }

            return false;
        }

        //-----------------------------
        //a row opens a level when it has children, or when a choice has to go deeper than it
        private bool IsBranch(IReadOnlyList<string> path, ActivityNode node) =>
            path.Count < MaxDepth && (node.Children.Count > 0 || path.Count < RequiredDepth);

        //-----------------------------
        //finds node at path in tree
        private ActivityNode? NodeAt(IReadOnlyList<string> path)
        {
            ActivityNode? node = null;
            var level = _tree;
            foreach (var name in path)
            {
                node = level.FirstOrDefault(n => n.Label == name);
                if (node == null)
                    return null;
                level = node.Children;
            }
            return node;
        }

        //-----------------------------
        //generates add row label text
        private string AddLabel(IReadOnlyList<string> path) => AddLabelFor != null ? AddLabelFor(path) : (path.Count == 0 ? "Add an activity" : $"Add to {path[^1]}");

        private static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b) => a.SequenceEqual(b);

        private T Res<T>(string key) => (T)FindResource(key);

        private TextBlock Label(string text) => new()
        {
            Text = text,
            RenderTransform = Res<Transform>("Baseline.Body"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = Res<double>("LineHeightBody"),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private TextBlock Text(string text, double size, Brush brush) => new()
        {
            Text = text,
            RenderTransform = Res<Transform>("Baseline.Body"),
            FontSize = size,
            Foreground = brush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = Res<double>("LineHeightBody"),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
//------------------------------EOF-----------------------------\\
