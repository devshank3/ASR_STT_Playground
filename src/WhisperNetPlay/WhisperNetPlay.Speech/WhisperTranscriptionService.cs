using System.Text;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace WhisperNetPlay.Speech;

/// <summary>
/// Orchestrates live English speech-to-text: microphone capture, energy-based endpointing, and a
/// reused <see cref="WhisperProcessor"/>.
/// </summary>
public sealed class WhisperTranscriptionService : ISpeechTranscriptionService
{
    private readonly IMicrophoneCapture _microphone;
    private readonly WhisperModelProvider _modelProvider;

    private readonly object _bufferLock = new();
    private readonly List<float> _pending = new();

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private CancellationTokenSource? _sessionCts;
    private Task? _worker;
    private NativeWhisperLog? _nativeLog;

    public WhisperTranscriptionService(
        IMicrophoneCapture? microphone = null,
        WhisperModelProvider? modelProvider = null)
    {
        _microphone = microphone ?? new NAudioMicrophoneCapture();
        _modelProvider = modelProvider ?? new WhisperModelProvider();
        _microphone.SamplesAvailable += OnSamplesAvailable;
    }

    /// <inheritdoc />
    public bool IsReady => _processor is not null;

    /// <inheritdoc />
    public bool IsListening { get; private set; }

    /// <inheritdoc />
    public string? Backend { get; private set; }

    /// <inheritdoc />
    public event EventHandler<string>? InterimTextChanged;

    /// <inheritdoc />
    public event EventHandler<string>? SegmentCommitted;

    /// <inheritdoc />
    public async Task InitializeAsync(IProgress<string>? progress, CancellationToken cancellationToken = default)
    {
        if (_processor is not null)
            return;

        var modelPath = await _modelProvider.GetModelPathAsync(progress, cancellationToken).ConfigureAwait(false);
        progress?.Report("Loading speech model…");

        _nativeLog ??= NativeWhisperLog.Create();

        RuntimeOptions.RuntimeLibraryOrder =
        [
            RuntimeLibrary.Cuda12,
            RuntimeLibrary.Cuda,
            RuntimeLibrary.Cpu
        ];

        _factory = WhisperFactory.FromPath(modelPath);
        // This sample is English-only on purpose.
        _processor = _factory.CreateBuilder()
            .WithLanguage("en")
            .WithNoContext()
            .WithThreads(Math.Max(2, Environment.ProcessorCount - 2))
            .Build();

        Backend = DescribeBackend(RuntimeOptions.LoadedLibrary);
        progress?.Report($"Speech model ready · {Backend}.");
    }

    /// <inheritdoc />
    public Task StartAsync()
    {
        if (IsListening)
            return Task.CompletedTask;
        if (_processor is null)
            throw new InvalidOperationException("InitializeAsync must complete before StartAsync.");

        lock (_bufferLock)
            _pending.Clear();

        _sessionCts = new CancellationTokenSource();
        _microphone.Start();
        IsListening = true;
        _worker = Task.Run(() => RunAsync(_sessionCts.Token));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (!IsListening)
            return;

        IsListening = false;

        try
        {
            _microphone.Stop();
        }
        catch
        {
            // Ignore stop races.
        }

        _sessionCts?.Cancel();
        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch
            {
                // Worker cancellation is expected on stop.
            }

            _worker = null;
        }

        _sessionCts?.Dispose();
        _sessionCts = null;

        InterimTextChanged?.Invoke(this, string.Empty);
    }

    private void OnSamplesAvailable(float[] samples)
    {
        lock (_bufferLock)
            _pending.AddRange(samples);
    }

    private float[]? TakePending()
    {
        lock (_bufferLock)
        {
            if (_pending.Count == 0)
                return null;

            var copy = _pending.ToArray();
            _pending.Clear();
            return copy;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var endpoint = new EnergyEndpointDetector();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var chunk = TakePending();
                if (chunk is null)
                {
                    await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                switch (endpoint.AddChunk(chunk))
                {
                    case EnergyEndpointDetector.Decision.Commit:
                    {
                        var committed = await TranscribeAsync(endpoint.CopyUtterance(), cancellationToken)
                            .ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(committed))
                            SegmentCommitted?.Invoke(this, committed.Trim());

                        endpoint.ResetAfterCommit();
                        InterimTextChanged?.Invoke(this, string.Empty);
                        break;
                    }
                    case EnergyEndpointDetector.Decision.RequestInterim:
                    {
                        var interim = await TranscribeAsync(endpoint.CopyUtterance(), cancellationToken)
                            .ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(interim))
                            InterimTextChanged?.Invoke(this, interim.Trim());
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop.
        }
    }

    private async Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken)
    {
        var processor = _processor;
        if (processor is null)
            return string.Empty;

        var builder = new StringBuilder();
        try
        {
            await foreach (var segment in processor.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
                builder.Append(segment.Text);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }

        return builder.ToString();
    }

    private static string DescribeBackend(RuntimeLibrary? library) => library switch
    {
        RuntimeLibrary.Cuda => "GPU (CUDA 13)",
        RuntimeLibrary.Cuda12 => "GPU (CUDA 12)",
        RuntimeLibrary.Vulkan => "GPU (Vulkan)",
        RuntimeLibrary.CoreML => "GPU (CoreML)",
        RuntimeLibrary.OpenVino => "OpenVINO",
        RuntimeLibrary.Cpu => "CPU",
        RuntimeLibrary.CpuNoAvx => "CPU (no AVX)",
        _ => "unknown backend"
    };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _microphone.SamplesAvailable -= OnSamplesAvailable;
        _microphone.Dispose();
        _processor?.Dispose();
        _factory?.Dispose();
        _nativeLog?.Dispose();
    }
}
