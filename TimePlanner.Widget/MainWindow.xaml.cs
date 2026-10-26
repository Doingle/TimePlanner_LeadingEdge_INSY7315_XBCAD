using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TimePlanner.Widget.Controls;
using TimePlanner.Widget.Models;
using TimePlanner.Widget.Services;
using TimePlanner.Widget.Views;

namespace TimePlanner.Widget
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static readonly Thickness ShadowMargin = new(48, 48, 48, 72);

        private const double ScreenInset = 16;

        // Space under the main card's top for its tallest screen, so going from screen to screen never moves the top
        public const double MainRoom = 680;

        private enum Corner
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
        }

        private Corner _corner = Corner.BottomRight;
        private Point? _anchor;
        private bool _positioning;
        private Slot? _shown;

        // Widget positioning and desktop visibility
        private bool _idle;
        private bool _presenting;
        private bool _positionDirty;
        private Services.DesktopVisibility? _desktop;

        // Widget update checking
        private WidgetUpdateChecker? _updates;

        public MainWindow()
        {
            InitializeComponent();

            SizeChanged += (_, _) => Reposition();
            LocationChanged += (_, _) => Reanchor();

            Closed += (_, _) =>
            {
                _desktop?.Dispose();
                Application.Current.Shutdown();
            };

            StateChanged += (_, _) =>
            {
                if (WindowState == WindowState.Minimized && Host.Content != null)
                    Dispatcher.BeginInvoke(
                        DispatcherPriority.ApplicationIdle,
                        () => RestoreVisible(activate: false));
            };

            UpdateNotice.Dismissed += (_, _) => _updates?.Dismiss();
        }

        public void AttachUpdates(WidgetUpdateChecker updates)
        {
            if (_updates != null)
                _updates.Changed -= UpdatesChanged;

            _updates = updates;
            _updates.Changed += UpdatesChanged;

            UpdateNoticeVisibility();
        }

        private void UpdatesChanged(object? sender, EventArgs e)
            => UpdateNoticeVisibility();

        private void UpdateNoticeVisibility()
        {
            // Defer while hidden/collapsed or working in sign-in,
            // logging and Timesheet screens.
            var show = _updates?.Available != null
                && IsVisible
                && Host.Content is SetupView or TimerView or DayEndedView;

            var wasVisible = UpdateNotice.Visibility == Visibility.Visible;

            UpdateNotice.Visibility = show
                ? Visibility.Visible
                : Visibility.Collapsed;

            Host.IsEnabled = !show;

            if (show && !wasVisible && IsActive)
            {
                UpdateNotice.MoveFocus(
                    new TraversalRequest(FocusNavigationDirection.First));
            }
            else if (!show && wasVisible && IsActive
                     && Host.Content is FrameworkElement screen)
            {
                screen.MoveFocus(
                    new TraversalRequest(FocusNavigationDirection.First));
            }
        }

        public IWidgetHost OpenSlot()
        {
            SavePosition();

            var slot = new Slot(this);
            _shown = slot;

            return slot;
        }

        public void UseSlot(IWidgetHost slot)
        {
            if (_shown == slot)
                return;

            SavePosition();

            _shown = (Slot)slot;
            LoadPosition();
        }

        private sealed class Slot(MainWindow window) : IWidgetHost
        {
            public WidgetPreferences? Preferences { get; private set; }
            public Action? Save { get; private set; }

            public void ConfigurePositions(
                WidgetPreferences preferences,
                Action save)
            {
                Preferences = preferences;
                Save = save;

                if (window._shown == this)
                    window.LoadPosition();
            }

            public void Present(FrameworkElement screen, bool activate = true)
            {
                if (window._shown == this)
                    window.Present(screen, activate);
            }

            public void HideWidget()
            {
                if (window._shown == this)
                    window.HideWidget();
            }
        }

        private void Present(FrameworkElement screen, bool activate)
        {
            // Preserve the current widget position before changing screens.
            SavePosition();

            // Dismiss an existing update notice when navigating.
            if (UpdateNotice.Visibility == Visibility.Visible)
                _updates?.Dismiss();

            _presenting = true;
            _idle = screen is IdleView;

            LoadPosition();

            Host.Content = screen;

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            if (!IsVisible)
                Show();

            UpdateLayout();
            Reposition();

            _presenting = false;

            // Refresh the update notice for the new screen.
            UpdateNoticeVisibility();

            if (activate)
            {
                RestoreVisible(activate: true);

                Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    () =>
                    {
                        if (Host.Content == screen && IsActive)
                        {
                            var target =
                                UpdateNotice.Visibility == Visibility.Visible
                                    ? (FrameworkElement)UpdateNotice
                                    : screen;

                            target.MoveFocus(
                                new TraversalRequest(
                                    FocusNavigationDirection.First));
                        }
                    });
            }
        }

        private void HideWidget()
        {
            // Save the widget's position before hiding it.
            SavePosition();

            // Dismiss any visible update notice.
            if (UpdateNotice.Visibility == Visibility.Visible)
                _updates?.Dismiss();

            Hide();
            Host.Content = null;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            DeviceLines.Apply(VisualTreeHelper.GetDpi(this));

            HwndSource.FromHwnd(
                new WindowInteropHelper(this).Handle
            )?.AddHook(WindowMessage);

            _desktop = new Services.DesktopVisibility(
                Dispatcher,
                () => RestoreVisible(activate: false));
        }

        protected override void OnDpiChanged(
            DpiScale oldDpi,
            DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);

            DeviceLines.Apply(newDpi);
        }

        private void Reposition()
        {
            if (!IsVisible || ActualWidth <= 0)
                return;

            var area = WorkArea();
            var m = ShadowMargin;

            // The idle shape starts in the top corner; the main card hangs from its top, low enough on the right
            // to sit near the taskbar yet with room below it for the tallest screen
            _anchor ??= new Point(
                area.Right - ScreenInset,
                _idle
                    ? area.Top + ScreenInset
                    : Math.Max(area.Top + ScreenInset, area.Bottom - ScreenInset - MainRoom));

            var anchor = _anchor.Value;

            var location = new WidgetPosition(
                anchor.X,
                anchor.Y,
                _corner is Corner.TopRight or Corner.BottomRight,
                _corner is Corner.BottomLeft or Corner.BottomRight
            ).Locate(
                area,
                new Size(ActualWidth, ActualHeight),
                m);

            _positioning = true;

            Left = location.X;
            Top = location.Y;

            _positioning = false;
        }

        private void Reanchor()
        {
            if (_positioning
                || _presenting
                || WindowState != WindowState.Normal
                || !IsVisible
                || ActualWidth <= 0)
            {
                return;
            }

            var area = WorkArea();
            var m = ShadowMargin;

            var card = new Rect(
                Left + m.Left,
                Top + m.Top,
                ActualWidth - m.Left - m.Right,
                ActualHeight - m.Top - m.Bottom);

            var right =
                card.Left + card.Width / 2 >
                area.Left + area.Width / 2;

            // The idle shape holds whichever edge is nearer; the main card always holds its top, so it stays put from screen to screen
            var bottom =
                _idle &&
                card.Top + card.Height / 2 >
                area.Top + area.Height / 2;

            _corner = (right, bottom) switch
            {
                (true, true) => Corner.BottomRight,
                (true, false) => Corner.TopRight,
                (false, true) => Corner.BottomLeft,
                _ => Corner.TopLeft,
            };

            _anchor = new Point(
                right ? card.Right : card.Left,
                bottom ? card.Bottom : card.Top);

            _positionDirty = true;
        }

        private void LoadPosition()
        {
            var saved = _idle
                ? _shown?.Preferences?.IdlePosition
                : _shown?.Preferences?.MainPosition;

            // A main card saved by its bottom edge (before the card held its top) moves to its top, and is saved that way
            var converted = !_idle && saved?.IsValid == true && saved.Bottom;
            if (converted)
                saved = saved!.PinnedAtTop(MainRoom);

            if (saved?.IsValid == true)
            {
                _anchor = new Point(saved.X, saved.Y);

                _corner = (saved.Right, saved.Bottom) switch
                {
                    (true, true) => Corner.BottomRight,
                    (true, false) => Corner.TopRight,
                    (false, true) => Corner.BottomLeft,
                    _ => Corner.TopLeft,
                };
            }
            else
            {
                _anchor = null;

                _corner = Corner.TopRight;
            }

            _positionDirty = converted;
        }

        public void SavePosition()
        {
            if (!_positionDirty
                || _anchor == null
                || _shown?.Preferences is not { } preferences)
            {
                return;
            }

            Reposition();
            Reanchor();

            var saved = new WidgetPosition(
                _anchor.Value.X,
                _anchor.Value.Y,
                _corner is Corner.TopRight or Corner.BottomRight,
                _corner is Corner.BottomLeft or Corner.BottomRight);

            if (_idle)
                preferences.IdlePosition = saved;
            else
                preferences.MainPosition = saved;

            try
            {
                _shown.Save?.Invoke();
            }
            catch (Exception ex) when (
                ex is System.IO.IOException
                or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "The widget position could not be saved.");
            }

            _positionDirty = false;
        }

        private IntPtr WindowMessage(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            // WM_EXITSIZEMOVE: main card's left drag ended.
            if (message == 0x0232)
                SavePosition();

            // WM_DISPLAYCHANGE: a monitor may have gone away.
            if (message == 0x007E)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    Reposition);
            }

            return IntPtr.Zero;
        }

        private void RestoreVisible(bool activate)
        {
            // Respect the user's Hidden idle preference.
            if (Host.Content == null)
                return;

            _presenting = true;

            WindowState = WindowState.Normal;
            Show();
            Reposition();

            _presenting = false;

            Topmost = false;
            Topmost = true;

            if (activate)
                Activate();
        }

        private Rect WorkArea()
        {
            var handle = new WindowInteropHelper(this).Handle;
            var source = PresentationSource.FromVisual(this);

            if (handle == IntPtr.Zero
                || source?.CompositionTarget == null
                || _anchor == null)
            {
                return SystemParameters.WorkArea;
            }

            var info = new MonitorInfo
            {
                Size = Marshal.SizeOf<MonitorInfo>()
            };

            var point = source.CompositionTarget
                .TransformToDevice
                .Transform(_anchor.Value);

            if (!GetMonitorInfo(
                    MonitorFromPoint(
                        new NativePoint
                        {
                            X = (int)point.X,
                            Y = (int)point.Y
                        },
                        MonitorDefaultToNearest),
                    ref info))
            {
                return SystemParameters.WorkArea;
            }

            var pixels = info.Work;

            var fromDevice =
                source.CompositionTarget.TransformFromDevice;

            var topLeft = fromDevice.Transform(
                new Point(pixels.Left, pixels.Top));

            var bottomRight = fromDevice.Transform(
                new Point(pixels.Right, pixels.Bottom));

            return new Rect(topLeft, bottomRight);
        }

        private const uint MonitorDefaultToNearest = 2;

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr MonitorFromPoint(
            NativePoint point,
            uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(
            IntPtr monitor,
            ref MonitorInfo info);

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Win32Rect Monitor;
            public Win32Rect Work;
            public uint Flags;
        }
    }
}