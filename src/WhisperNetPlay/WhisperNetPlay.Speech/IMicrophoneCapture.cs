namespace WhisperNetPlay.Speech;

/// <summary>
/// Captures microphone audio as 16 kHz mono <see cref="float"/> samples normalized to the
/// range [-1, 1] — the format the Whisper models expect. Implementations are platform-specific.
/// </summary>
public interface IMicrophoneCapture : IDisposable
{
    /// <summary>Sample rate of the delivered audio, in Hz (16000 for Whisper).</summary>
    int SampleRate { get; }

    /// <summary>True while the microphone is actively capturing.</summary>
    bool IsCapturing { get; }

    /// <summary>Raised whenever a new block of samples becomes available.</summary>
    event Action<float[]>? SamplesAvailable;

    /// <summary>Starts capturing. Throws if no input device is available.</summary>
    void Start();

    /// <summary>Stops capturing.</summary>
    void Stop();
}
