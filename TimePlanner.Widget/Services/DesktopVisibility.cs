using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace TimePlanner.Widget.Services;

/// <summary>Reasserts the widget above the desktop after Show desktop, without stealing keyboard focus.</summary>
internal sealed class DesktopVisibility : IDisposable
{
    private readonly WinEventProc _callback;
    private readonly IntPtr _hook;
    private readonly DispatcherTimer _settled;
    private bool _disposed;

    public DesktopVisibility(Dispatcher dispatcher, Action restore)
    {
        _settled = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(200) };
        _settled.Tick += (_, _) => { _settled.Stop(); if (!_disposed) restore(); };
        _callback = (_, _, hwnd, _, _, _, _) =>
        {
            if (_disposed || hwnd == IntPtr.Zero) return;
            var name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() is not ("Progman" or "WorkerW")) return;
            dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                _settled.Stop();
                _settled.Start();
            });
        };
        _hook = SetWinEventHook(3, 3, IntPtr.Zero, _callback, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND, out of context.
    }

    public void Dispose()
    {
        _disposed = true;
        _settled.Stop();
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        GC.KeepAlive(_callback);
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
}
