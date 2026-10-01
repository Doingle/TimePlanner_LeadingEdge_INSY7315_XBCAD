using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{

    public partial class SetupView : UserControl
    {
        private static readonly Dictionary<IdleVisibility, string> VisibilityHelpText = new()
        {
            [IdleVisibility.Visible] = "Always visible in the corner.",
            [IdleVisibility.Faded] = "See-through until you point at it.",
            [IdleVisibility.Hidden] = "In the system tray until a check-in.",
        };

        private static readonly Dictionary<IdleShape, string> ShapeHelpText = new()
        {
            [IdleShape.Pill] = "Shows the time left.",
            [IdleShape.Ring] = "Fills as the time runs out.",
            [IdleShape.Dot] = "Hover to see the time.",
        };

        private const string ShapeOff = "Nothing shows while the widget is hidden.";

        private readonly WidgetFlow _flow;
        private readonly bool _isSettings;
        private readonly List<int> _intervals;
        private readonly bool _ready;

        public SetupView(WidgetFlow flow, bool isSettings)
        {
            InitializeComponent();
            _flow = flow;
            _isSettings = isSettings;
            var settings = flow.Session.Settings;
            var preferences = flow.Session.Preferences;

            if (isSettings)
            {
                SettingsHeader.Visibility = Visibility.Visible;
                IntervalField.Margin = (Thickness)FindResource("GapSection");
                PrimaryButton.Content = "Done";
            }
            ShowWelcome(!isSettings);

            _intervals = CheckInPolicy.ChoicesIncluding(settings.CheckInIntervalMinutes);
            IntervalSelect.ItemsSource = _intervals.Select(Format.Interval).ToList();
            IntervalSelect.SelectedIndex = _intervals.IndexOf(settings.CheckInIntervalMinutes);

            (preferences.IdleVisibility switch
            {
                IdleVisibility.Faded => FadedOption,
                IdleVisibility.Hidden => HiddenOption,
                _ => VisibleOption,
            }).IsChecked = true;

            (preferences.IdleShape switch
            {
                IdleShape.Ring => RingOption,
                IdleShape.Dot => DotOption,
                _ => PillOption,
            }).IsChecked = true;

            SoundSwitch.IsChecked = preferences.SoundOnCheckIn;

            _ready = true;
            ApplyIdleSettings();
            ShowNextCheckIn();
        }

        public void ShowAdvanced(bool moveFocus = false) => Show(advanced: true, moveFocus);

        private void Show(bool advanced, bool moveFocus)
        {
            MainView.Visibility = advanced ? Visibility.Collapsed : Visibility.Visible;
            AdvancedView.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
            ShowWelcome(!_isSettings && !advanced);
            if (moveFocus)
            {
                Control target = advanced ? AdvancedBack : AdvancedRow;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => target.Focus());
            }
        }

        private void ShowWelcome(bool show)
        {
            WelcomeBanner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            Card.GripBrush = (Brush)FindResource(show ? "HandleOnActionBrush" : "HandleBrush");
        }

        private void OpenAdvanced_Click(object sender, RoutedEventArgs e) => Show(advanced: true, moveFocus: true);

        private void CloseAdvanced_Click(object sender, RoutedEventArgs e) => Show(advanced: false, moveFocus: true);

        private void Primary_Click(object sender, RoutedEventArgs e)
        {
            if (_isSettings)
                _flow.ShowTimer(activate: true);
            else
                _flow.StartTracking();
        }

        private void BackToTimer_Click(object sender, RoutedEventArgs e) => _flow.ShowTimer(activate: true);

        private async void Interval_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || IntervalSelect.SelectedIndex < 0)
                return;

            var saving = _flow.ChangeIntervalAsync(_intervals[IntervalSelect.SelectedIndex]);
            ShowNextCheckIn();
            await saving;
        }

        private void ShowNextCheckIn()
        {
            var scheduler = _flow.Scheduler;
            var settings = _flow.Session.Settings;
            var next = scheduler.NextCheckIn(scheduler.Now, TimeSpan.FromMinutes(settings.CheckInIntervalMinutes));
            var lead = _isSettings ? "Next check-in moves to " : "First check-in at ";

            IntervalHelp.Text = next is { } at ? lead + Format.Clock(at)
                : scheduler.IsPaused ? "The interval counts from when you resume the timer."
                : $"No check-ins after {Format.Clock(DateTime.Today + settings.WorkdayEnd.ToTimeSpan())} today.";
        }

        private void Visibility_Checked(object sender, RoutedEventArgs e)
        {
            if (!_ready)
                return;
            _flow.Session.Preferences.IdleVisibility = sender == HiddenOption ? IdleVisibility.Hidden
                : sender == FadedOption ? IdleVisibility.Faded
                : IdleVisibility.Visible;
            _flow.SavePreferences();
            ApplyIdleSettings();
        }

        private void Shape_Checked(object sender, RoutedEventArgs e)
        {
            if (!_ready)
                return;
            _flow.Session.Preferences.IdleShape = sender == RingOption ? IdleShape.Ring
                : sender == DotOption ? IdleShape.Dot
                : IdleShape.Pill;
            _flow.SavePreferences();
            ApplyIdleSettings();
        }

        private void Sound_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready)
                return;
            _flow.Session.Preferences.SoundOnCheckIn = SoundSwitch.IsChecked == true;
            _flow.SavePreferences();
        }

        private void ApplyIdleSettings()
        {
            var preferences = _flow.Session.Preferences;
            VisibilityHelp.Text = VisibilityHelpText[preferences.IdleVisibility];

            var off = preferences.IdleVisibility == IdleVisibility.Hidden;
            PillOption.IsEnabled = RingOption.IsEnabled = DotOption.IsEnabled = !off;
            ShapeHelp.Text = off ? ShapeOff : ShapeHelpText[preferences.IdleShape];

            AdvancedSummary.Text = off
                ? "Hidden"
                : $"{preferences.IdleShape}, {(preferences.IdleVisibility == IdleVisibility.Faded ? "faded" : "visible")}";
        }
    }
}
