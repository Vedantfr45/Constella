using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Constella.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Constella.Audio;

/// <summary>
/// Measures how loud the computer's sound output is, using WASAPI loopback capture (it "listens"
/// to whatever is playing; nothing is recorded or stored). The level is smoothed with a fast
/// attack and slow release so the edge glow breathes rather than flickers.
/// </summary>
internal sealed class LoudnessMeter : IDisposable
{
    /// <summary>Loudness (dBFS) mapped to 0 and 1 before auto-gain.</summary>
    private const double FloorDb = -50, CeilingDb = -6;
    private const double AttackSeconds = 0.08, ReleaseSeconds = 0.45;

    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();
    private readonly DispatcherTimer _retryTimer;
    private MMDeviceEnumerator? _enumerator;
    private DeviceWatcher? _deviceWatcher;
    private WasapiLoopbackCapture? _capture;
    private double _sumSquares;
    private long _samples;
    private double _envelope;
    private double _peak = 0.5;
    private bool _wanted;

    public LoudnessMeter(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _retryTimer.Tick += (_, _) =>
        {
            _retryTimer.Stop();
            if (_wanted)
            {
                StartCapture();
            }
        };
    }

    /// <summary>Starts listening (cheap to call repeatedly).</summary>
    public void Start()
    {
        if (_wanted)
        {
            return;
        }
        _wanted = true;
        WatchDeviceChanges();
        StartCapture();
    }

    /// <summary>Stops listening so no CPU is spent while the overlay is hidden.</summary>
    public void Stop()
    {
        _wanted = false;
        _retryTimer.Stop();
        StopCapture();
        _envelope = 0;
    }

    /// <summary>
    /// Returns the smoothed level (0..1) for this frame. Call once per frame with the frame time.
    /// </summary>
    public double Sample(double dt)
    {
        double sum;
        long count;
        lock (_gate)
        {
            sum = _sumSquares;
            count = _samples;
            _sumSquares = 0;
            _samples = 0;
        }

        // Windows sends no loopback data during silence, so "no samples" means quiet.
        double rms = count > 0 ? Math.Sqrt(sum / count) : 0;
        double db = 20 * Math.Log10(rms + 1e-7);
        double level = Ease.Clamp01((db - FloorDb) / (CeilingDb - FloorDb));

        // Auto-gain: follow the recent peak (decaying slowly) so quiet volume still moves the glow.
        _peak = Math.Max(Math.Max(level, 0.35), _peak - dt * 0.05);
        double normalized = Ease.Clamp01(level / _peak);

        double tau = normalized > _envelope ? AttackSeconds : ReleaseSeconds;
        _envelope = Ease.Approach(_envelope, normalized, dt, tau);
        return _envelope;
    }

    public void Dispose()
    {
        Stop();
        if (_deviceWatcher is not null && _enumerator is not null)
        {
            _enumerator.UnregisterEndpointNotificationCallback(_deviceWatcher);
        }
        _enumerator?.Dispose();
    }

    private void StartCapture()
    {
        StopCapture();
        try
        {
            var capture = new WasapiLoopbackCapture();
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();
            _capture = capture;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            // No output device (or it is busy). Try again shortly; the glow just stays calm meanwhile.
            Log.Warn($"Audio loopback unavailable: {ex.Message}");
            _retryTimer.Start();
        }
    }

    private void StopCapture()
    {
        var capture = _capture;
        _capture = null;
        if (capture is null)
        {
            return;
        }
        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        try
        {
            capture.StopRecording();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Log.Warn($"Audio loopback stop failed: {ex.Message}");
        }
        capture.Dispose();
    }

    /// <summary>Runs on NAudio's capture thread; only sums energy, the UI thread does the rest.</summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture capture || e.BytesRecorded == 0)
        {
            return;
        }

        var format = capture.WaveFormat;
        var bytes = e.Buffer.AsSpan(0, e.BytesRecorded);
        double sum = 0;
        long count;

        // The shared-mode mix format is almost always 32-bit float; handle 16-bit PCM too.
        if (format.BitsPerSample == 32 && format.Encoding is WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible)
        {
            var samples = MemoryMarshal.Cast<byte, float>(bytes);
            foreach (float s in samples)
            {
                sum += s * s;
            }
            count = samples.Length;
        }
        else if (format.BitsPerSample == 16)
        {
            var samples = MemoryMarshal.Cast<byte, short>(bytes);
            foreach (short s in samples)
            {
                double v = s / 32768.0;
                sum += v * v;
            }
            count = samples.Length;
        }
        else
        {
            return;
        }

        lock (_gate)
        {
            _sumSquares += sum;
            _samples += count;
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null)
        {
            return;
        }
        // The device was unplugged or changed format. Restart from the UI thread.
        Log.Warn($"Audio loopback stopped: {e.Exception.Message}");
        _dispatcher.InvokeAsync(() =>
        {
            if (_wanted && ReferenceEquals(sender, _capture))
            {
                StopCapture();
                _retryTimer.Start();
            }
        });
    }

    private void WatchDeviceChanges()
    {
        if (_enumerator is not null)
        {
            return;
        }
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _deviceWatcher = new DeviceWatcher(() => _dispatcher.InvokeAsync(() =>
            {
                if (_wanted)
                {
                    StartCapture(); // Follow the new default speakers/headphones.
                }
            }));
            _enumerator.RegisterEndpointNotificationCallback(_deviceWatcher);
        }
        catch (COMException ex)
        {
            Log.Warn($"Cannot watch audio device changes: {ex.Message}");
        }
    }

    /// <summary>Calls back when Windows switches the default output device.</summary>
    private sealed class DeviceWatcher : IMMNotificationClient
    {
        private readonly Action _defaultOutputChanged;

        public DeviceWatcher(Action defaultOutputChanged) => _defaultOutputChanged = defaultOutputChanged;

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
            {
                _defaultOutputChanged();
            }
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
        }

        public void OnDeviceRemoved(string deviceId)
        {
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
        }
    }
}
