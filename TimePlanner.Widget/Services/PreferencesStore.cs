using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TimePlanner.Core.Ai;
using TimePlanner.Core.Services.Models;
using TimePlanner.Widget.Models;

namespace TimePlanner.Widget.Services
{
    //-----------------------------
    //manages persistent storage of widget preferences with resilient parsing
    public class PreferencesStore(string? path)
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
        };

        //-----------------------------
        //loads preferences property by property so invalid values do not wipe other settings
        public WidgetPreferences Load()
        {
            //return defaults when path is missing or file does not exist
            if (path == null || !File.Exists(path))
            {
                return new WidgetPreferences();
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                //file inaccessible falls back to defaults
                return new WidgetPreferences();
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(text);
            }
            catch (Exception)
            {
                //backup unparseable file before returning defaults
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    var bak = dir != null ? Path.Combine(dir, "widget.json.bak") : path + ".bak";
                    File.Copy(path, bak, overwrite: true);
                }
                catch (Exception)
                {
                    //ignore backup failure
                }

                return new WidgetPreferences();
            }

            using (doc)
            {
                var root = doc.RootElement;
                //root must be json object
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new WidgetPreferences();
                }

                var prefs = new WidgetPreferences();

                if (TryGetEnum<CheckInTiming>(root, "CheckInTiming", out var timing))
                    prefs.CheckInTiming = timing;

                if (TryGetBool(root, "LargerText", out var largerText))
                    prefs.LargerText = largerText;

                if (root.TryGetProperty("WorkingDays", out var days) && days.ValueKind == JsonValueKind.Array)
                {
                    var parsedDays = new List<DayOfWeek>();
                    var valid = true;
                    foreach (var day in days.EnumerateArray())
                    {
                        if (day.ValueKind != JsonValueKind.String ||
                            !Enum.TryParse<DayOfWeek>(day.GetString(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                        {
                            valid = false;
                            break;
                        }
                        parsedDays.Add(parsed);
                    }
                    if (valid)
                        prefs.WorkingDays = parsedDays;
                }

                //read root level preferences
                if (TryGetEnum<IdleVisibility>(root, "IdleVisibility", out var vis))
                {
                    prefs.IdleVisibility = vis;
                }

                if (TryGetEnum<IdleShape>(root, "IdleShape", out var shape))
                {
                    prefs.IdleShape = shape;
                }

                if (TryGetBool(root, "SoundOnCheckIn", out var sound))
                {
                    prefs.SoundOnCheckIn = sound;
                }

                if (TryGetBool(root, "StartWithWindows", out var start))
                {
                    prefs.StartWithWindows = start;
                }

                if (TryGetBool(root, "VoiceTipDismissed", out var tip))
                {
                    prefs.VoiceTipDismissed = tip;
                }

                if (ReadPosition(root, "IdlePosition", Options, out var idlePos))
                {
                    prefs.IdlePosition = idlePos;
                }

                if (ReadPosition(root, "MainPosition", Options, out var mainPos))
                {
                    prefs.MainPosition = mainPos;
                }

                //read nested ai preferences setting by setting
                if (root.TryGetProperty("Ai", out var aiProp) && aiProp.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetBool(aiProp, "VoiceEnabled", out var voiceEnabled))
                    {
                        prefs.Ai.VoiceEnabled = voiceEnabled;
                    }

                    if (TryGetBool(aiProp, "SetupCompleted", out var setupDone))
                    {
                        prefs.Ai.SetupCompleted = setupDone;
                    }

                    if (TryGetEnum<CompilerKind>(aiProp, "Compiler", out var comp))
                    {
                        prefs.Ai.Compiler = comp;
                    }

                    if (TryGetString(aiProp, "CompilerUrl", out var compUrl) && compUrl != null)
                    {
                        prefs.Ai.CompilerUrl = compUrl;
                    }

                    if (TryGetString(aiProp, "CompilerModel", out var compModel) && compModel != null)
                    {
                        prefs.Ai.CompilerModel = compModel;
                    }

                    if (TryGetInt(aiProp, "ContextTokens", out var ctx))
                    {
                        prefs.Ai.ContextTokens = ctx;
                    }

                    if (TryGetInt(aiProp, "TimeoutSeconds", out var timeout))
                    {
                        prefs.Ai.TimeoutSeconds = timeout;
                    }

                    if (TryGetNullableInt(aiProp, "GpuLayers", out var gpuLayers))
                    {
                        prefs.Ai.GpuLayers = gpuLayers;
                    }

                    if (TryGetEnum<HardwareChoice>(aiProp, "DayHardware", out var dayHw))
                    {
                        prefs.Ai.DayHardware = dayHw;
                    }

                    if (TryGetEnum<VoiceHardwareChoice>(aiProp, "VoiceHardware", out var voiceHw))
                    {
                        prefs.Ai.VoiceHardware = voiceHw;
                    }

                    if (ReadSpeed(aiProp, "LastSpeedCheck", Options, out var speed))
                    {
                        prefs.Ai.LastSpeedCheck = speed;
                    }

                    if (TryGetEnum<TranscriberKind>(aiProp, "Transcriber", out var transcriber))
                    {
                        prefs.Ai.Transcriber = transcriber;
                    }

                    if (TryGetString(aiProp, "WhisperModel", out var wModel) && wModel != null)
                    {
                        prefs.Ai.WhisperModel = wModel;
                    }

                    if (TryGetString(aiProp, "WhisperModelPath", out var wPath))
                    {
                        prefs.Ai.WhisperModelPath = wPath;
                    }

                    if (TryGetString(aiProp, "TranscriberUrl", out var tUrl) && tUrl != null)
                    {
                        prefs.Ai.TranscriberUrl = tUrl;
                    }

                    if (TryGetString(aiProp, "TranscriberModel", out var tModel) && tModel != null)
                    {
                        prefs.Ai.TranscriberModel = tModel;
                    }

                    if (TryGetString(aiProp, "MicrophoneDeviceId", out var micId))
                    {
                        prefs.Ai.MicrophoneDeviceId = micId;
                    }

                    if (TryGetBool(aiProp, "OllamaManagedByWidget", out var managed))
                    {
                        prefs.Ai.OllamaManagedByWidget = managed;
                    }

                    if (TryGetBool(aiProp, "SlowPcHintDismissed", out var slowDismissed))
                    {
                        prefs.Ai.SlowPcHintDismissed = slowDismissed;
                    }
                }

                return prefs;
            }
        }

        //-----------------------------
        //saves preferences to disk
        public void Save(WidgetPreferences preferences)
        {
            //skip save when in memory only
            if (path == null)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(preferences, Options));
        }

        //-----------------------------
        //parses enum value from json element by name
        private static bool TryGetEnum<TEnum>(JsonElement parent, string propName, out TEnum value) where TEnum : struct, Enum
        {
            value = default;
            //check property presence
            if (parent.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var str = prop.GetString();
                //parse enum matching defined constants
                if (Enum.TryParse<TEnum>(str, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
                {
                    value = parsed;
                    return true;
                }
            }

            return false;
        }

        //-----------------------------
        //parses boolean value from json element
        private static bool TryGetBool(JsonElement parent, string propName, out bool value)
        {
            value = default;
            //check property presence
            if (parent.TryGetProperty(propName, out var prop) && prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = prop.GetBoolean();
                return true;
            }

            return false;
        }

        //-----------------------------
        //parses integer value from json element
        private static bool TryGetInt(JsonElement parent, string propName, out int value)
        {
            value = default;
            //check property presence
            if (parent.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
            {
                return true;
            }

            return false;
        }

        //-----------------------------
        //parses nullable integer value from json element
        private static bool TryGetNullableInt(JsonElement parent, string propName, out int? value)
        {
            value = default;
            //check property presence
            if (parent.TryGetProperty(propName, out var prop))
            {
                //number kind parses integer
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var v))
                {
                    value = v;
                    return true;
                }

                //null kind returns null value
                if (prop.ValueKind == JsonValueKind.Null)
                {
                    value = null;
                    return true;
                }
            }

            return false;
        }

        //-----------------------------
        //parses string value from json element
        private static bool TryGetString(JsonElement parent, string propName, out string? value)
        {
            value = default;
            //check property presence
            if (parent.TryGetProperty(propName, out var prop))
            {
                //string kind parses string text
                if (prop.ValueKind == JsonValueKind.String)
                {
                    value = prop.GetString();
                    return true;
                }

                //null kind returns null value
                if (prop.ValueKind == JsonValueKind.Null)
                {
                    value = null;
                    return true;
                }
            }

            return false;
        }

        //-----------------------------
        //parses widget position record from json element
        private static bool ReadPosition(JsonElement parent, string propName, JsonSerializerOptions options, out WidgetPosition? position)
        {
            position = null;
            //check property presence
            if (parent.TryGetProperty(propName, out var prop))
            {
                //null kind returns null position
                if (prop.ValueKind == JsonValueKind.Null)
                {
                    return true;
                }

                //object kind deserializes position
                if (prop.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        position = JsonSerializer.Deserialize<WidgetPosition>(prop.GetRawText(), options);
                        return true;
                    }
                    catch (Exception)
                    {
                        //invalid position keeps default
                        return false;
                    }
                }
            }

            return false;
        }

        //-----------------------------
        //parses speed result record from json element
        private static bool ReadSpeed(JsonElement parent, string propName, JsonSerializerOptions options, out SpeedResult? speed)
        {
            speed = null;
            //check property presence
            if (parent.TryGetProperty(propName, out var prop))
            {
                //null kind returns null speed
                if (prop.ValueKind == JsonValueKind.Null)
                {
                    return true;
                }

                //object kind deserializes speed
                if (prop.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        speed = JsonSerializer.Deserialize<SpeedResult>(prop.GetRawText(), options);
                        return true;
                    }
                    catch (Exception)
                    {
                        //invalid speed keeps default
                        return false;
                    }
                }
            }

            return false;
        }
    }
}
