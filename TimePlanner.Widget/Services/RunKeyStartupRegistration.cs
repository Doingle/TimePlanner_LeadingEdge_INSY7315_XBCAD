using Microsoft.Win32;

namespace TimePlanner.Widget.Services
{
    /// <summary>
    /// A value under this user's Run key (HKCU, so no administrator rights are needed). The widget is a
    /// zip with no installer, so the widget itself keeps the entry, and it is removed with the setting.
    /// </summary>
    public sealed class RunKeyStartupRegistration : IStartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "TimePlanner";

        public string? GetCommand()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }

        public void Register(string command)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }

        public void Unregister()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
