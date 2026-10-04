using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    /// <summary>
    /// Interaction logic for IdleView.xaml
    /// </summary>
    public partial class IdleView : UserControl
    {
        private readonly WidgetFlow _flow;

        public IdleView(WidgetFlow flow)
        {
            InitializeComponent();
            _flow = flow;

            var preferences = flow.Session.Preferences;
            Pill.Visibility = preferences.IdleShape == IdleShape.Pill ? Visibility.Visible : Visibility.Collapsed;
            Ring.Visibility = preferences.IdleShape == IdleShape.Ring ? Visibility.Visible : Visibility.Collapsed;
            Dot.Visibility = preferences.IdleShape == IdleShape.Dot ? Visibility.Visible : Visibility.Collapsed;

            // Faded: see-through until pointed at
            if (preferences.IdleVisibility == IdleVisibility.Faded)
                Root.Opacity = (double)FindResource("IdleFadedOpacity");

            ShowTime();
            Loaded += (_, _) => flow.Scheduler.Ticked += Scheduler_Ticked;
            Unloaded += (_, _) => flow.Scheduler.Ticked -= Scheduler_Ticked;
        }

        private void Scheduler_Ticked(object? sender, EventArgs e) => ShowTime();

        private void ShowTime()
        {
            var left = _flow.Scheduler.TimeLeft;
            PillTime.Text = left is { } time ? Format.Countdown(time) : "--:--";
            RingProgress.Fraction = _flow.Scheduler.Elapsed;

            var spoken = left is { } l ? Format.Spoken(l) : null;
            AutomationProperties.SetName(Pill, spoken != null ? $"{spoken} until your next check-in" : "No more check-ins today");
            AutomationProperties.SetName(Ring, spoken != null ? $"Next check-in in {spoken}" : "No more check-ins today");
        }

        private void Root_MouseEnter(object sender, MouseEventArgs e) => _flow.ShowTimer();
    }
}
