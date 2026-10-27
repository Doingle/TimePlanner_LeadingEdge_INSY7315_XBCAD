using System.IO;
using System.Text;

namespace TimePlanner.Widget.Services
{
    //support detail parameters for diagnostic reporting
    public sealed record SupportInfo(
        string? Version = null,
        string? ProcessPath = null,
        string? OsVersion = null,
        string? Architecture = null,
        string? DashboardUrl = null,
        bool? IsSignedIn = null,
        bool? StartWithWindows = null,
        bool? VoiceEnabled = null,
        string? VoiceModel = null,
        string? DayModel = null,
        string? DayEngine = null,
        string? DayHardware = null,
        string? AiEngineStatus = null,
        bool? AiInstalledByWidget = null,
        string? MicrophoneName = null);

    //-----------------------------
    //builds sanitized diagnostic support summary string
    public static class SupportDetails
    {
        //-----------------------------
        //formats support information into plain text lines omitting sensitive user details
        public static string Build(SupportInfo info)
        {
            var sb = new StringBuilder();

            //format application version and installation type
            var version = string.IsNullOrWhiteSpace(info.Version) ? "unknown" : info.Version.Trim();
            var localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TimePlanner");
            var isInstalled = !string.IsNullOrWhiteSpace(info.ProcessPath)
                && info.ProcessPath.StartsWith(localPrograms, StringComparison.OrdinalIgnoreCase);
            var installType = isInstalled ? "installed" : "portable";
            sb.AppendLine($"Time Planner {version} ({installType})");

            //format operating system version and architecture
            var osRaw = string.IsNullOrWhiteSpace(info.OsVersion) ? "Windows" : info.OsVersion.Trim();
            var os = osRaw.StartsWith("Windows", StringComparison.OrdinalIgnoreCase) ? osRaw : $"Windows {osRaw}";
            var arch = string.IsNullOrWhiteSpace(info.Architecture)
                ? (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")
                : info.Architecture.Trim();
            sb.AppendLine($"{os} {arch}");

            //format dashboard host only omitting tokens paths or user info
            var dashboardHost = ExtractHost(info.DashboardUrl);
            sb.AppendLine($"Dashboard: {dashboardHost}");

            //format sign in status
            var signedIn = info.IsSignedIn.HasValue ? (info.IsSignedIn.Value ? "yes" : "no") : "unknown";
            sb.AppendLine($"Signed in: {signedIn}");

            //format start with windows setting
            var startup = info.StartWithWindows.HasValue ? (info.StartWithWindows.Value ? "on" : "off") : "unknown";
            sb.AppendLine($"Start with Windows: {startup}");

            //format voice notes state and model
            var voiceState = info.VoiceEnabled.HasValue ? (info.VoiceEnabled.Value ? "on" : "off") : "unknown";
            var voiceModel = string.IsNullOrWhiteSpace(info.VoiceModel) ? "unknown" : info.VoiceModel.Trim();
            sb.AppendLine($"Voice notes: {voiceState} · voice model: {voiceModel}");

            //format day model engine and hardware configuration
            var dayModel = string.IsNullOrWhiteSpace(info.DayModel) ? "unknown" : info.DayModel.Trim();
            var dayEngine = string.IsNullOrWhiteSpace(info.DayEngine) ? "unknown" : info.DayEngine.Trim();
            var dayHardware = string.IsNullOrWhiteSpace(info.DayHardware) ? "unknown" : info.DayHardware.Trim();
            sb.AppendLine($"Day model: {dayModel} · engine: {dayEngine} · hardware: {dayHardware}");

            //format ai engine execution state and widget management
            var engineStatus = string.IsNullOrWhiteSpace(info.AiEngineStatus) ? "unknown" : info.AiEngineStatus.Trim();
            var widgetManaged = info.AiInstalledByWidget.HasValue ? (info.AiInstalledByWidget.Value ? "yes" : "no") : "unknown";
            sb.AppendLine($"AI engine: {engineStatus} · installed by Time Planner: {widgetManaged}");

            //format microphone selection
            var microphone = string.IsNullOrWhiteSpace(info.MicrophoneName) ? "unknown" : info.MicrophoneName.Trim();
            sb.Append($"Microphone: {microphone}");

            return sb.ToString();
        }

        //-----------------------------
        //extracts sanitized host from url string omitting credentials port and path
        public static string ExtractHost(string? url)
        {
            //check if url is null or whitespace
            if (string.IsNullOrWhiteSpace(url))
            {
                return "unknown";
            }

            var text = url.Trim();
            //strip scheme if present
            var schemeIdx = text.IndexOf("://", StringComparison.Ordinal);
            if (schemeIdx >= 0)
            {
                text = text[(schemeIdx + 3)..];
            }

            //strip user info up to last at sign
            var atIdx = text.LastIndexOf('@');
            if (atIdx >= 0)
            {
                text = text[(atIdx + 1)..];
            }

            //strip path and query
            var slashIdx = text.IndexOf('/');
            if (slashIdx >= 0)
            {
                text = text[..slashIdx];
            }

            //strip port
            var colonIdx = text.IndexOf(':');
            if (colonIdx >= 0)
            {
                text = text[..colonIdx];
            }

            return string.IsNullOrWhiteSpace(text) ? "unknown" : text;
        }
    }
}
//------------------------------EOF-----------------------------\\
