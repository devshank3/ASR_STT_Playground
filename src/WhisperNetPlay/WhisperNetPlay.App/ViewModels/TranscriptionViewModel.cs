using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperNetPlay.Speech;

namespace WhisperNetPlay.App.ViewModels;

/// <summary>
/// Drives the live speech-to-text window: Whisper model loading, mic toggle, GPU/CPU badge,
/// interim hypothesis, and the editable transcript.
/// </summary>
public partial class TranscriptionViewModel : ObservableObject
{
    private readonly ISpeechTranscriptionService _service;
    private readonly SynchronizationContext? _ui;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MicButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ToggleMicCommand))]
    public partial bool IsListening { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleMic))]
    [NotifyCanExecuteChangedFor(nameof(ToggleMicCommand))]
    public partial bool IsModelReady { get; set; }

    [ObservableProperty]
    public partial bool IsModelLoading { get; set; }

    [ObservableProperty]
    public partial string TranscriptText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInterim))]
    public partial string InterimText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Preparing speech model…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGpuBackend))]
    [NotifyPropertyChangedFor(nameof(IsCpuBackend))]
    public partial string BackendText { get; set; } = "…";

    public bool HasInterim => !string.IsNullOrWhiteSpace(InterimText);
    public bool CanToggleMic => IsModelReady;
    public string MicButtonText => IsListening ? "Stop" : "Start Mic";
    public bool IsGpuBackend => BackendText.StartsWith("GPU", StringComparison.OrdinalIgnoreCase);
    public bool IsCpuBackend => BackendText.StartsWith("CPU", StringComparison.OrdinalIgnoreCase);

    /// <summary>Design-time constructor used by the XAML previewer.</summary>
    public TranscriptionViewModel()
        : this(new WhisperTranscriptionService())
    {
    }

    public TranscriptionViewModel(ISpeechTranscriptionService service)
    {
        _service = service;
        _ui = SynchronizationContext.Current;
        _service.InterimTextChanged += OnInterimTextChanged;
        _service.SegmentCommitted += OnSegmentCommitted;
    }

    public async Task InitializeAsync()
    {
        IsModelLoading = true;
        var progress = new Progress<string>(message => StatusText = message);
        try
        {
            await _service.InitializeAsync(progress);
            IsModelReady = _service.IsReady;
            BackendText = _service.Backend ?? "unknown backend";
            StatusText = IsModelReady
                ? "Press Start Mic and speak."
                : "Speech model unavailable.";
        }
        catch (Exception ex)
        {
            IsModelReady = false;
            BackendText = "unavailable";
            StatusText = $"Speech model failed to load: {ex.Message}";
        }
        finally
        {
            IsModelLoading = false;
        }
    }

    public async Task ShutdownAsync()
    {
        _service.InterimTextChanged -= OnInterimTextChanged;
        _service.SegmentCommitted -= OnSegmentCommitted;
        await _service.DisposeAsync();
    }

    [RelayCommand(CanExecute = nameof(CanToggleMic))]
    private async Task ToggleMicAsync()
    {
        if (!IsModelReady)
            return;

        if (IsListening)
        {
            await _service.StopAsync();
            IsListening = false;
            StatusText = "Stopped. You can edit the transcript.";
            return;
        }

        try
        {
            await _service.StartAsync();
            IsListening = true;
            StatusText = "Listening… speak now.";
        }
        catch (Exception ex)
        {
            IsListening = false;
            StatusText = $"Could not start microphone: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearTranscript()
    {
        TranscriptText = string.Empty;
        InterimText = string.Empty;
    }

    private void OnInterimTextChanged(object? sender, string text)
        => PostToUi(() => InterimText = text);

    private void OnSegmentCommitted(object? sender, string text)
        => PostToUi(() =>
        {
            TranscriptText = AppendSegment(TranscriptText, text);
            InterimText = string.Empty;
        });

    private void PostToUi(Action action)
    {
        if (_ui is null)
            action();
        else
            _ui.Post(_ => action(), null);
    }

    private static string AppendSegment(string existing, string segment)
    {
        if (string.IsNullOrEmpty(existing))
            return segment;

        var needsSpace = !char.IsWhiteSpace(existing[^1]);
        return needsSpace ? $"{existing} {segment}" : existing + segment;
    }
}
