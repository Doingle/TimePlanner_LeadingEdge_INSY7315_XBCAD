using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
    public partial class ActivityPicker : UserControl
    {
        private const string Chevron = "›";  
        private const int RecentLimit = 3;

        private readonly Dictionary<string, MenuItem> _rows = [];
        private List<ActivityNode> _tree = [];
        private List<IReadOnlyList<string>> _recent = [];
        private IReadOnlyList<string> _selected = [];
        private ContextMenu? _menu;
        private IReadOnlyList<string>? _addPath;
        private MenuItem? _addField;
        private TextBox? _addInput;

        public ActivityPicker()
        {
            InitializeComponent();
            RenderValue();
        }

        //-----------------------------
        //what one item is called in messages
        public string FieldName { get; set; } = "Activity";

        //-----------------------------
        //text shown when nothing is chosen
        public string Placeholder { get; set; } = "Choose an activity";

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
        //opens the dropdown menu at a given path
        public void Open(IReadOnlyList<string>? expand = null, IReadOnlyList<string>? addingAt = null)
        {
            _addPath = addingAt;
            _menu = BuildMenu();
            _menu.Closed += Menu_Closed;
            Field.SetIsOpen(PickerButton, true);
            _menu.IsOpen = true;

            //expands paths for first display
            foreach (var path in new[] { expand, addingAt })
            {
                //skips null expand paths
                if (path == null) continue;
                for (var depth = 1; depth <= path.Count; depth++)
                    //opens submenus along path
                    if (_rows.TryGetValue(PathKey(path.Take(depth)), out var row))
                    {
                        row.IsSubmenuOpen = true;
                        CloseSiblings(row);
                    }
            }

            //focuses input box when adding
            if (_addInput != null)
                FocusAddInput();
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
            Open(expand: _selected.Count > 1 ? _selected.Take(_selected.Count - 1).ToList() : null);
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
        //builds the dropdown context menu
        private ContextMenu BuildMenu()
        {
            _rows.Clear();
            _addField = null;
            _addInput = null;

            var menu = new ContextMenu { Style = Res<Style>("Menu.Dropdown"), PlacementTarget = PickerButton };
            AutomationProperties.SetName(menu, FieldName);

            //adds recent section when recent choices exist
            if (_recent.Count > 0)
            {
                menu.Items.Add(new Separator { Style = Res<Style>("Menu.Label"), Tag = "Recent" });
                //adds recent rows
                foreach (var path in _recent)
                    menu.Items.Add(RecentRow(path));
                menu.Items.Add(new Separator { Style = Res<Style>("Menu.Separator") });
            }

            AddLevel(menu.Items, [], _tree);
            return menu;
        }

        //-----------------------------
        //adds one tree level of items and an add row
        private void AddLevel(ItemCollection items, IReadOnlyList<string> path, List<ActivityNode> nodes)
        {
            //adds menu items for each node
            foreach (var node in nodes)
            {
                IReadOnlyList<string> nodePath = [.. path, node.Label];
                UIElement headerContent = Label(node.Label);

                //shows remove button on removable rows
                if (CanRemove?.Invoke(nodePath) == true)
                {
                    var removeBtn = new Button
                    {
                        Style = Res<Style>("Button.Icon"),
                        Content = new Icon { Kind = IconKind.Close, Size = Res<double>("SizeIconSm") },
                        ToolTip = "Remove",
                        Margin = new Thickness(8, 0, 0, 0)
                    };
                    AutomationProperties.SetName(removeBtn, $"Remove {node.Label}");
                    removeBtn.Click += (s, e) =>
                    {
                        e.Handled = true;
                        //closes menu before raising remove
                        if (_menu != null)
                        {
                            _menu.IsOpen = false;
                        }
                        RemoveRequested?.Invoke(this, nodePath);
                    };

                    var panel = new DockPanel();
                    DockPanel.SetDock(removeBtn, Dock.Right);
                    panel.Children.Add(removeBtn);
                    panel.Children.Add(headerContent);
                    headerContent = panel;
                }

                var row = new MenuItem
                {
                    Style = Res<Style>("Menu.Row"),
                    Header = headerContent,
                    IsChecked = Same(nodePath, _selected),
                    Tag = nodePath,
                };
                MenuAssist.SetIsEmptyBranch(row, node.Children.Count == 0);
                AutomationProperties.SetName(row, node.Label);
                row.PreviewMouseLeftButtonUp += Row_PreviewMouseLeftButtonUp;
                row.PreviewKeyDown += Row_PreviewKeyDown;
                AddLevel(row.Items, nodePath, node.Children);

                //watches row for mouse hover and focus leave
                if (row.Items.Count > 0)
                {
                    WatchBranch(row);
                }

                _rows[PathKey(nodePath)] = row;
                items.Add(row);
            }

            //root level offers add row only when allowed
            if (path.Count == 0 && !AllowRootAdd)
            {
                return;
            }

            //deepest allowed level offers no add row
            if (path.Count >= MaxDepth)
                return;

            //separates items from add row
            if (nodes.Count > 0)
                items.Add(new Separator { Style = Res<Style>("Menu.Separator") });

            items.Add(_addPath != null && Same(_addPath, path) ? AddField(path) : AddRow(path));
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

            var row = new MenuItem { Style = Res<Style>("Menu.Row"), Header = text, IsChecked = Same(path, _selected) };
            AutomationProperties.SetName(row, string.Join($" {Chevron} ", path));
            row.Click += (_, _) => Choose(path);
            return row;
        }

        //-----------------------------
        //creates the add row button
        private MenuItem AddRow(IReadOnlyList<string> path)
        {
            var row = new MenuItem
            {
                Style = Res<Style>("Menu.Row"),
                Header = Label(AddLabel(path)),
                Icon = new Icon { Kind = IconKind.Plus, Size = Res<double>("SizeIconSm") },
                StaysOpenOnClick = true,
            };
            row.Click += (_, _) => BeginAdding(row, path);
            return row;
        }

        //-----------------------------
        //creates the inline text field for adding an item
        private MenuItem AddField(IReadOnlyList<string> path)
        {
            var input = new TextBox { Style = Res<Style>("Input"), Padding = new Thickness(6, 0, 6, 0), MaxLength = MaxNameLength };
            Field.SetPlaceholder(input, "New item");
            AutomationProperties.SetName(input, AddLabel(path));
            input.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    Commit(path, input.Text);
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    CancelAdding();
                }
            };

            var confirm = new Button
            {
                Style = Res<Style>("Button.Icon"),
                Content = new Icon { Kind = IconKind.Check },
                ToolTip = "Add",
                Margin = new Thickness(8, 0, 0, 0),
            };
            AutomationProperties.SetName(confirm, "Add");
            confirm.Click += (_, _) => Commit(path, input.Text);

            var line = new DockPanel { Margin = new Thickness(0, 4, 0, 4), MinWidth = 202 };
            DockPanel.SetDock(confirm, Dock.Right);
            line.Children.Add(confirm);
            line.Children.Add(input);

            var hint = new TextBlock
            {
                Style = Res<Style>("Text.Caption"),
                Foreground = Res<Brush>("TextTertiaryBrush"),
                Margin = new Thickness(8, 0, 8, 4),
                Text = "Enter to add, Esc to cancel",
            };

            var panel = new StackPanel();
            panel.Children.Add(line);
            panel.Children.Add(hint);

            _addPath = path;
            _addInput = input;
            _addField = new MenuItem { Style = Res<Style>("Menu.AddField"), Header = panel };
            return _addField;
        }

        private void BeginAdding(MenuItem addRow, IReadOnlyList<string> path)
        {
            CancelAdding();
            var field = AddField(path);
            Replace(addRow, field, _addInput);
            FocusAddInput();
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

        private void FocusAddInput()
        {
            var input = _addInput;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => input?.Focus());
        }

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

            //guided child add opens child input for top level items
            if (GuidedChildAdd && path.Count == 0)
            {
                //closes menu before opening child add
                if (_menu != null)
                {
                    _menu.IsOpen = false;
                }
                Open(addingAt: [existing.Label]);
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
            if (ReferenceEquals(sender, _menu))
                _menu = null;
        }

        private void Row_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is MenuItem { Tag: IReadOnlyList<string> path } row && OwningRow(e.OriginalSource as DependencyObject) == row)
            {
                e.Handled = true;

                //shallow choices expand their submenu instead of selecting
                if (path.Count < RequiredDepth)
                {
                    row.IsSubmenuOpen = true;
                    CloseSiblings(row);
                    return;
                }

                Choose(path);
            }
        }

        private void Row_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ReferenceEquals(e.OriginalSource, sender) && sender is MenuItem { Tag: IReadOnlyList<string> path } row)
            {
                e.Handled = true;

                if (path.Count < RequiredDepth)
                {
                    row.IsSubmenuOpen = true;
                    CloseSiblings(row);
                    return;
                }

                Choose(path);
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
        //finds containing menu item for a visual node
        private static MenuItem? OwningRow(DependencyObject? d)
        {
            //traverses visual or logical tree upward
            while (d != null && d is not MenuItem)
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as MenuItem;
        }

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

        private static string PathKey(IEnumerable<string> path) => string.Join(">", path);

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

        //grace before an unused submenu closes
        private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(400);

        //-----------------------------
        //wires hover rules onto a branch row
        private void WatchBranch(MenuItem row)
        {
            row.MouseEnter += (_, _) => CloseSiblings(row);
            row.MouseLeave += (_, _) => ScheduleClose(row);
            row.LostKeyboardFocus += (_, _) => ScheduleClose(row);
        }

        //-----------------------------
        //closes other open branches at the same level
        private static void CloseSiblings(MenuItem row)
        {
            //only rows inside a menu have siblings
            if (ItemsControl.ItemsControlFromItemContainer(row) is not { } parent)
            {
                return;
            }

            //every other open branch at this level closes
            foreach (var sibling in parent.Items.OfType<MenuItem>().Where(s => s != row && s.IsSubmenuOpen))
            {
                sibling.IsSubmenuOpen = false;
            }
        }

        //-----------------------------
        //closes a branch soon after mouse and focus leave it
        private void ScheduleClose(MenuItem row)
        {
            var timer = new DispatcherTimer { Interval = CloseDelay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();

                //hovered or typed in branches stay open
                if (!row.IsMouseOver && !row.IsKeyboardFocusWithin)
                {
                    row.IsSubmenuOpen = false;
                }
            };
            timer.Start();
        }
    }
}
//------------------------------EOF-----------------------------\\


