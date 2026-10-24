using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NAudio.Wave;
using TimePlanner.Core.Ai;

namespace TimePlanner.Widget.Services.Ai
{
    //-----------------------------
    //manages 16khz mono microphone audio capture
    public sealed class AudioRecorder : IDisposable
    {
        private WaveInEvent? _waveIn;
        private WaveFileWriter? _writer;
        private DispatcherTimer? _timer;
        private Stopwatch? _stopwatch;
        private string? _tempPath;
        private bool _cancelled;
        private bool _isDisposed;

        public event Action<double>? LevelChanged;
        public event Action<TimeSpan>? Elapsed;
        public event Action<string?, TimeSpan, string?>? Stopped;

        public bool HeardSpeech { get; private set; }
        public bool IsRecording => _waveIn != null;

        //-----------------------------
        //begins recording from default audio input device
        public void Start()
        {
            //verify audio input device availability
            if (WaveInEvent.DeviceCount == 0)
            {
                Dispatch(() => Stopped?.Invoke(null, TimeSpan.Zero, "No microphone found. Plug one in, or type your update instead."));
                return;
            }

            var dir = Path.Combine(Path.GetTempPath(), "TimePlanner");
            Directory.CreateDirectory(dir);

            _tempPath = Path.Combine(dir, $"rec-{Guid.NewGuid():N}.wav");
            HeardSpeech = false;
            _cancelled = false;

            try
            {
                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 16, 1),
                    BufferMilliseconds = 50
                };

                _writer = new WaveFileWriter(_tempPath, _waveIn.WaveFormat);
                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;

                _stopwatch = Stopwatch.StartNew();
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _timer.Tick += OnTimerTick;
                _timer.Start();

                _waveIn.StartRecording();
            }
            //map mic access permission exception
            catch (Exception)
            {
                CleanUpWriter();
                CleanUpWaveIn();
                DeleteTempFile();

                Dispatch(() => Stopped?.Invoke(null, TimeSpan.Zero,
                    "Windows is blocking the microphone. Turn on Settings › Privacy & security › Microphone › Let desktop apps access your microphone."));
            }
        }

        //-----------------------------
        //handles incoming audio buffer and peak meter calculation
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            //write buffer chunk to disk
            if (_writer != null && e.BytesRecorded > 0)
            {
                _writer.Write(e.Buffer, 0, e.BytesRecorded);

                double peak = 0;
                //calculate peak sample amplitude
                for (var i = 0; i < e.BytesRecorded; i += 2)
                {
                    var sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                    var sampleAbs = Math.Abs(sample) / 32768.0;
                    if (sampleAbs > peak)
                    {
                        peak = sampleAbs;
                    }
                }

                //flag speech when peak crosses threshold
                if (peak > 0.04)
                {
                    HeardSpeech = true;
                }

                Dispatch(() => LevelChanged?.Invoke(peak));
            }
        }

        //-----------------------------
        //dispatches elapsed time and triggers auto stop at limit
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
                _waveIn?.StopRecording();
            }
            //ignore stop errors
            catch (Exception)
            {
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
                _waveIn?.StopRecording();
            }
            catch (Exception)
            {
            }

            CleanUpWriter();
            CleanUpWaveIn();
            DeleteTempFile();
        }

        //-----------------------------
        //handles recording completion event from wave in
        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            var length = _stopwatch?.Elapsed ?? TimeSpan.Zero;
            CleanUpWriter();
            CleanUpWaveIn();

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
            if (_writer != null)
            {
                try
                {
                    _writer.Dispose();
                }
                catch (Exception)
                {
                }

                _writer = null;
            }
        }

        //-----------------------------
        //safely disposes wave in capture device
        private void CleanUpWaveIn()
        {
            if (_waveIn != null)
            {
                try
                {
                    _waveIn.DataAvailable -= OnDataAvailable;
                    _waveIn.RecordingStopped -= OnRecordingStopped;
                    _waveIn.Dispose();
                }
                catch (Exception)
                {
                }

                _waveIn = null;
            }
        }

        //-----------------------------
        //deletes temporary wav file from disk
        private void DeleteTempFile()
        {
            if (!string.IsNullOrWhiteSpace(_tempPath) && File.Exists(_tempPath))
            {
                try
                {
                    File.Delete(_tempPath);
                }
                catch (Exception)
                {
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
