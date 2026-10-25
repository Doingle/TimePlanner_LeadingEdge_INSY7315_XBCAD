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
