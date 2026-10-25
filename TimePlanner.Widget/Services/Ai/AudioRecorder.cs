using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TimePlanner.Core.Ai;

namespace TimePlanner.Widget.Services.Ai
{
    //-----------------------------
    //captures microphone audio via wasapi shared mode and resamples to 16khz mono
    public sealed class AudioRecorder : IDisposable
    {
        private readonly AiSettings? _settings;
        private readonly MicrophoneCatalog _catalog;

        private WasapiCapture? _capture;
        private BufferedWaveProvider? _bufferedProvider;
        private WdlResamplingSampleProvider? _resampler;
        private WaveFileWriter? _writer;
        private DispatcherTimer? _timer;
        private Stopwatch? _stopwatch;
        private string? _tempPath;
        private bool _cancelled;
        private bool _isDisposed;
        private bool _saveToFile;
        private readonly List<float> _recordedSamples = new();

        public event Action<double>? LevelChanged;
        public event Action<TimeSpan>? Elapsed;
        public event Action<string?, TimeSpan, string?>? Stopped;

        public bool HeardSpeech { get; private set; }
        public bool IsDigitalSilence { get; private set; }
        public bool IsRecording => _capture != null;

        //-----------------------------
        //initializes recorder with optional custom settings and catalog
        public AudioRecorder(AiSettings? settings = null, MicrophoneCatalog? catalog = null)
        {
            _settings = settings;
            _catalog = catalog ?? new MicrophoneCatalog();
        }

        //-----------------------------
        //begins audio recording with option to save wav file to disk
        public void Start(bool saveToFile = true)
        {
            _saveToFile = saveToFile;
            _recordedSamples.Clear();
            HeardSpeech = false;
            IsDigitalSilence = false;
            _cancelled = false;

            var device = _catalog.Resolve(_settings?.MicrophoneDeviceId);
            //verify audio capture device availability
            if (device == null)
            {
                Dispatch(() => Stopped?.Invoke(null, TimeSpan.Zero, "No microphone found. Plug one in, or type your update instead."));
                return;
            }

            //prepare storage file when recording to disk
            if (_saveToFile)
            {
                var dir = Path.Combine(Path.GetTempPath(), "TimePlanner");
                Directory.CreateDirectory(dir);
                _tempPath = Path.Combine(dir, $"rec-{Guid.NewGuid():N}.wav");
            }
            else
            {
                _tempPath = null;
            }

            try
            {
                _capture = new WasapiCapture(device)
                {
                    ShareMode = AudioClientShareMode.Shared
                };

                var nativeFormat = _capture.WaveFormat;
                _bufferedProvider = new BufferedWaveProvider(nativeFormat)
                {
                    DiscardOnBufferOverflow = true,
                    ReadFully = false,
                    BufferLength = nativeFormat.AverageBytesPerSecond * 5
                };

                ISampleProvider sampleProvider = _bufferedProvider.ToSampleProvider();
                //downmix multi channel input to mono
                if (sampleProvider.WaveFormat.Channels > 1)
                {
                    sampleProvider = sampleProvider.ToMono();
                }

                _resampler = new WdlResamplingSampleProvider(sampleProvider, 16000);

                //initialize wav writer when saving to disk
                if (_saveToFile && _tempPath != null)
                {
                    _writer = new WaveFileWriter(_tempPath, new WaveFormat(16000, 16, 1));
                }

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _stopwatch = Stopwatch.StartNew();
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _timer.Tick += OnTimerTick;
                _timer.Start();

                _capture.StartRecording();
            }
            catch (Exception ex)
            {
                //clean up resources on capture start failure
                CleanUpWriter();
                CleanUpCapture();
                DeleteTempFile();

                Trace.TraceWarning($"Microphone capture start failed: {ex.Message}");
                Dispatch(() => Stopped?.Invoke(null, TimeSpan.Zero,
                    "Windows is blocking the microphone. Turn on Settings › Privacy & security › Microphone › Let desktop apps access your microphone."));
            }
        }

        //-----------------------------
        //processes recorded audio chunk and writes converted pcm
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            //process recorded audio samples
            if (_bufferedProvider != null && _resampler != null && e.BytesRecorded > 0)
            {
                _bufferedProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);

                var floatBuffer = new float[4096];
                int samplesRead;
                double latestPeak = 0;

                //read all resampled 16khz mono float samples
                while ((samplesRead = _resampler.Read(floatBuffer, 0, floatBuffer.Length)) > 0)
                {
                    //append samples for speech analysis
                    for (var i = 0; i < samplesRead; i++)
                    {
                        var s = floatBuffer[i];
                        _recordedSamples.Add(s);
                        var abs = Math.Abs(s);
                        //track peak amplitude
                        if (abs > latestPeak)
                        {
                            latestPeak = abs;
                        }
                    }

                    //write sixteen bit pcm to file
                    if (_writer != null)
                    {
                        var pcmBytes = new byte[samplesRead * 2];
                        //convert float to signed 16 bit integer bytes
                        for (var i = 0; i < samplesRead; i++)
                        {
                            var clamped = Math.Clamp(floatBuffer[i], -1.0f, 1.0f);
                            var val = (short)(clamped < 0 ? clamped * 32768f : clamped * 32767f);
                            pcmBytes[i * 2] = (byte)(val & 0xFF);
                            pcmBytes[i * 2 + 1] = (byte)((val >> 8) & 0xFF);
                        }

                        _writer.Write(pcmBytes, 0, pcmBytes.Length);
                    }
                }

                Dispatch(() => LevelChanged?.Invoke(Math.Min(1.0, latestPeak)));
            }
        }

        //-----------------------------
        //handles interval timer tick to report elapsed time
        private void OnTimerTick(object? sender, EventArgs e)
        {
            //dispatch elapsed duration
            if (_stopwatch != null)
            {
                var elapsed = _stopwatch.Elapsed;
                Dispatch(() => Elapsed?.Invoke(elapsed));

                //automatically stop when maximum recording limit reached
                if (elapsed.TotalSeconds >= AiLimits.RecordingSeconds)
                {
                    Stop();
                }
            }
        }

        //-----------------------------
        //stops current recording and finalizes wav file
        public void Stop()
        {
            _timer?.Stop();
            _stopwatch?.Stop();

            try
            {
                _capture?.StopRecording();
            }
            catch (Exception)
            {
                //ignore stop error
            }
        }

        //-----------------------------
        //cancels recording and deletes audio file
        public void Cancel()
        {
            _cancelled = true;
            _timer?.Stop();
            _stopwatch?.Stop();

            try
            {
                _capture?.StopRecording();
            }
            catch (Exception)
            {
                //ignore stop error
            }

            CleanUpWriter();
            CleanUpCapture();
            DeleteTempFile();
        }

        //-----------------------------
        //handles recording completion event from wasapi capture
        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            var length = _stopwatch?.Elapsed ?? TimeSpan.Zero;
            CleanUpWriter();
            CleanUpCapture();

            //calculate speech presence and digital silence
            if (_recordedSamples.Count > 0)
            {
                var span = CollectionsMarshal.AsSpan(_recordedSamples);
                IsDigitalSilence = AudioLevels.IsDigitalSilence(span);
                HeardSpeech = AudioLevels.HasSpeech(span, 16000);
            }
            else
            {
                IsDigitalSilence = true;
                HeardSpeech = false;
            }

            //ignore completion if cancelled
            if (_cancelled)
            {
                DeleteTempFile();
                return;
            }

            var path = _tempPath;
            var error = e.Exception?.Message;

            //delete temp file if stopped with error
            if (error != null)
            {
                DeleteTempFile();
                path = null;
            }

            Dispatch(() => Stopped?.Invoke(path, length, error));
        }

        //-----------------------------
        //safely disposes wave file writer
        private void CleanUpWriter()
        {
            //dispose writer instance
            if (_writer != null)
            {
                try
                {
                    _writer.Dispose();
                }
                catch (Exception)
                {
                    //ignore disposal failure
                }

                _writer = null;
            }
        }

        //-----------------------------
        //safely disposes wasapi capture device
        private void CleanUpCapture()
        {
            //dispose capture instance
            if (_capture != null)
            {
                try
                {
                    _capture.DataAvailable -= OnDataAvailable;
                    _capture.RecordingStopped -= OnRecordingStopped;
                    _capture.Dispose();
                }
                catch (Exception)
                {
                    //ignore disposal failure
                }

                _capture = null;
            }
        }

        //-----------------------------
        //deletes temporary wav file from disk
        private void DeleteTempFile()
        {
            //delete file if path is valid
            if (!string.IsNullOrWhiteSpace(_tempPath) && File.Exists(_tempPath))
            {
                try
                {
                    File.Delete(_tempPath);
                }
                catch (Exception)
                {
                    //ignore deletion error
                }
            }
        }

        //-----------------------------
        //dispatches callback to wpf ui thread
        private static void Dispatch(Action action)
        {
            var app = Application.Current;
            //dispatch to wpf thread
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.InvokeAsync(action);
            }
            else
            {
                action();
            }
        }

        //-----------------------------
        //disposes audio capture resources
        public void Dispose()
        {
            //skip multiple disposes
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            Cancel();
        }
    }
}
//------------------------------EOF-----------------------------\\
