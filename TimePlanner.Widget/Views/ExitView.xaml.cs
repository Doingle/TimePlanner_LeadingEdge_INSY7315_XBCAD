using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views;

public partial class ExitView : UserControl
{
    private readonly Action _exit;
    private readonly Action _cancel;

    public ExitView(WidgetFlow flow, Action exit, Action cancel)
    {
        InitializeComponent();
        _exit = exit;
        _cancel = cancel;
        Explanation.Text = flow.Scheduler.IsRunning
            ? $"Your day stays open. You can log it later. We won't check in while Time Planner is closed. You can log the time since {Format.Clock(flow.GetLogPeriod().Start)} when you open it again."
            : "We won't check in while Time Planner is closed. You can open it again from the Start menu.";
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => _exit();
    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancel();
    private void View_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        _cancel();
    }
}
