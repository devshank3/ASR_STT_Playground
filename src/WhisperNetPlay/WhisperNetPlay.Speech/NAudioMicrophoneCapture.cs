using NAudio.Wave;

namespace WhisperNetPlay.Speech;

/// <summary>
/// <see cref="IMicrophoneCapture"/> backed by NAudio's <see cref="WaveInEvent"/> (WinMM). It
/// requests 16 kHz / 16-bit / mono directly from the driver and converts each captured buffer to
/// normalized float samples so the audio is ready for Whisper without any resampling.
/// </summary>
public sealed class NAudioMicrophoneCapture : IMicrophoneCapture
{
    private const int TargetSampleRate = EnergyEndpointDetector.SampleRate;

    private WaveInEvent? _waveIn;

    /// <inheritdoc />
    public int SampleRate => TargetSampleRate;

    /// <inheritdoc />
    public bool IsCapturing { get; private set; }

    /// <inheritdoc />
    public event Action<float[]>? SamplesAvailable;

    /// <inheritdoc />
    public void Start()
    {
        if (IsCapturing)
            return;

        if (WaveInEvent.DeviceCount == 0)
            throw new InvalidOperationException("No microphone or audio input device was found.");

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(TargetSampleRate, 16, 1),
            BufferMilliseconds = 50,
            NumberOfBuffers = 3,
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
        IsCapturing = true;
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (!IsCapturing)
            return;

        IsCapturing = false;
        try
        {
            _waveIn?.StopRecording();
        }
        catch
        {
            // Ignore stop races; the device is being torn down anyway.
        }

        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.Dispose();
            _waveIn = null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var handler = SamplesAvailable;
        if (handler is null || e.BytesRecorded <= 0)
            return;

        var sampleCount = e.BytesRecorded / 2;
        var samples = new float[sampleCount];
        var buffer = e.Buffer;
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(buffer[i * 2] | (buffer[(i * 2) + 1] << 8));
            samples[i] = sample / 32768f;
        }

        handler(samples);
    }

    /// <inheritdoc />
    public void Dispose() => Stop();
}
