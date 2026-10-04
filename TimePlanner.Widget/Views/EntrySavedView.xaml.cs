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
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Views
{
    /// <summary>
    /// Interaction logic for EntrySavedView.xaml
    /// </summary>
    public partial class EntrySavedView : UserControl
    {
        private readonly WidgetFlow _flow;

        public EntrySavedView(WidgetFlow flow)
        {
            InitializeComponent();
            _flow = flow;

            var scheduler = flow.Scheduler;
            Subtitle.Text = scheduler.PromptAt switch
            {
                null => "Check-ins wait while the timer is paused.",
                { } due when due <= scheduler.Now => "The next check-in is due now.",
                { } next => $"Next check-in at {Format.Clock(next)}.",
            };
        }

        private void KeepTracking_Click(object sender, RoutedEventArgs e) => _flow.KeepTracking();

        private async void EndDay_Click(object sender, RoutedEventArgs e) => await _flow.EndDayAsync();
    }
}
