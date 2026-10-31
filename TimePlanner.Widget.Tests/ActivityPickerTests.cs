using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TimePlanner.Widget.Controls;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Tests;

public class ActivityPickerTests
{
    [Fact]
    public Task BranchesOpenInPlace_AndRenamingUpdatesTheRowAndTheChosenPath() => ReminderFlowTests.OnUi(async () =>
    {
        var picker = new ActivityPicker();
        // The dropdown is a popup, which needs a real window under it
        var window = new Window { Content = picker, Width = 320, Height = 120, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        window.Show();
        window.Activate();
        try
        {
            picker.Load([new ActivityNode("Coding", new ActivityNode("Bug fix"), new ActivityNode("Testing")), new ActivityNode("Email")],
                [], ["Coding", "Bug fix"]);
            var saved = new List<string>();
            picker.CanRename = path => path.Count > 1;
            picker.RenameAsync = (path, name) =>
            {
                saved.Add($"{string.Join(" › ", path)} → {name}");
                return Task.FromResult<string?>(null);
            };
            picker.RenameWarningAsync = _ => Task.FromResult<string?>("Used on a day you already sent.");
            var changes = 0;
            picker.SelectionChanged += (_, _) => changes++;

            // A branch opens in place, under a header that goes back
            picker.Open();
            var menu = OpenMenu();
            Press(Row(menu, "Coding"), Key.Enter);
            Assert.Null(FindRow(menu, "Email"));
            Assert.NotNull(Row(menu, "Bug fix"));

            // F2 swaps the row for a name field, with the sent-day warning under it
            Press(Row(menu, "Bug fix"), Key.F2);
            var input = Descendants<TextBox>(menu).Single();
            await WaitUntil(() => Descendants<TextBlock>(menu).Any(t => t.Text == "Used on a day you already sent."));
            Assert.Equal("Bug fix", input.Text);

            input.Text = "Bug fixing";
            Press(input, Key.Enter);
            await WaitUntil(() => FindRow(menu, "Bug fixing") != null);

            Assert.Equal(["Coding › Bug fix → Bug fixing"], saved);
            Assert.Equal(["Coding", "Bug fixing"], picker.SelectedPath);
            Assert.Equal(1, changes);
            Assert.True(menu.IsOpen);

            // Left goes back to the top level
            Press(Row(menu, "Testing"), Key.Left);
            Assert.NotNull(Row(menu, "Email"));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task AnOpenedBranchCanStillBeChosenOnItsOwn() => ReminderFlowTests.OnUi(async () =>
    {
        var picker = new ActivityPicker();
        var window = new Window { Content = picker, Width = 320, Height = 120, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        window.Show();
        window.Activate();
        try
        {
            picker.Load([new ActivityNode("Coding", new ActivityNode("Bug fix"))], [], ["Coding", "Bug fix"]);

            picker.Open();
            var menu = OpenMenu();
            Press(Row(menu, "Coding"), Key.Enter);
            Press(Named(menu, "Coding only"), Key.Enter);

            Assert.Equal(["Coding"], picker.SelectedPath);
            await WaitUntil(() => !menu.IsOpen);

            // Opened again, the Coding only row carries the check
            picker.Open(expand: ["Coding"]);
            menu = OpenMenu();
            Assert.Contains(Descendants<Icon>(Named(menu, "Coding only")), icon => icon.Kind == IconKind.Check);
            Assert.DoesNotContain(Descendants<Icon>(Row(menu, "Bug fix")), icon => icon.Kind == IconKind.Check);
            menu.IsOpen = false;
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task AnItemWithNothingUnderItYet_CanTakeTheNextLevel_UntilTheDeepest() => ReminderFlowTests.OnUi(async () =>
    {
        var picker = new ActivityPicker();
        var window = new Window { Content = picker, Width = 320, Height = 120, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        window.Show();
        window.Activate();
        try
        {
            picker.Load([new ActivityNode("Coding", new ActivityNode("Frontend", new ActivityNode("React")), new ActivityNode("Backend"), new ActivityNode("Testing"))],
                [], []);
            picker.CanRename = path => path.Count > 1;
            picker.CanRemove = path => path.Count > 1;

            // Coding is a main activity with things under it: nothing behind a More button
            picker.Open();
            var menu = OpenMenu();
            Assert.Null(MoreButton(Row(menu, "Coding")));

            // Backend has nothing under it yet: its More menu adds under it, renames it and, after a divider, removes it in red
            Press(Row(menu, "Coding"), Key.Enter);
            ClickButton(MoreButton(Row(menu, "Backend"))!);
            var more = OpenMenuNamed("More for Backend");
            Assert.Equal(["Add under Backend", "Rename Backend", "Remove Backend"], more.Items.OfType<MenuItem>().Select(AutomationProperties.GetName));
            Assert.IsType<Separator>(more.Items[2]);
            var remove = (MenuItem)more.Items[3];
            Assert.Same(remove.FindResource("TextDangerBrush"), remove.Foreground);

            // Add under opens Backend with the add field ready, and Backend can still be chosen on its own
            ((MenuItem)more.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(more.IsOpen);
            Assert.True(menu.IsOpen);
            Assert.NotNull(Named(menu, "Backend only"));
            var input = Descendants<TextBox>(menu).Single();
            input.Text = "API";
            Press(input, Key.Enter);

            Assert.Equal(["Coding", "Backend", "API"], picker.SelectedPath);
            await WaitUntil(() => !menu.IsOpen);

            // React is at the deepest level: its More menu has no Add under, and Right leaves it where it is
            picker.Open(expand: ["Coding", "Frontend"]);
            menu = OpenMenu();
            Press(Row(menu, "React"), Key.Apps);
            more = OpenMenuNamed("More for React");
            Assert.Equal(["Rename React", "Remove React"], more.Items.OfType<MenuItem>().Select(AutomationProperties.GetName));
            more.IsOpen = false;
            Press(Row(menu, "React"), Key.Right);
            Assert.Empty(Descendants<TextBox>(menu));
            Assert.NotNull(Row(menu, "React"));

            // Right on a row with nothing under it does what Add under does
            Press(Row(menu, "React"), Key.Left);
            Press(Row(menu, "Testing"), Key.Right);
            Assert.NotNull(Named(menu, "Testing only"));
            Assert.Single(Descendants<TextBox>(menu));
            menu.IsOpen = false;
        }
        finally
        {
            window.Close();
        }
    });

    private static MenuItem Named(ContextMenu menu, string name)
    {
        var row = menu.Items.OfType<MenuItem>().FirstOrDefault(i => AutomationProperties.GetName(i) == name);
        Assert.NotNull(row);
        return row;
    }

    private static ContextMenu OpenMenu() =>
        PresentationSource.CurrentSources.OfType<HwndSource>()
            .SelectMany(source => source.RootVisual is { } root ? Descendants<ContextMenu>(root) : [])
            .Single(menu => menu.IsOpen);

    private static ContextMenu OpenMenuNamed(string name) =>
        PresentationSource.CurrentSources.OfType<HwndSource>()
            .SelectMany(source => source.RootVisual is { } root ? Descendants<ContextMenu>(root) : [])
            .Single(menu => menu.IsOpen && AutomationProperties.GetName(menu) == name);

    private static Button? MoreButton(MenuItem row) =>
        Descendants<Button>(row).SingleOrDefault(b => AutomationProperties.GetName(b).StartsWith("More for "));

    private static void ClickButton(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    private static MenuItem? FindRow(ContextMenu menu, string label) =>
        menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.Tag is IReadOnlyList<string> path && path[^1] == label);

    private static MenuItem Row(ContextMenu menu, string label)
    {
        var row = FindRow(menu, label);
        Assert.NotNull(row);
        return row;
    }

    private static void Press(UIElement target, Key key) =>
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });

    private static async Task WaitUntil(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
            await Task.Delay(20);
        Assert.True(done());
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;
            foreach (var below in Descendants<T>(child))
                yield return below;
        }
    }
}
