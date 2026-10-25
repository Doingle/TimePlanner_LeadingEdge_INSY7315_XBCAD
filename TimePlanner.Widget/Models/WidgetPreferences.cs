using TimePlanner.Core.Ai;

namespace TimePlanner.Widget.Models
{
    public sealed class WidgetPreferences
    {
        public IdleVisibility IdleVisibility { get; set; } = IdleVisibility.Visible;
        public IdleShape IdleShape { get; set; } = IdleShape.Pill;
        public bool SoundOnCheckIn { get; set; } = true;
        public AiSettings Ai { get; set; } = new();
        // On for a first run and for a widget.json saved before the setting existed; a saved choice is kept on every update
        public bool StartWithWindows { get; set; } = true;
        public bool VoiceTipDismissed { get; set; }
        public WidgetPosition? IdlePosition { get; set; }
        public WidgetPosition? MainPosition { get; set; }
    }
}
