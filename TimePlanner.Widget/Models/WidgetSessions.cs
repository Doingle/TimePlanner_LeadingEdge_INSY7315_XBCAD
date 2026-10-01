using System;
using System.Collections.Generic;
using System.Text;
using TimePlanner.Core.Domain.Entities;

namespace TimePlanner.Widget.Models
{
    public enum IdleVisibility
    {
        Visible,
        Faded,
        Hidden,
    }

    public enum IdleShape
    {
        Pill,
        Ring,
        Dot,
    }

    public sealed class WidgetSession
    {
        public AppUser User { get; set; } = new() { Settings = new UserSettings() };
        public UserSettings Settings => User.Settings ??= new UserSettings();
        public WidgetPreferences Preferences { get; set; } = new();
        public int? ProjectId { get; set; }

        public IReadOnlyList<string> Activity { get; set; } = [];
    }
}
