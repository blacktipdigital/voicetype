using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceType.Core.Logging;

namespace VoiceType.Core.Audio;

/// <summary>
/// WASAPI shared-mode capture at the device mix format, converted in memory
/// to 24 kHz mono PCM16 and emitted in 100 ms chunks. The transit buffer is
/// bounded (5 s, discard-on-overflow) so a stalled consumer cannot grow
/// memory without limit. No audio touches disk.
/// </summary>
public sealed class WasapiAudioCapture : IAudioCapture
{
    private const int TargetSampleRate = 24_000;
    private const int ChunkSamples = TargetSampleRate / 10; // 100 ms

    private readonly ILog _log;
    private readonly object _sync = new();

    private WasapiCapture? _capture;
    private MMDevice? _device;
    private BufferedWaveProvider? _transit;
    private ISampleProvider? _pipeline;
    private readonly float[] _sampleBuf = new float[ChunkSamples];
    private int _sampleFill;
    private bool _stopping;

    public event Action<byte[]>? ChunkReady;
    public event Action<float>? LevelChanged;
    public event Action<string>? CaptureError;

    public WasapiAudioCapture(ILog log) => _log = log;

    public IReadOnlyList<AudioDevice> EnumerateDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try
        {
            using var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            defaultId = def.ID;
        }
        catch
        {
            // no default capture device present
        }

        var devices = new List<AudioDevice>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device)
            {
                devices.Add(new AudioDevice(device.ID, device.FriendlyName, device.ID == defaultId));
            }
        }

        return devices;
    }

    public void Start(string? deviceId)
    {
        lock (_sync)
        {
            if (_capture is not null)
                throw new InvalidOperationException("Capture already running.");

            using var enumerator = new MMDeviceEnumerator();
            _device = deviceId is null
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                : enumerator.GetDevice(deviceId);

            _capture = new WasapiCapture(_device); // shared mode, device mix format
            _transit = new BufferedWaveProvider(_capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(5),
                DiscardOnBufferOverflow = true,
                ReadFully = false,
            };

            ISampleProvider samples = _transit.ToSampleProvider();
            if (samples.WaveFormat.Channels == 2)
                samples = new StereoToMonoSampleProvider(samples);
            else if (samples.WaveFormat.Channels > 2)
                samples = new MultiChannelToMonoSampleProvider(samples);
            if (samples.WaveFormat.SampleRate != TargetSampleRate)
                samples = new WdlResamplingSampleProvider(samples, TargetSampleRate);
            _pipeline = samples;

            _sampleFill = 0;
            _stopping = false;
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _log.Info("Audio capture started.");
        }
    }

    public void Stop()
    {
        WasapiCapture? capture;
        lock (_sync)
        {
            capture = _capture;
            _stopping = true;
        }

        if (capture is null) return;

        try
        {
            capture.StopRecording();
        }
        catch (Exception ex)
        {
            _log.Warn($"StopRecording failed: {ex.GetType().Name}.");
        }

        Cleanup();
        _log.Info("Audio capture stopped; buffers cleared.");
    }

    public void Dispose() => Stop();

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        BufferedWaveProvider? transit;
        ISampleProvider? pipeline;
        lock (_sync)
        {
            transit = _transit;
            pipeline = _pipeline;
            if (transit is null || pipeline is null || _stopping) return;
        }

        transit.AddSamples(e.Buffer, 0, e.BytesRecorded);

        // Drain everything the resampler can produce into 100 ms chunks.
        while (true)
        {
            int read = pipeline.Read(_sampleBuf, _sampleFill, ChunkSamples - _sampleFill);
            if (read <= 0) break;
            _sampleFill += read;
            if (_sampleFill < ChunkSamples) continue;

            EmitChunk(_sampleBuf, ChunkSamples);
            _sampleFill = 0;
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null && !_stopping)
        {
            _log.Error("Capture faulted.", e.Exception);
            Cleanup();
            CaptureError?.Invoke("Microphone capture failed.");
        }
    }

    private void EmitChunk(float[] samples, int count)
    {
        var pcm = new byte[count * sizeof(short)];
        float peak = 0f;
        for (int i = 0; i < count; i++)
        {
            float s = Math.Clamp(samples[i], -1f, 1f);
            float abs = Math.Abs(s);
            if (abs > peak) peak = abs;
            short value = (short)(s * short.MaxValue);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        LevelChanged?.Invoke(peak);
        ChunkReady?.Invoke(pcm);
    }

    private void Cleanup()
    {
        lock (_sync)
        {
            if (_capture is not null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.Dispose();
                _capture = null;
            }

            _device?.Dispose();
            _device = null;
            _transit?.ClearBuffer();
            _transit = null;
            _pipeline = null;
            Array.Clear(_sampleBuf);
            _sampleFill = 0;
        }
    }

    /// <summary>Averages >2-channel input down to mono.</summary>
    private sealed class MultiChannelToMonoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _scratch = Array.Empty<float>();

        public MultiChannelToMonoSampleProvider(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int needed = count * _channels;
            if (_scratch.Length < needed) _scratch = new float[needed];
            int read = _source.Read(_scratch, 0, needed);
            int frames = read / _channels;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                for (int c = 0; c < _channels; c++) sum += _scratch[f * _channels + c];
                buffer[offset + f] = sum / _channels;
            }

            return frames;
        }
    }
}
