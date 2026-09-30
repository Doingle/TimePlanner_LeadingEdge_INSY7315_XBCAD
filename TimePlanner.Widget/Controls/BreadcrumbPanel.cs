using System.Windows;
using System.Windows.Controls;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Controls
{
    public partial class EntrySavedView : UserControl
    {
        private readonly WidgetFlow _flow;

        public EntrySavedView(WidgetFlow flow)
        {
            InitializeComponent();
            _flow = flow;
            Subtitle.Text = $"Next check-in at {SampleData.NextCheckIn}.";
        }

        private void KeepTracking_Click(object sender, RoutedEventArgs e) => _flow.KeepTracking();

        private void EndDay_Click(object sender, RoutedEventArgs e) => _flow.EndDay();
    }

}
