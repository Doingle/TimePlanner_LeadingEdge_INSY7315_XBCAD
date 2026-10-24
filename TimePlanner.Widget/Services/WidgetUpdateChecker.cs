using TimePlanner.Core.Sync;

namespace TimePlanner.Widget.Services;

/// <summary>One optional notice per run; checking and dismissal never participate in sending time.</summary>
public sealed class WidgetUpdateChecker(DashboardClient client, string installedVersion) : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private bool _checking;
    private bool _dismissed;
    private bool _disposed;
    public WidgetRelease? Available { get; private set; }
    public event EventHandler? Changed;

    public async Task CheckAsync()
    {
        if (_checking || _dismissed || _disposed || Available != null)
            return;
        _checking = true;
        try
        {
            var release = await client.GetWidgetReleaseAsync(_shutdown.Token);
            if (!_dismissed && !_disposed && release?.IsNewerThan(installedVersion) == true)
            {
                Available = release;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally { _checking = false; }
    }

    public void Dismiss()
    {
        _dismissed = true;
        Available = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _disposed = true;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
