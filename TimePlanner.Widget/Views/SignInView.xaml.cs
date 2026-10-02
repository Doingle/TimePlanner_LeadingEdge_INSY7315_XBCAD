using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TimePlanner.Core.Sync;

namespace TimePlanner.Widget.Views
{
    //-----------------------------
    //sign in screen for dashboard access
    public partial class SignInView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly Func<Task> _afterSignIn;
        private readonly Action _back;

        //-----------------------------
        //creates a new sign in view
        public SignInView(WidgetFlow flow, Func<Task> afterSignIn, Action back)
        {
            InitializeComponent();
            _flow = flow;
            _afterSignIn = afterSignIn;
            _back = back;
        }

        //-----------------------------
        //handles sign in button click
        private async void SignIn_Click(object sender, RoutedEventArgs e)
        {
            ErrorLine.Visibility = Visibility.Collapsed;
            SignInButton.IsEnabled = false;

            try
            {
                var email = EmailBox.Text;
                var password = PasswordBox.Password;
                var outcome = await _flow.SignInToDashboardAsync(email, password);

                //check outcome status
                if (outcome.Status == SignInStatus.SignedIn)
                {
                    await _afterSignIn();
                }
                else
                {
                    ErrorText.Text = outcome.Message ?? "Sign in failed.";
                    ErrorLine.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                SignInButton.IsEnabled = true;
            }
        }

        //-----------------------------
        //handles cancel button click
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _back();
        }
    }
}
//------------------------------EOF-----------------------------\\
