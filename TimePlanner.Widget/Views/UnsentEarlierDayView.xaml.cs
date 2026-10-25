using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views;

/// <summary>
/// "Yesterday isn't sent yet" with Send now: the latest earlier day with logged time that was never sent,
/// by Core's day rules. Send now opens that day's timesheet and starts sending it. Hidden when there is none.
/// </summary>
public partial class UnsentEarlierDayView : UserControl
{
    private WidgetFlow? _flow;
    private Action? _back;
    private UnsentDay? _day;

    public UnsentEarlierDayView()
    {
        InitializeComponent();
    }

    public UnsentDay? Day => _day;

    /// <summary>Looks for the day, optionally only when it is <paramref name="only"/>; <paramref name="back"/> is where the timesheet returns to.</summary>
    public async Task LoadAsync(WidgetFlow flow, Action back, DateOnly? only = null)
    {
        _flow = flow;
        _back = back;
        var day = await flow.GetUnsentEarlierDayAsync();
        if (day == null || (only != null && day.Day != only))
        {
            _day = null;
            Visibility = Visibility.Collapsed;
            return;
        }

        _day = day;
        var today = DateOnly.FromDateTime(flow.Scheduler.Now);
        TitleText.Text = $"{Format.RelativeDay(day.Day, today)} isn't sent yet";
        DetailText.Text = $"{Format.Date(day.Day)} · {Format.Duration(day.Logged)} logged";
        AutomationProperties.SetName(SendButton, $"Send {Format.Date(day.Day)} now");
        Visibility = Visibility.Visible;
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_flow == null || _back == null || _day == null)
            return;
        SendButton.IsEnabled = false;
        try
        {
            await _flow.SendNowAsync(_day.Day, _back);
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }
}
