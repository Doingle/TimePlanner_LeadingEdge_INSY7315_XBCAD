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
using TimePlanner.Widget.Services;
using System.Windows.Threading;

namespace TimePlanner.Widget.Views
{
    /// <summary>
    /// Interaction logic for IdleView.xaml
    /// </summary>
    public partial class IdleView : UserControl
    {
        private readonly WidgetFlow _flow;
        private readonly IdleHover _hover = new(TimeProvider.System);
        private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
        private bool _dragging;
        private Point _dragPointer;
        private Point _dragWindow;
        private MainWindow? _window;

        public IdleView(WidgetFlow flow)
        {
            InitializeComponent();
            _flow = flow;

            var preferences = flow.Session.Preferences;
            Pill.Visibility = preferences.IdleShape == IdleShape.Pill ? Visibility.Visible : Visibility.Collapsed;
            Ring.Visibility = preferences.IdleShape == IdleShape.Ring ? Visibility.Visible : Visibility.Collapsed;
            Dot.Visibility = preferences.IdleShape == IdleShape.Dot ? Visibility.Visible : Visibility.Collapsed;
            HoverFill.Round = preferences.IdleShape != IdleShape.Pill;
            _hoverTimer.Tick += (_, _) =>
            {
                if (!Root.IsMouseOver || _dragging) { CancelHover(); return; }
                HoverFill.Fraction = _hover.Fraction;
                if (_hover.Fraction >= 1)
                {
                    CancelHover();
                    _flow.ShowTimer();
                }
            };

            // Faded: see-through until pointed at
            if (preferences.IdleVisibility == IdleVisibility.Faded)
                Root.Opacity = (double)FindResource("IdleFadedOpacity");

            ShowTime();
            Loaded += (_, _) => flow.Scheduler.Ticked += Scheduler_Ticked;
            Unloaded += (_, _) =>
            {
                flow.Scheduler.Ticked -= Scheduler_Ticked;
                CancelHover();
                FinishDrag();
            };
        }

        private void Scheduler_Ticked(object? sender, EventArgs e) => ShowTime();

        private void ShowTime()
        {
            // A day off has no check-ins: the pill counts up the time not logged yet
            if (_flow.Scheduler.IsDayOff)
            {
                var unlogged = _flow.Scheduler.Unlogged;
                PillTime.Text = Format.Countdown(unlogged);
                RingProgress.Fraction = 0;
                AutomationProperties.SetName(Pill, $"{Format.Spoken(unlogged)} not logged yet, no check-ins today");
                AutomationProperties.SetName(Ring, "No check-ins today");
                return;
            }

            var left = _flow.Scheduler.TimeLeft;
            PillTime.Text = left is { } time ? Format.Countdown(time) : "--:--";
            RingProgress.Fraction = _flow.Scheduler.Elapsed;

            var spoken = left is { } l ? Format.Spoken(l) : null;
            AutomationProperties.SetName(Pill, spoken != null ? $"{spoken} until your next check-in" : "No more check-ins today");
            AutomationProperties.SetName(Ring, spoken != null ? $"Next check-in in {spoken}" : "No more check-ins today");
        }

        private void Root_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_dragging || e.RightButton == MouseButtonState.Pressed) return;
            Root.Opacity = 1;
            _hover.Start();
            _hoverTimer.Start();
        }

        private void CancelHover()
        {
            _hoverTimer.Stop();
            _hover.Cancel();
            HoverFill.Fraction = 0;
        }

        private void Root_MouseLeave(object sender, MouseEventArgs e)
        {
            CancelHover();
            if (!_dragging && _flow.Session.Preferences.IdleVisibility == IdleVisibility.Faded)
                Root.Opacity = (double)FindResource("IdleFadedOpacity");
        }

        private void Root_RightDown(object sender, MouseButtonEventArgs e)
        {
            CancelHover();
            _window = Window.GetWindow(this) as MainWindow;
            if (_window == null || !Root.CaptureMouse()) return;
            _dragging = true;
            _dragPointer = PointToScreen(e.GetPosition(this));
            _dragWindow = new Point(_window.Left, _window.Top);
            Root.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private void Root_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || _window == null) return;
            if (e.RightButton != MouseButtonState.Pressed) { FinishDrag(); return; }
            var delta = PointToScreen(e.GetPosition(this)) - _dragPointer;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is { } target)
                delta = target.TransformFromDevice.Transform(delta);
            _window.Left = _dragWindow.X + delta.X;
            _window.Top = _dragWindow.Y + delta.Y;
            e.Handled = true;
        }

        private void Root_RightUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            FinishDrag();
            e.Handled = true;
        }

        private void Root_LostCapture(object sender, MouseEventArgs e) => FinishDrag();

        private void FinishDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            Root.ReleaseMouseCapture();
            Root.Cursor = Cursors.Hand;
            _window?.SavePosition();
            _window = null;
            CancelHover();
        }
    }
}
