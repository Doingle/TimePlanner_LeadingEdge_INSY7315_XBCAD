using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TimePlanner.Widget.Controls;
using TimePlanner.Widget.Services;

namespace TimePlanner.Widget
{
    public sealed class TrayIconManager : IDisposable
    {
        private const int SmallIconWidth = 49;   

        private readonly TaskbarIcon _icon;
        private readonly ContextMenu _menu;

        //-----------------------------
        // Menu actions share the widget's navigation and confirmation flow.
        public TrayIconManager(Action show, Action log, Action timesheet, Action settings, Action help, Action exit, Func<bool> canLog, Action? devCheckIn = null
#if DEBUG
            , Func<ScreenPreview, Task>? showPreview = null
#endif
        )
        {
            _menu = CreateMenu(show, log, timesheet, settings, help, exit, canLog, devCheckIn
#if DEBUG
                , showPreview
#endif
            );
            _icon = new TaskbarIcon
            {
                Icon = LoadIcon(),
                ToolTipText = DevMode.Enabled ? "Time Planner (developer mode)" : "Time Planner",
                ContextMenu = _menu,
                NoLeftClickDelay = true,
            };
            _icon.TrayLeftMouseUp += (_, _) => show();
        }

        public static ContextMenu CreateMenu(Action show, Action log, Action timesheet, Action settings, Action help, Action exit, Func<bool> canLog, Action? devCheckIn = null
#if DEBUG
            , Func<ScreenPreview, Task>? showPreview = null
#endif
        )
        {
            var menu = new TrayMenu();
            Icon MenuIcon(IconKind kind) => new() { Kind = kind, Size = (double)Application.Current.FindResource("SizeIconSm") };
            var open = new MenuItem { Header = "Open Time Planner", FontWeight = FontWeights.Bold, Icon = MenuIcon(IconKind.Clock) };
            open.Click += (_, _) => show();
            menu.Items.Add(open);

            var logItem = new MenuItem { Header = "Log time since last check-in", IsEnabled = canLog(), Icon = MenuIcon(IconKind.Edit) };
            logItem.Click += (_, _) => log();
            menu.Items.Add(logItem);
            void RefreshLogAvailability()
            {
                logItem.IsEnabled = canLog();
                logItem.Opacity = logItem.IsEnabled ? 1 : 0.5;
            }
            RefreshLogAvailability();
            menu.Opened += (_, _) => RefreshLogAvailability();

            //opens timesheet view for current day
            var timesheetItem = new MenuItem { Header = "Timesheet", Icon = MenuIcon(IconKind.List) };
            timesheetItem.Click += (_, _) => timesheet();
            menu.Items.Add(timesheetItem);

            var settingsItem = new MenuItem { Header = "Settings", Icon = MenuIcon(IconKind.Settings) };
            settingsItem.Click += (_, _) => settings();
            menu.Items.Add(settingsItem);

            //dev feature not available to users
            if (DevMode.Enabled && devCheckIn != null)
            {
                menu.Items.Add(new Separator { Style = (Style)Application.Current.FindResource("Menu.Separator") });
                var devItem = new MenuItem { Header = "Developer: Check in now", Icon = MenuIcon(IconKind.Clock) };
                devItem.Click += (_, _) => devCheckIn();
                menu.Items.Add(devItem);
            }

#if DEBUG
            //dev feature not available to users
            if (DevMode.Enabled && showPreview != null)
            {
                var previewMenu = new MenuItem { Header = "Preview screens" };
                foreach (var group in ScreenPreviews.All.GroupBy(p => p.Section))
                {
                    var sectionItem = new MenuItem { Header = group.Key };
                    foreach (var p in group)
                    {
                        var item = new MenuItem { Header = p.Title };
                        item.Click += async (_, _) => await showPreview(p);
                        sectionItem.Items.Add(item);
                    }
                    previewMenu.Items.Add(sectionItem);
                }
                menu.Items.Add(previewMenu);
            }
#endif

            menu.Items.Add(new Separator { Style = (Style)Application.Current.FindResource("Menu.Separator") });
            var helpItem = new MenuItem { Header = "Help", Icon = MenuIcon(IconKind.Help) };
            helpItem.Click += (_, _) => help();
            menu.Items.Add(helpItem);

            menu.Items.Add(new Separator { Style = (Style)Application.Current.FindResource("Menu.Separator") });
            var exitItem = new MenuItem { Header = "Exit", Icon = MenuIcon(IconKind.Close) };
            exitItem.Click += (_, _) => exit();
            menu.Items.Add(exitItem);
            return menu;
        }

#if DEBUG
        public void ShowPreviewMenu(FrameworkElement target)
        {
            _menu.PlacementTarget = target;
            _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            _menu.IsOpen = true;
        }

#endif

        public void Dispose() => _icon.Dispose();

        private static System.Drawing.Icon LoadIcon()
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/TimePlanner.Widget;component/Assets/insy.ico"));
            if (resource == null)
                return System.Drawing.SystemIcons.Application;

            using var stream = resource.Stream;
            var size = GetSystemMetrics(SmallIconWidth);
            return new System.Drawing.Icon(stream, size, size);
        }

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int GetSystemMetrics(int index);
    }
}
