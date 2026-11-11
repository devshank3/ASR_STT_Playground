namespace WhisperNetPlay.Speech;

/// <summary>
/// Live speech-to-text service: captures the microphone and produces English transcription with a
/// Whisper model. While the user speaks it emits volatile <see cref="InterimTextChanged"/>
/// hypotheses for the active utterance, and raises <see cref="SegmentCommitted"/> with stable text
/// once an utterance ends (detected via a trailing pause).
/// </summary>
public interface ISpeechTranscriptionService : IAsyncDisposable
{
    /// <summary>True once the model is loaded and the service can start listening.</summary>
    bool IsReady { get; }

    /// <summary>True while the microphone is on and transcription is running.</summary>
    bool IsListening { get; }

    /// <summary>
    /// Friendly name of the inference backend that loaded (e.g. "GPU (CUDA 12)" or "CPU").
    /// Null until <see cref="InitializeAsync"/> has completed.
    /// </summary>
    string? Backend { get; }

    /// <summary>Raised with the current in-progress hypothesis for the active utterance.</summary>
    event EventHandler<string>? InterimTextChanged;

    /// <summary>Raised with a finalized segment of text when an utterance ends.</summary>
    event EventHandler<string>? SegmentCommitted;

    /// <summary>
    /// Loads — and, on first use, downloads — the Whisper model and prepares the processor.
    /// Progress messages (e.g. download / load status) are reported through <paramref name="progress"/>.
    /// </summary>
    Task InitializeAsync(IProgress<string>? progress, CancellationToken cancellationToken = default);

    /// <summary>Starts microphone capture and live transcription.</summary>
    Task StartAsync();

    /// <summary>Stops microphone capture and transcription.</summary>
    Task StopAsync();
}
