using System.Windows;
using System.Windows.Controls;

namespace TimePlanner.Widget.Views;

public partial class UnsentDayReminderView : UserControl
{
    private WidgetFlow? _flow;
    private DateOnly? _day;

    public UnsentDayReminderView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_flow != null)
            {
                _flow.Reminder.Changed += ReminderChanged;
                UpdateNotice();
            }
        };
        Unloaded += (_, _) =>
        {
            if (_flow != null)
                _flow.Reminder.Changed -= ReminderChanged;
        };
    }

    /// <summary>Raised whenever the row shows or hides, so Day ended can swap its own Open timesheet button.</summary>
    public event EventHandler? ShownChanged;

    public bool IsShown => Visibility == Visibility.Visible;

    public void Initialise(WidgetFlow flow, DateOnly day)
    {
        _flow = flow;
        _day = day;
        UpdateNotice();
    }

    private void ReminderChanged(object? sender, EventArgs e) => UpdateNotice();

    private void UpdateNotice()
    {
        var reminder = _flow!.Reminder;
        var show = _day == reminder.Today && reminder.IsVisible;
        if (IsShown == show)
            return;
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ShownChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_flow == null || !OpenButton.IsEnabled)
            return;
        OpenButton.IsEnabled = false;
        try
        {
            await _flow.OpenReminderTimesheetAsync();
        }
        finally
        {
            OpenButton.IsEnabled = true;
        }
    }
}
