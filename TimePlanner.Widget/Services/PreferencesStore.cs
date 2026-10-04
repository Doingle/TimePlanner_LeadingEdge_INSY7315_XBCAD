using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Services
{
    // Without a path (the previews) the preferences are kept in memory only
    public class PreferencesStore(string? path)
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            // Choices by name only: a number in the file is not one of the options
            Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
        };

        public WidgetPreferences Load()
        {
            if (path == null || !File.Exists(path))
                return new WidgetPreferences();

            try
            {
                var loaded = JsonSerializer.Deserialize<WidgetPreferences>(File.ReadAllText(path), Options);
                return loaded != null && IsValid(loaded) ? loaded : new WidgetPreferences();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                // A damaged, edited or locked file falls back to the defaults rather than stopping the widget
                return new WidgetPreferences();
            }
        }

        /// <summary>Every choice is one the widget offers ("Pill, Ring" would otherwise read as a mix of two).</summary>
        private static bool IsValid(WidgetPreferences preferences) =>
            Enum.IsDefined(preferences.IdleVisibility) && Enum.IsDefined(preferences.IdleShape);

        public void Save(WidgetPreferences preferences)
        {
            if (path == null)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(preferences, Options));
        }
    }
}
