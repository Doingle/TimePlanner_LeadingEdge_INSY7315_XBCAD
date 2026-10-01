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

        public event EventHandler? SelectionChanged;

        public IReadOnlyList<string> SelectedPath => _selected;


        public void Load(List<ActivityNode> tree, List<IReadOnlyList<string>> recent, IReadOnlyList<string> selected)
        {
            _tree = tree;
            _recent = recent;
            _selected = selected;
            RenderValue();
        }

        public bool Validate()
        {
            if (_selected.Count > 0)
                return true;

            Field.SetIsInvalid(PickerButton, true);
            ErrorLine.Visibility = Visibility.Visible;
            AutomationProperties.SetHelpText(PickerButton, "Choose an activity to save this entry.");
            return false;
        }

        public void FocusField() => PickerButton.Focus();


        public void Open(IReadOnlyList<string>? expand = null, IReadOnlyList<string>? addingAt = null)
        {
            _addPath = addingAt;
            _menu = BuildMenu();
            _menu.Closed += Menu_Closed;
            Field.SetIsOpen(PickerButton, true);
            _menu.IsOpen = true;

            foreach (var path in new[] { expand, addingAt })
            {
                if (path == null) continue;
                for (var depth = 1; depth <= path.Count; depth++)
                    if (_rows.TryGetValue(PathKey(path.Take(depth)), out var row))
                        row.IsSubmenuOpen = true;
            }

            if (_addInput != null)
                FocusAddInput();
        }


        private void PickerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_menu is { IsOpen: true })
            {
                _menu.IsOpen = false;
                return;
            }
            Open(expand: _selected.Count > 1 ? _selected.Take(_selected.Count - 1).ToList() : null);
        }

        private void RenderValue()
        {
            PathPanel.Children.Clear();
            if (_selected.Count == 0)
            {
                PathPanel.Children.Add(Text("Choose an activity", Res<double>("FontSizeBody"), Res<Brush>("TextTertiaryBrush")));
                PickerButton.ToolTip = null;
                AutomationProperties.SetName(PickerButton, "Activity, Choose an activity");
                return;
            }

            for (var i = 0; i < _selected.Count; i++)
            {
                var last = i == _selected.Count - 1;
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
            AutomationProperties.SetName(PickerButton, "Activity, " + spoken);
        }

        private ContextMenu BuildMenu()
        {
            _rows.Clear();
            _addField = null;
            _addInput = null;

            var menu = new ContextMenu { Style = Res<Style>("Menu.Dropdown"), PlacementTarget = PickerButton };
            AutomationProperties.SetName(menu, "Activity");

            if (_recent.Count > 0)
            {
                menu.Items.Add(new Separator { Style = Res<Style>("Menu.Label"), Tag = "Recent" });
                foreach (var path in _recent)
                    menu.Items.Add(RecentRow(path));
                menu.Items.Add(new Separator { Style = Res<Style>("Menu.Separator") });
            }

            AddLevel(menu.Items, [], _tree);
            return menu;
        }

        private void AddLevel(ItemCollection items, IReadOnlyList<string> path, List<ActivityNode> nodes)
        {
            foreach (var node in nodes)
            {
                IReadOnlyList<string> nodePath = [.. path, node.Label];
                var row = new MenuItem
                {
                    Style = Res<Style>("Menu.Row"),
                    Header = Label(node.Label),
                    IsChecked = Same(nodePath, _selected),
                    Tag = nodePath,
                };
                MenuAssist.SetIsEmptyBranch(row, node.Children.Count == 0);
                AutomationProperties.SetName(row, node.Label);
                row.PreviewMouseLeftButtonUp += Row_PreviewMouseLeftButtonUp;
                row.PreviewKeyDown += Row_PreviewKeyDown;
                AddLevel(row.Items, nodePath, node.Children);

                _rows[PathKey(nodePath)] = row;
                items.Add(row);
            }

            // Core nests activities at most MaxDepth deep, so the deepest level offers no Add
            if (path.Count >= ActivityService.MaxDepth)
                return;

            if (nodes.Count > 0)
                items.Add(new Separator { Style = Res<Style>("Menu.Separator") });

            items.Add(_addPath != null && Same(_addPath, path) ? AddField(path) : AddRow(path));
        }

        private MenuItem RecentRow(IReadOnlyList<string> path)
        {
            var text = Label(string.Empty);
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

        private MenuItem AddField(IReadOnlyList<string> path)
        {
            var input = new TextBox { Style = Res<Style>("Input"), Padding = new Thickness(6, 0, 6, 0), MaxLength = InputLimits.ActivityName };
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
            var name = InputLimits.CleanActivityName(text);
            if (name.Length == 0)
                return;

            var level = path.Count == 0 ? _tree : NodeAt(path)?.Children;
            if (level == null)
                return;

            var existing = level.FirstOrDefault(n => string.Equals(n.Label, name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                level.Add(new ActivityNode(name));

            Choose([.. path, existing?.Label ?? name]);
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
                Choose(path);
            }
        }

        private void Row_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ReferenceEquals(e.OriginalSource, sender) && sender is MenuItem { Tag: IReadOnlyList<string> path })
            {
                e.Handled = true;
                Choose(path);
            }
        }


        private static MenuItem? OwningRow(DependencyObject? d)
        {
            while (d != null && d is not MenuItem)
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as MenuItem;
        }

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

        private static string AddLabel(IReadOnlyList<string> path) => path.Count == 0 ? "Add an activity" : $"Add to {path[^1]}";

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
    }
}


