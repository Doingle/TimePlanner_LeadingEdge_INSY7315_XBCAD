using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //setup status banner card presenting setup state and actions
    public partial class AiSetupCard : UserControl
    {
        public event Action? InstallRequested;
        public event Action? CancelRequested;
        public event Action? RetryRequested;
        public event Action? UseFasterRequested;
        public event Action? DismissSlowPcRequested;

        //-----------------------------
        //initializes setup card control
        public AiSetupCard()
        {
            InitializeComponent();
        }

        //-----------------------------
        //hides all state subpanels before displaying target state
        private void HideAll()
        {
            NotSetUpPanel.Visibility = Visibility.Collapsed;
            InstallingPanel.Visibility = Visibility.Collapsed;
            ReadyPanel.Visibility = Visibility.Collapsed;
            AttentionPanel.Visibility = Visibility.Collapsed;
            OrgAiPanel.Visibility = Visibility.Collapsed;
        }

        //-----------------------------
        //displays initial unconfigured state prompting installation
        public void SetNotSetUp()
        {
            HideAll();
            NotSetUpPanel.Visibility = Visibility.Visible;
        }

        //-----------------------------
        //displays installation progress with step details and optional cancellation
        public void SetInstalling(int step, int total, string description, double? fraction, bool canCancel)
        {
            HideAll();
            InstallingPanel.Visibility = Visibility.Visible;

            //format progress label with percentage when available
            if (fraction.HasValue)
            {
                var pct = (int)Math.Round(fraction.Value * 100);
                InstallingText.Text = $"Step {step} of {total} · {description} · {pct}%";
                ProgressBar.Value = Math.Clamp(fraction.Value, 0.0, 1.0);
            }
            //otherwise format step and description without percentage
            else
            {
                InstallingText.Text = $"Step {step} of {total} · {description}";
                ProgressBar.Value = 0.0;
            }

            //configure cancel button visibility
            CancelButton.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        }

        //-----------------------------
        //displays ready state with hardware execution summary and optional slow pc hint
        public void SetReady(string hardwareSummary, bool showSlowPcHint = false)
        {
            HideAll();
            ReadyPanel.Visibility = Visibility.Visible;
            ReadyText.Text = $"Ready · {hardwareSummary}";
            SlowPcPanel.Visibility = showSlowPcHint ? Visibility.Visible : Visibility.Collapsed;
        }

        //-----------------------------
        //displays problem state prompting user resolution
        public void SetNeedsAttention(string problem, string actionLabel = "Try again")
        {
            HideAll();
            AttentionPanel.Visibility = Visibility.Visible;
            AttentionText.Text = problem;
            RetryButton.Content = actionLabel;
            RetryButton.ToolTip = actionLabel;
            AutomationProperties.SetName(RetryButton, actionLabel);
        }

        //-----------------------------
        //displays organisation ai delegation summary
        public void SetOrganisationAi()
        {
            HideAll();
            OrgAiPanel.Visibility = Visibility.Visible;
        }

        //-----------------------------
        //handles install button click
        private void Install_Click(object sender, RoutedEventArgs e)
        {
            InstallRequested?.Invoke();
        }

        //-----------------------------
        //handles cancel button click
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CancelRequested?.Invoke();
        }

        //-----------------------------
        //handles retry button click
        private void Retry_Click(object sender, RoutedEventArgs e)
        {
            RetryRequested?.Invoke();
        }

        //-----------------------------
        //handles use faster button click
        private void UseFaster_Click(object sender, RoutedEventArgs e)
        {
            UseFasterRequested?.Invoke();
        }

        //-----------------------------
        //handles dismiss slow pc hint button click
        private void DismissSlowPc_Click(object sender, RoutedEventArgs e)
        {
            DismissSlowPcRequested?.Invoke();
        }
    }
}
//------------------------------EOF-----------------------------\\
