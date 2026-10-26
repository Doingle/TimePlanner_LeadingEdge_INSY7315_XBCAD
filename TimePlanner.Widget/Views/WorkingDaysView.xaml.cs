using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views;

/// <summary>
/// The working days, Monday to Sunday. Check-ins only happen on ticked days, and the widget only opens on its
/// own at sign-in on them; on other days a day can still be started by hand. Each tick is saved straight away.
/// </summary>
public partial class WorkingDaysView : UserControl
{
    private readonly WidgetFlow _flow;
    private readonly Action _back;
    private readonly List<(DayOfWeek Day, CheckBox Box)> _days = [];
    private readonly bool _ready;

    public WorkingDaysView(WidgetFlow flow, Action back)
    {
        InitializeComponent();
        _flow = flow;
        _back = back;

        var ticked = flow.Session.Preferences.WorkingDays;
        foreach (var day in Format.Week)
        {
            if (_days.Count > 0)
            {
                DaysList.Children.Add(new Border
                {
                    Height = (double)FindResource("Hairline"),
                    Background = (Brush)FindResource("BorderSubtleBrush"),
                });
            }

            var box = new CheckBox
            {
                Style = (Style)FindResource("DayRow"),
                Content = day.ToString(),
                IsChecked = ticked.Contains(day),
            };
            AutomationProperties.SetName(box, day.ToString());
            box.Checked += Day_Changed;
            box.Unchecked += Day_Changed;
            DaysList.Children.Add(box);
            _days.Add((day, box));
        }

        _ready = true;
    }

    private async void Day_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready)
            await _flow.ChangeWorkingDaysAsync(_days.Where(d => d.Box.IsChecked == true).Select(d => d.Day));
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _back();

    private void View_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        e.Handled = true;
        _back();
    }
}
