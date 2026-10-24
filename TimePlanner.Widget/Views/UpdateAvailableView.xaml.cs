using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TimePlanner.Widget.Views;

public partial class UpdateAvailableView : UserControl
{
    // The server supplies a version, never an executable path or arbitrary browser destination.
    public const string ReleasesUrl = "https://github.com/Doingle/TimePlanner_LeadingEdge_INSY7315_XBCAD/releases/latest";
    private readonly Action _openRelease;
    public event EventHandler? Dismissed;

    public UpdateAvailableView() : this(() => Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true })) { }

    public UpdateAvailableView(Action openRelease)
    {
        InitializeComponent();
        _openRelease = openRelease;
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Dismissed?.Invoke(this, EventArgs.Empty);

    private void Notice_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Dismissed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _openRelease();
            Dismissed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ProblemText.Visibility = Visibility.Visible;
        }
    }
}
