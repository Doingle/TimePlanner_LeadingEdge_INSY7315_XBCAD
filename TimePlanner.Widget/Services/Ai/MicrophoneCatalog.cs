using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace TimePlanner.Widget.Services.Ai
{
    //-----------------------------
    //option item for microphone dropdown selection
    public sealed record MicrophoneOption(string? Id, string Name);

    //-----------------------------
    //enumerates and resolves audio capture endpoints via wasapi
    public sealed class MicrophoneCatalog
    {
        //-----------------------------
        //lists active microphones with windows default first
        public IReadOnlyList<MicrophoneOption> List()
        {
            var options = new List<MicrophoneOption>
            {
                new(null, "Windows default")
            };

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

                //add each active capture endpoint
                foreach (var device in endpoints)
                {
                    try
                    {
                        options.Add(new MicrophoneOption(device.ID, device.FriendlyName));
                    }
                    catch
                    {
                        //skip devices that fail property read
                    }
                }
            }
            catch (Exception ex)
            {
                //return windows default fallback on enumeration error
                Trace.TraceWarning($"Failed to enumerate audio capture devices: {ex.Message}");
            }

            return options;
        }

        //-----------------------------
        //resolves device by identifier falling back to default
        public MMDevice? Resolve(string? id) => Resolve(id, out _);

        //-----------------------------
        //resolves device by identifier and reports whether fallback occurred
        public MMDevice? Resolve(string? id, out bool fellBack)
        {
            fellBack = false;

            try
            {
                using var enumerator = new MMDeviceEnumerator();

                //resolve specific device when identifier provided
                if (!string.IsNullOrWhiteSpace(id))
                {
                    try
                    {
                        var match = enumerator.GetDevice(id);
                        //verify device is still active
                        if (match.State == DeviceState.Active)
                        {
                            return match;
                        }

                        fellBack = true;
                    }
                    catch
                    {
                        //device absent falls back to default
                        fellBack = true;
                    }
                }

                //obtain system default capture endpoint
                return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            }
            catch (Exception ex)
            {
                //no audio capture device available
                Trace.TraceWarning($"Failed to resolve audio capture endpoint: {ex.Message}");
                return null;
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
