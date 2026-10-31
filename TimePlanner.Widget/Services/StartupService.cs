using System.IO;
using System.Security;
using Microsoft.Extensions.Logging;

namespace TimePlanner.Widget.Services
{
    /// <summary>
    /// Keeps the Windows startup entry in line with the "Start with Windows" preference. It runs when the
    /// widget starts (so a moved or updated widget is registered at its new path, and an entry removed
    /// outside the widget comes back) and whenever the setting is switched.
    /// </summary>
    public sealed class StartupService(IStartupRegistration registration, string? programPath, ILogger<StartupService> logger)
    {
        /// <summary>Marks a start by Windows at sign-in, so on a day off the widget can wait in the tray instead of popping up.</summary>
        public const string SignInArgument = "--startup";

        /// <summary>The command Windows runs at sign-in: this program, quoted for paths with spaces, marked as a sign-in start.</summary>
        public string? Command => string.IsNullOrEmpty(programPath) ? null : $"\"{programPath}\" {SignInArgument}";

        /// <summary>
        /// Adds, updates or removes the entry. Returns null when Windows now matches the preference, or a
        /// message for the user when it could not be changed (the widget keeps running either way).
        /// </summary>
        public string? Apply(bool enabled)
        {
            try
            {
                var current = registration.GetCommand();
                if (enabled)
                {
                    if (Command == null)
                        return "The widget couldn't find its own program file, so it can't start with Windows.";
                    if (!string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
                        registration.Register(Command);
                }
                else if (current != null)
                {
                    registration.Unregister();
                }
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
            {
                logger.LogWarning(e, "Could not change the Windows startup entry");
                return enabled
                    ? "Windows didn't allow the widget to start with Windows. Please try again."
                    : "Windows didn't allow the widget to stop starting with Windows. Please try again.";
            }
        }
    }
}
