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

namespace TimePlanner.Widget
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static readonly Thickness ShadowMargin = new(48, 48, 48, 72);

        private const double ScreenInset = 16;

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

        public MainWindow()
        {
            InitializeComponent();
            SizeChanged += (_, _) => Reposition();
            LocationChanged += (_, _) => Reanchor();
            Closed += (_, _) => Application.Current.Shutdown();
        }


        public IWidgetHost OpenSlot()
        {
            var slot = new Slot(this);
            _shown = slot;
            return slot;
        }

        public void UseSlot(IWidgetHost slot) => _shown = (Slot)slot;

        private sealed class Slot(MainWindow window) : IWidgetHost
        {
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
            Host.Content = screen;
            if (!IsVisible)
                Show();

            UpdateLayout();
            Reposition();

            if (activate)
            {
                Activate();
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    if (Host.Content == screen && IsActive)
                        screen.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                });
            }
        }

        private void HideWidget()
        {
            Hide();
            Host.Content = null;
        }


        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            DeviceLines.Apply(VisualTreeHelper.GetDpi(this));
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
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
            _anchor ??= new Point(area.Right - ScreenInset, area.Bottom - ScreenInset);
            var anchor = _anchor.Value;

            var left = _corner is Corner.TopLeft or Corner.BottomLeft ? anchor.X - m.Left : anchor.X - ActualWidth + m.Right;
            var top = _corner is Corner.TopLeft or Corner.TopRight ? anchor.Y - m.Top : anchor.Y - ActualHeight + m.Bottom;

            left = Math.Max(area.Left - m.Left, Math.Min(left, area.Right - ActualWidth + m.Right));
            top = Math.Max(area.Top - m.Top, Math.Min(top, area.Bottom - ActualHeight + m.Bottom));

            _positioning = true;
            Left = left;
            Top = top;
            _positioning = false;
        }

        private void Reanchor()
        {
            if (_positioning || !IsVisible || ActualWidth <= 0)
                return;

            var area = WorkArea();
            var m = ShadowMargin;
            var card = new Rect(Left + m.Left, Top + m.Top, ActualWidth - m.Left - m.Right, ActualHeight - m.Top - m.Bottom);
            var right = card.Left + card.Width / 2 > area.Left + area.Width / 2;
            var bottom = card.Top + card.Height / 2 > area.Top + area.Height / 2;

            _corner = (right, bottom) switch
            {
                (true, true) => Corner.BottomRight,
                (true, false) => Corner.TopRight,
                (false, true) => Corner.BottomLeft,
                _ => Corner.TopLeft,
            };
            _anchor = new Point(right ? card.Right : card.Left, bottom ? card.Bottom : card.Top);
        }

        private Rect WorkArea()
        {
            var handle = new WindowInteropHelper(this).Handle;
            var source = PresentationSource.FromVisual(this);
            if (handle == IntPtr.Zero || source?.CompositionTarget == null || _anchor == null)
                return SystemParameters.WorkArea;

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(handle, MonitorDefaultToNearest), ref info))
                return SystemParameters.WorkArea;

            var pixels = info.Work;
            var fromDevice = source.CompositionTarget.TransformFromDevice;
            var topLeft = fromDevice.Transform(new Point(pixels.Left, pixels.Top));
            var bottomRight = fromDevice.Transform(new Point(pixels.Right, pixels.Bottom));
            return new Rect(topLeft, bottomRight);
        }


        private const uint MonitorDefaultToNearest = 2;

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

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