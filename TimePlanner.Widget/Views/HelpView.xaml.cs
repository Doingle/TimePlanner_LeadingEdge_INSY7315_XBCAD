using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TimePlanner.Widget.Views;

public partial class HelpView : UserControl
{
    private readonly Action _back;

    public HelpView(Action back)
    {
        InitializeComponent();
        _back = back;
        QuestionsScroll.MaxHeight = Math.Clamp(SystemParameters.WorkArea.Height - 160, 120, 440);
        Questions.ItemsSource = Topics;
    }

    public sealed record Topic(string Question, string Answer, bool InitiallyExpanded = false);

    private static readonly Topic[] Topics =
    [
        new("How do I move the widget?",
            "Drag the small bar at the top of the window.\n\nTo move the small timer, hold the right mouse button on it and drag. Each one stays where you leave it.", true),
        new("How do I start my day?",
            "Choose how often you want a check-in, then select Start tracking. A check-in asks what you have been working on."),
        new("What does ‘Log time since…’ mean?",
            "It lets you record what you did since your last check-in. The time on the button is where that period begins. You can log before the next reminder."),
        new("Where did the window go?",
            "After five seconds with your mouse away, the timer returns to its small idle shape. Hover over it for three seconds to open it. Moving away cancels the wait.\n\nYou can also click the Time Planner icon near the Windows clock. If it is hidden, select the little arrow there first."),
        new("How do I take a break?",
            "Select the pause button beside the countdown. Select it again when you are ready to carry on. Paused time is excluded from your work time. Lunch breaks are not added automatically."),
        new("How do I change my check-in time?",
            "Open Settings and change Check in every. Time that has already passed still counts, so the countdown may be shorter than the interval you chose. If that interval has already passed, a check-in may be due straight away."),
        new("How do I fix a time I logged?",
            "Open Timesheet from the tray menu, choose the day, then click the entry you want to change. Make your changes and select Save. If you already sent that day, send it again to update the dashboard."),
        new("How do I change the small timer?",
            "Open Settings, then Advanced options. Choose a pill, ring or dot. You can also make it see-through or hide it. When hidden, use the Time Planner icon near the Windows clock to reopen it."),
        new("Does closing the app end my day?",
            "No. The X asks you to confirm before quitting. Your day stays open, and there are no check-ins while the app is closed. You can log the time when you return.\n\nUse End day when you have finished working."),
        new("How do I send my timesheet?",
            "Right-click the Time Planner icon near the Windows clock and choose Timesheet. Check your entries, then select Send day. Follow the sign-in instructions if asked.\n\nLogging time and closing the app do not send it for you."),
        new("What if my timesheet does not send?",
            "Read the message shown in Timesheet. Check your internet connection and sign in if asked, then try Send day again. Wait for confirmation that it was sent. If it still fails, ask your team for help and tell them what the message says."),
        new("Can it open when I sign in to Windows?",
            "Open Settings, then Advanced options, and turn on Start with Windows. If Windows cannot apply the setting, a message will appear there."),
        new("Voice notes and Build my day",
            "Speak your check-in updates out loud, and let AI build your timesheet for the day.\n\nSet it up in Settings › Voice and AI. All voice recordings and models stay on this PC.\n\nYou check and adjust every suggested entry before anything is saved or sent.\n\nTo check your microphone, open Voice and AI › Advanced and select Test microphone."),
    ];

    private void Back_Click(object sender, RoutedEventArgs e) => _back();

    private void View_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        _back();
    }
}
