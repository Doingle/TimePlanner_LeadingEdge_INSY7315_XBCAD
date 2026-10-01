using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace TimePlanner.Widget
{
    public sealed class TrayIconManager : IDisposable
    {
        private const int SmallIconWidth = 49;   

        private readonly TaskbarIcon _icon;
        private readonly ContextMenu _menu = new();

        public TrayIconManager(Action show)
        {
            var open = new MenuItem { Header = "Show INSY", FontWeight = FontWeights.Bold };
            open.Click += (_, _) => show();
            _menu.Items.Add(open);

            _menu.Items.Add(new Separator());
            var exit = new MenuItem { Header = "Exit" };
            exit.Click += (_, _) => Application.Current.Shutdown();
            _menu.Items.Add(exit);

            _icon = new TaskbarIcon
            {
                Icon = LoadIcon(),
                ToolTipText = "INSY",
                ContextMenu = _menu,
                NoLeftClickDelay = true,
            };
            _icon.TrayLeftMouseUp += (_, _) => show();
        }

#if DEBUG
        public void AddPreviews(Action<ScreenPreview> showPreview)
        {
            var previews = new MenuItem { Header = "Preview screens" };
            foreach (var section in ScreenPreviews.All.GroupBy(p => p.Section))
            {
                if (previews.Items.Count > 0)
                    previews.Items.Add(new Separator());
                foreach (var preview in section)
                {
                    var item = new MenuItem { Header = $"{section.Key}: {preview.Title}" };
                    item.Click += (_, _) => showPreview(preview);
                    previews.Items.Add(item);
                }
            }
            _menu.Items.Insert(1, previews);
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
