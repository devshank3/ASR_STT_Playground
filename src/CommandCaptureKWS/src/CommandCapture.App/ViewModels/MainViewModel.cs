using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommandCapture.App.Preview;
using CommandCapture.App.Services;
using CommandCapture.Audio;
using CommandCapture.Camera;
using CommandCapture.Voice;

namespace CommandCapture.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IUiMarshal _ui;
    private readonly IPcmSource? _pcm;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PhraseLog _phrases = new();
    private readonly bool _design;
    private readonly FramePreview? _frames;
    private readonly CatalogStore? _store;
    private ICameraSession? _camera;
    private VoicePipeline? _pipeline;
    private Task? _listenTask;
    private CancellationTokenSource? _listen;
    private float _latestLevel;
    private int _levelScheduled;
    private int _closed;

    public MainViewModel()
    {
        _design = true;
        _ui = new DirectUiMarshal();
        Status = "Design preview";
        StillFolderText = "Pictures\\CommandCapture";
        LastStillText = "No still saved yet.";
        OverrunText = "Capture overruns: 0";
        Commands.Add(new CatalogPhraseRow("capture image"));
        Commands.Add(new CatalogPhraseRow("take photo"));
        Commands.Add(new CatalogPhraseRow("snap picture"));
        _phrases.Insert(new PhraseTrace(
            DateTimeOffset.Now,
            "capture image",
            "capture image",
            CommandCatalog.CaptureStill,
            "Saved in 40 ms. still.png"));
    }

    public MainViewModel(ICameraSession camera, IPcmSource pcm, IUiMarshal ui, string catalogPath, string stillDirectory)
    {
        _camera = camera;
        _pcm = pcm;
        _ui = ui;
        _frames = new FramePreview(ui);
        _frames.Painted += OnPainted;
        _store = new CatalogStore(catalogPath);
        _store.Reloaded += catalog => _ = ApplyCatalogAsync(catalog, announce: true);
        _store.Failed += message => _ui.Post(() => Status = message);
        StillFolderText = stillDirectory;
        LastStillText = "No still saved yet.";
        OverrunText = "Capture overruns: 0";
        Status = "Starting the preview.";
        try
        {
            _store.Load();
            ShowPhrases(_store.Current!);
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }

        _store.Watch(_lifetime.Token);
        camera.FrameAvailable += OnFrame;
        pcm.LevelChanged += OnLevel;
        _ = StartPreviewAsync();
        _ = RefreshModelAsync();
    }

    public ObservableCollection<CatalogPhraseRow> Commands { get; } = [];

    public ObservableCollection<PhraseLogRow> PhraseLog => _phrases.Entries;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StillFolderText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastStillText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OverrunText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPhrase { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double MicrophoneLevel { get; set; }

    [ObservableProperty]
    public partial bool ModelReady { get; set; }

    [ObservableProperty]
    public partial bool IsListening { get; set; }

    [ObservableProperty]
    public partial Bitmap? Preview { get; set; }

    public bool ShowDownload => !ModelReady;

    public bool ShowListen => ModelReady && !IsListening;

    public bool ShowStopListen => IsListening;

    [RelayCommand]
    private async Task CaptureAsync()
    {
        if (_design || _camera is null)
        {
            return;
        }

        try
        {
            var path = await _camera.CaptureStillAsync(CancellationToken.None);
            LastStillText = Path.GetFileName(path);
            Status = $"Saved {path}";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
    }

    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        if (_design)
        {
            return;
        }

        try
        {
            var progress = new Progress<string>(message => Status = message);
            await KeywordModelInstaller.EnsureAsync(progress, _lifetime.Token);
            ModelReady = true;
            Status = "Keyword model is ready. Press Listen.";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
    }

    [RelayCommand]
    private async Task ListenAsync()
    {
        if (_design || _pcm is null || _camera is null || _store is null || IsListening)
        {
            return;
        }

        VoicePipeline? pipeline = null;
        void OnStatus(string message) => _ui.Post(() => Status = message);
        void OnTrace(PhraseTrace trace) => _ui.Post(() => _phrases.Insert(trace));

        try
        {
            var model = await KeywordModelInstaller.EnsureAsync(
                new Progress<string>(message => _ui.Post(() => Status = message)),
                _lifetime.Token).ConfigureAwait(false);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            _ui.Post(() => ModelReady = true);
            var catalog = _store.Require();
            var handler = new StillCaptureHandler(_camera, _ui, (fileName, status, trace) =>
            {
                LastStillText = fileName;
                Status = status;
                _phrases.Insert(trace);
            });
            var dispatcher = new CommandDispatcher(catalog, [handler], new SystemClock());
            pipeline = new VoicePipeline(_pcm, catalog, dispatcher, model);
            pipeline.StatusMessage += OnStatus;
            pipeline.Traced += OnTrace;
            _pipeline = pipeline;
            _listen = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _listenTask = pipeline.RunAsync(_listen.Token);
            _ui.Post(() => IsListening = true);
            await _listenTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            var message = exception.Message;
            _ui.Post(() => Status = message);
        }
        finally
        {
            if (pipeline is not null)
            {
                pipeline.StatusMessage -= OnStatus;
                pipeline.Traced -= OnTrace;
            }

            _pipeline = null;
            _listenTask = null;
            _ui.Post(() => IsListening = false);
        }
    }

    [RelayCommand]
    private void StopListening() => _listen?.Cancel();

    [RelayCommand]
    private async Task AddPhraseAsync()
    {
        if (_design || _store is null)
        {
            return;
        }

        var phrase = NewPhrase.Trim();
        if (phrase.Length == 0)
        {
            Status = "Type a phrase to add.";
            return;
        }

        try
        {
            var next = _store.AddCapturePhrase(phrase);
            string? reloadError = null;
            var pipeline = _pipeline;
            if (pipeline is not null)
            {
                try
                {
                    await Task.Run(() => pipeline.ReloadCatalog(next));
                }
                catch (Exception exception)
                {
                    reloadError = exception.Message;
                }
            }

            ShowPhrases(next);
            NewPhrase = string.Empty;
            Status = reloadError is null
                ? $"Added \"{phrase}\"."
                : $"Added \"{phrase}\", but listening did not update: {reloadError}";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
        {
            return;
        }

        _lifetime.Cancel();
        _listen?.Cancel();
        _store?.Dispose();
        _frames?.Stop();
        if (_pcm is not null)
        {
            _pcm.LevelChanged -= OnLevel;
        }

        if (_camera is not null)
        {
            _camera.FrameAvailable -= OnFrame;
        }

        var listen = _listenTask;
        var pcm = _pcm;
        var camera = _camera;
        try
        {
            Task.Run(async () =>
            {
                if (listen is not null)
                {
                    try
                    {
                        await listen.ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                    }
                }

                if (pcm is not null)
                {
                    try
                    {
                        await pcm.StopAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                    }
                }

                if (camera is not null)
                {
                    try
                    {
                        await camera.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                    }
                }
            }).Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception)
        {
        }

        _frames?.Dispose();
        _lifetime.Dispose();
        _listen?.Dispose();
    }

    partial void OnModelReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDownload));
        OnPropertyChanged(nameof(ShowListen));
    }

    partial void OnIsListeningChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowListen));
        OnPropertyChanged(nameof(ShowStopListen));
    }

    private async Task ApplyCatalogAsync(CommandCatalog catalog, bool announce)
    {
        var pipeline = _pipeline;
        if (pipeline is not null)
        {
            try
            {
                await Task.Run(() => pipeline.ReloadCatalog(catalog));
            }
            catch (Exception exception)
            {
                _ui.Post(() => Status = exception.Message);
                return;
            }
        }

        _ui.Post(() =>
        {
            if (Volatile.Read(ref _closed) == 1)
            {
                return;
            }

            ShowPhrases(catalog);
            if (announce)
            {
                Status = "Commands reloaded.";
            }
        });
    }

    private void ShowPhrases(CommandCatalog catalog)
    {
        Commands.Clear();
        foreach (var command in catalog.Directives)
        {
            foreach (var phrase in command.Phrases)
            {
                Commands.Add(new CatalogPhraseRow(phrase));
            }
        }
    }

    private async Task StartPreviewAsync()
    {
        if (_camera is null)
        {
            return;
        }

        try
        {
            await _camera.StartAsync(_lifetime.Token);
            var ready = KeywordModelInstaller.IsInstalled();
            _ui.Post(() =>
            {
                ModelReady = ready;
                if (_store?.Loaded != true)
                {
                    return;
                }

                Status = ready
                    ? "Preview is running. Press Listen."
                    : "Preview is running. Download the keyword model, then press Listen.";
            });
        }
        catch (Exception exception)
        {
            _ui.Post(() => Status = exception.Message);
        }
    }

    private async Task RefreshModelAsync()
    {
        var ready = await Task.Run(KeywordModelInstaller.IsInstalled);
        _ui.Post(() => ModelReady = ready);
    }

    private void OnFrame(VideoFrame frame) => _frames?.Submit(frame);

    private void OnPainted(bool replaced)
    {
        if (Volatile.Read(ref _closed) == 1)
        {
            return;
        }

        if (replaced)
        {
            Preview = _frames?.Image;
        }

        if (_pcm is not null)
        {
            OverrunText = $"Capture overruns: {_pcm.OverrunCount}";
        }
    }

    private void OnLevel(float peak)
    {
        _latestLevel = peak;
        if (Interlocked.CompareExchange(ref _levelScheduled, 1, 0) != 0)
        {
            return;
        }

        _ui.Post(() =>
        {
            if (Volatile.Read(ref _closed) == 1)
            {
                return;
            }

            MicrophoneLevel = _latestLevel;
            if (_pcm is not null)
            {
                OverrunText = $"Capture overruns: {_pcm.OverrunCount}";
            }

            Interlocked.Exchange(ref _levelScheduled, 0);
        });
    }
}
